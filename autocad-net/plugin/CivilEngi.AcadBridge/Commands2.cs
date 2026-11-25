using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CivilEngi.AcadBridge
{
    internal static partial class Commands
    {
        // ------------------------------------------------------------ ve (gop nhieu doi tuong / 1 lan goi)
        /// <summary>
        /// items: [{kind: line|polyline|text|mtext|block|dim_aligned|circle, ...}], space, layer (mac dinh), color.
        /// line: start,end | polyline: points,closed | text: text,at,height,rotation_deg,align(left|center|middle)
        /// mtext: text,at,height,width | block: name,at,rotation_deg,scale,attributes{},dynamic{}
        /// dim_aligned: p1,p2,at,text | circle: center,radius. Moi item co the co layer, color rieng.
        /// </summary>
        public static object Draw(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            Docs.EnsureIdle(doc, a);
            var db = doc.Database;
            var items = a.List("items") ?? throw new BridgeException("Thieu 'items'");
            var handles = new List<object>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(Docs.Space(tr, db, a.Str("space")), OpenMode.ForWrite);
                foreach (var o in items)
                {
                    var it = new Args(o);
                    Entity e;
                    string kind = (it.Str("kind") ?? "").ToLowerInvariant();
                    switch (kind)
                    {
                        case "line":
                            e = new Line(U.Pt(it.NumArr("start")), U.Pt(it.NumArr("end")));
                            break;
                        case "polyline":
                            {
                                var pl = new Polyline();
                                var pts = it.List("points") ?? throw new BridgeException("polyline thieu points");
                                for (int i = 0; i < pts.Count; i++)
                                {
                                    var p = Args.ToNumArr(pts[i]);
                                    pl.AddVertexAt(i, new Point2d(p[0], p[1]), 0, 0, 0);
                                }
                                pl.Closed = it.Bool("closed");
                                e = pl;
                                break;
                            }
                        case "text":
                            {
                                var t = new DBText
                                {
                                    TextString = it.ReqStr("text"),
                                    Position = U.Pt(it.NumArr("at")),
                                    Height = it.Num("height", 250),
                                    Rotation = it.Num("rotation_deg") * Math.PI / 180,
                                };
                                if (t.Height <= 0) t.Height = 250;
                                t.TextStyleId = db.Textstyle;
                                string al = (it.Str("align") ?? "left").ToLowerInvariant();
                                if (al == "center" || al == "middle")
                                {
                                    t.HorizontalMode = TextHorizontalMode.TextCenter;
                                    if (al == "middle") t.VerticalMode = TextVerticalMode.TextVerticalMid;
                                    t.AlignmentPoint = t.Position;
                                }
                                e = t;
                                break;
                            }
                        case "mtext":
                            e = new MText
                            {
                                Contents = it.ReqStr("text"),
                                Location = U.Pt(it.NumArr("at")),
                                TextHeight = it.Num("height", 250),
                                Width = it.Num("width", 0),
                                Rotation = it.Num("rotation_deg") * Math.PI / 180,
                                TextStyleId = db.Textstyle,
                            };
                            break;
                        case "circle":
                            e = new Circle(U.Pt(it.NumArr("center")), Vector3d.ZAxis, it.ReqNum("radius"));
                            break;
                        case "dim_aligned":
                            e = new AlignedDimension(U.Pt(it.NumArr("p1")), U.Pt(it.NumArr("p2")), U.Pt(it.NumArr("at")), it.Str("text") ?? "", db.Dimstyle);
                            break;
                        case "block":
                            e = MakeBlock(tr, db, btr, it);
                            break;
                        default:
                            throw new BridgeException("kind khong ho tro: " + kind);
                    }
                    string layer = it.Str("layer") ?? a.Str("layer");
                    if (!(e is BlockReference))
                    {
                        e.SetDatabaseDefaults(db);
                        if (layer != null) { U.EnsureLayer(tr, db, layer); e.Layer = layer; }
                        if (it.Has("color") || a.Has("color")) e.ColorIndex = it.Has("color") ? it.Int("color") : a.Int("color");
                        btr.AppendEntity(e);
                        tr.AddNewlyCreatedDBObject(e, true);
                        if (e is DBText dt && dt.HorizontalMode != TextHorizontalMode.TextLeft) dt.AdjustAlignment(db);
                    }
                    else
                    {
                        if (layer != null) { U.EnsureLayer(tr, db, layer); e.Layer = layer; }
                        if (it.Has("color") || a.Has("color")) e.ColorIndex = it.Has("color") ? it.Int("color") : a.Int("color");
                    }
                    handles.Add(U.H(e.ObjectId));
                }
                tr.Commit();
            }
            return handles;
        }, 15000, 600000);

        static Entity MakeBlock(Transaction tr, Database db, BlockTableRecord space, Args it)
        {
            string name = it.ReqStr("name");
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(name)) throw new BridgeException("Ban ve khong co block '" + name + "'");
            var def = (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead);
            double sc = it.Num("scale", 1);
            var br = new BlockReference(U.Pt(it.NumArr("at")), def.ObjectId)
            {
                Rotation = it.Num("rotation_deg") * Math.PI / 180,
                ScaleFactors = new Scale3d(sc),
            };
            br.SetDatabaseDefaults(db);
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            var attrs = it.Dict("attributes") ?? new Dictionary<string, object>();
            if (def.HasAttributeDefinitions)
            {
                foreach (ObjectId id in def)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;
                    var ar = new AttributeReference();
                    ar.SetAttributeFromBlock(ad, br.BlockTransform);
                    foreach (var kv in attrs)
                        if (kv.Key.Equals(ad.Tag, StringComparison.OrdinalIgnoreCase)) ar.TextString = Convert.ToString(kv.Value);
                    br.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                }
            }
            var dyn = it.Dict("dynamic");
            if (dyn != null && br.IsDynamicBlock)
                foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                    foreach (var kv in dyn)
                        if (kv.Key.Equals(p.PropertyName, StringComparison.OrdinalIgnoreCase) && !p.ReadOnly)
                            p.Value = ConvertLike(p.Value, kv.Value);
            return br;
        }

        // ------------------------------------------------------------ viewport / soat sheet
        static Dictionary<string, object> VpInfo(Viewport vp)
        {
            double s = vp.CustomScale > 0 ? vp.CustomScale : 1;
            double hw = vp.Width / 2 / s, hh = vp.Height / 2 / s;
            double cx = vp.ViewCenter.X + vp.ViewTarget.X, cy = vp.ViewCenter.Y + vp.ViewTarget.Y;
            var d = new Dictionary<string, object>
            {
                ["handle"] = U.H(vp.ObjectId),
                ["number"] = vp.Number,
                ["layer"] = vp.Layer,
                ["center"] = U.P(vp.CenterPoint),
                ["width"] = U.R(vp.Width), ["height"] = U.R(vp.Height),
                ["paper_bbox"] = new[] { U.R(vp.CenterPoint.X - vp.Width / 2), U.R(vp.CenterPoint.Y - vp.Height / 2), U.R(vp.CenterPoint.X + vp.Width / 2), U.R(vp.CenterPoint.Y + vp.Height / 2) },
                ["view_center"] = new[] { U.R(cx), U.R(cy) },
                ["custom_scale"] = U.R(vp.CustomScale, 8),
                ["scale_1_to"] = U.R(1 / s, 3),
                ["twist_deg"] = U.R(vp.TwistAngle * 180 / Math.PI, 4),
                ["locked"] = vp.Locked,
                ["on"] = vp.On,
                ["clipped"] = vp.NonRectClipOn,
                ["model_bbox"] = new[] { U.R(cx - hw), U.R(cy - hh), U.R(cx + hw), U.R(cy + hh) },
            };
            return d;
        }

        internal static List<Viewport> LayoutViewports(Transaction tr, Database db, string layout)
        {
            var lo = Docs.FindLayout(tr, db, layout);
            var btr = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead);
            var res = new List<Viewport>();
            bool first = true;
            foreach (ObjectId id in btr)
            {
                if (id.IsErased || id.ObjectClass.DxfName != "VIEWPORT") continue;
                var vp = (Viewport)tr.GetObject(id, OpenMode.ForRead);
                // Viewport dau tien trong layout = khung giay (Number = 1 khi layout da mo, -1 khi chua mo trong phien)
                if (first) { first = false; continue; }
                if (vp.Number == 1) continue;
                res.Add(vp);
            }
            return res;
        }

        /// <summary>Viewport cua 1 hay nhieu layout (names[] hoac layout; bo trong = tat ca), kem vung model nhin thay.</summary>
        public static object Viewports(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            var db = doc.Database;
            var names = a.StrList("layouts") ?? (a.Has("layout") ? new List<string> { a.Str("layout") } : null);
            var res = new Dictionary<string, object>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (names == null)
                {
                    names = new List<string>();
                    var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry en in dict) if (en.Key != "Model") names.Add(en.Key);
                }
                foreach (var n in names)
                    res[n] = LayoutViewports(tr, db, n).Select(VpInfo).Cast<object>().ToList();
                tr.Commit();
            }
            return res;
        });

        /// <summary>Sua viewport: center[x,y], width, height (giay); view_center[x,y] (model); custom_scale; locked.</summary>
        public static object ViewportSet(Args a)
        {
            return ForEachEntity(a, (tr, e) =>
            {
                if (!(e is Viewport vp)) throw new BridgeException("khong phai viewport");
                bool wasLocked = vp.Locked;
                if (wasLocked) vp.Locked = false;
                if (a.Has("center")) vp.CenterPoint = U.Pt(a.NumArr("center"));
                if (a.Has("width")) vp.Width = a.Num("width");
                if (a.Has("height")) vp.Height = a.Num("height");
                if (a.Has("custom_scale")) vp.CustomScale = a.Num("custom_scale");
                if (a.Has("view_center"))
                {
                    var c = a.NumArr("view_center");
                    vp.ViewCenter = new Point2d(c[0] - vp.ViewTarget.X, c[1] - vp.ViewTarget.Y);
                }
                vp.Locked = a.Has("locked") ? a.Bool("locked") : wasLocked;
            });
        }

        /// <summary>
        /// Soat sheet: tim doi tuong model bi vien viewport CAT (1 phan trong, 1 phan ngoai) – ten dam, chu MC, khung...
        /// args: layouts[] / layout (trong = tat ca), types (mac dinh TEXT,MTEXT,INSERT), layers, blocks, tol (mm model).
        /// </summary>
        public static object ViewportCutCheck(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            var db = doc.Database;
            var names = a.StrList("layouts") ?? (a.Has("layout") ? new List<string> { a.Str("layout") } : null);
            var types = U.Wilds(a.StrList("types") ?? new List<string> { "TEXT", "MTEXT", "INSERT" });
            var layers = U.Wilds(a.StrList("layers"));
            var blocks = U.Wilds(a.StrList("blocks"));
            double tol = a.Num("tol", 1);
            double maxSize = a.Num("max_size", 0);               // >0: bo qua doi tuong bbox lon hon (vd block thep san)
            var skipLabels = U.Wilds(a.StrList("skip_labels"));  // vd ["*ThepSan*"]
            var res = new Dictionary<string, object>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                // lay truoc danh sach doi tuong model + bbox
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var ents = new List<(string h, string type, string layer, string label, Extents3d x)>();
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !U.AnyMatch(types, id.ObjectClass.DxfName)) continue;
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || !U.AnyMatch(layers, e.Layer)) continue;
                    string label = null;
                    if (e is BlockReference br)
                    {
                        label = U.BlockName(tr, br);
                        if (blocks != null && !U.AnyMatch(blocks, label)) continue;
                        foreach (ObjectId aid in br.AttributeCollection)
                        {
                            if (aid.IsErased) continue;
                            var ar = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                            if (!string.IsNullOrWhiteSpace(ar.TextString)) { label += " | " + ar.TextString; break; }
                        }
                    }
                    else if (e is DBText t) label = t.TextString;
                    else if (e is MText m) label = m.Text;
                    var x = U.Ext(e);
                    if (!x.HasValue) continue;
                    if (maxSize > 0 && (x.Value.MaxPoint.X - x.Value.MinPoint.X > maxSize || x.Value.MaxPoint.Y - x.Value.MinPoint.Y > maxSize)) continue;
                    if (skipLabels != null && skipLabels.Any(r => r.IsMatch(label ?? ""))) continue;
                    ents.Add((U.H(id), id.ObjectClass.DxfName, e.Layer, label, x.Value));
                }
                if (names == null)
                {
                    names = new List<string>();
                    var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry en in dict) if (en.Key != "Model") names.Add(en.Key);
                }
                foreach (var n in names)
                {
                    var cuts = new List<object>();
                    foreach (var vp in LayoutViewports(tr, db, n))
                    {
                        if (Math.Abs(vp.TwistAngle) > 1e-9) continue;
                        var mb = (double[])VpInfo(vp)["model_bbox"];
                        foreach (var en in ents)
                        {
                            var mn = en.x.MinPoint; var mx = en.x.MaxPoint;
                            bool touch = mx.X > mb[0] && mn.X < mb[2] && mx.Y > mb[1] && mn.Y < mb[3];
                            if (!touch) continue;
                            bool inside = mn.X >= mb[0] - tol && mx.X <= mb[2] + tol && mn.Y >= mb[1] - tol && mx.Y <= mb[3] + tol;
                            if (inside) continue;
                            // phan nam trong view
                            double ix = Math.Max(0, Math.Min(mx.X, mb[2]) - Math.Max(mn.X, mb[0]));
                            double iy = Math.Max(0, Math.Min(mx.Y, mb[3]) - Math.Max(mn.Y, mb[1]));
                            double area = Math.Max(1e-9, (mx.X - mn.X) * (mx.Y - mn.Y));
                            cuts.Add(new Dictionary<string, object>
                            {
                                ["viewport"] = U.H(vp.ObjectId),
                                ["handle"] = en.h, ["type"] = en.type, ["layer"] = en.layer, ["label"] = en.label,
                                ["bbox"] = U.Box(en.x),
                                ["visible_ratio"] = U.R(ix * iy / area, 3),
                            });
                        }
                    }
                    res[n] = cuts;
                }
                tr.Commit();
            }
            return res;
        }, 15000, 600000);

        /// <summary>
        /// Tim polyline kin chong nhau (vd khung view Defpoints trung sau khi VD lai).
        /// args: space, layers (mac dinh Defpoints), bbox, min_ratio (0.5). Tra cap chong + handle cu hon (de xoa neu user dong y).
        /// </summary>
        public static object FindOverlaps(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            var db = doc.Database;
            var layers = U.Wilds(a.StrList("layers") ?? new List<string> { "Defpoints" });
            var bb = a.NumArr("bbox");
            double minR = a.Num("min_ratio", 0.5);
            var fr = new List<(long h, double[] b)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(Docs.Space(tr, db, a.Str("space")), OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased || id.ObjectClass.DxfName != "LWPOLYLINE") continue;
                    var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                    if (!pl.Closed || !U.AnyMatch(layers, pl.Layer)) continue;
                    var x = U.Ext(pl);
                    if (!x.HasValue) continue;
                    var b = U.Box(x.Value);
                    if (bb != null && (b[2] < bb[0] || b[0] > bb[2] || b[3] < bb[1] || b[1] > bb[3])) continue;
                    fr.Add((id.Handle.Value, b));
                }
                tr.Commit();
            }
            var pairs = new List<object>();
            var older = new HashSet<string>();
            fr.Sort((p, q) => p.b[0].CompareTo(q.b[0]));
            for (int i = 0; i < fr.Count; i++)
                for (int j = i + 1; j < fr.Count && fr[j].b[0] < fr[i].b[2]; j++)
                {
                    var A = fr[i].b; var B = fr[j].b;
                    double w = Math.Min(A[2], B[2]) - Math.Max(A[0], B[0]);
                    double h = Math.Min(A[3], B[3]) - Math.Max(A[1], B[1]);
                    if (w <= 0 || h <= 0) continue;
                    double r = w * h / Math.Min((A[2] - A[0]) * (A[3] - A[1]), (B[2] - B[0]) * (B[3] - B[1]));
                    if (r < minR) continue;
                    string ha = fr[i].h.ToString("X"), hb = fr[j].h.ToString("X");
                    string old = fr[i].h < fr[j].h ? ha : hb;
                    older.Add(old);
                    pairs.Add(new Dictionary<string, object> { ["a"] = ha, ["a_bbox"] = A, ["b"] = hb, ["b_bbox"] = B, ["ratio"] = U.R(r, 3), ["older"] = old });
                }
            return new Dictionary<string, object> { ["frames"] = fr.Count, ["pairs"] = pairs, ["older_handles"] = older.ToList() };
        });

        // ------------------------------------------------------------ DCE: so cau kien (XData DcePro truong 4)
        static readonly Regex RxName = new Regex(@"\(0\)_(.*?)\(1\)", RegexOptions.Compiled);
        static readonly Regex RxF4 = new Regex(@"\(4\)_(\d+)\(", RegexOptions.Compiled);

        /// <summary>
        /// Nhan / dat so cau kien DCE (thay sua_sck.py). args: x0, x1 (loc theo tam bbox), factor (he so nhan),
        /// set {TEN: so} (dat thang cho cau kien TEN), run (false = chi xem truoc), space.
        /// Bo qua Dimension (truong 4 cua dim la chieu dai) va ten bat dau bang 'R'.
        /// </summary>
        public static object DceSck(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            bool run = a.Bool("run");
            if (run) Docs.EnsureIdle(doc, a);
            var db = doc.Database;
            double x0 = a.Num("x0", double.MinValue), x1 = a.Num("x1", double.MaxValue);
            int k = a.Int("factor", 1);
            var fix = a.Dict("set") ?? new Dictionary<string, object>();
            string NewVal(string name, string old) =>
                fix.TryGetValue(name, out var v) ? Convert.ToString(v) : (long.Parse(old) * k).ToString();
            var cnt = new Dictionary<string, int>();
            var log = new HashSet<string>();
            int changed = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(Docs.Space(tr, db, a.Str("space")), OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased) continue;
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || e is Dimension) continue;
                    var rb = e.XData;
                    if (rb == null) continue;
                    var tv = rb.AsArray();
                    rb.Dispose();
                    if (!tv.Any(t => t.TypeCode == 1001 && Convert.ToString(t.Value) == "DcePro")) continue;
                    var x = U.Ext(e);
                    if (!x.HasValue) continue;
                    double cx = (x.Value.MinPoint.X + x.Value.MaxPoint.X) / 2;
                    if (!(cx >= x0 && cx < x1)) continue;
                    bool ok = false; string name = null;
                    for (int i = 0; i < tv.Length; i++)
                    {
                        if (!(tv[i].Value is string s)) continue;
                        if (s.StartsWith("(0)_"))
                        {
                            var m = RxName.Match(s); name = m.Success ? m.Groups[1].Value : null;
                            if (name != null && name.StartsWith("R")) name = null;
                            var m4 = RxF4.Match(s);
                            if (name != null && m4.Success)
                            {
                                var nv = NewVal(name, m4.Groups[1].Value);
                                if (nv != m4.Groups[1].Value)
                                {
                                    tv[i] = new TypedValue(tv[i].TypeCode, s.Substring(0, m4.Groups[1].Index) + nv + s.Substring(m4.Groups[1].Index + m4.Groups[1].Length));
                                    ok = true; log.Add($"{name}: {m4.Groups[1].Value} -> {nv}");
                                }
                            }
                        }
                        if (i > 0 && Convert.ToString(tv[i - 1].Value) == "LuuThongSoChung_SYS" && name != null)
                        {
                            var p = s.Split('∞');
                            if (p.Length > 4 && long.TryParse(p[4].Trim(), out _))
                            {
                                var nv = NewVal(name, p[4].Trim());
                                if (nv != p[4].Trim()) { p[4] = nv + " "; tv[i] = new TypedValue(tv[i].TypeCode, string.Join("∞", p)); ok = true; }
                            }
                        }
                    }
                    if (!ok) continue;
                    string key = e is BlockReference br ? U.BlockName(tr, br) : e.GetRXClass().DxfName;
                    cnt[key] = cnt.TryGetValue(key, out var c) ? c + 1 : 1;
                    changed++;
                    if (run)
                    {
                        e.UpgradeOpen();
                        using (var nrb = new ResultBuffer(tv)) e.XData = nrb;
                    }
                }
                tr.Commit();
            }
            return new Dictionary<string, object> { ["run"] = run, ["changed"] = changed, ["by_kind"] = cnt, ["changes"] = log.OrderBy(s => s).ToList() };
        }, 15000, 600000);

        // ------------------------------------------------------------ man hinh, bien, luu
        /// <summary>mode: extents (mac dinh) | window (bbox[x0,y0,x1,y1]) | center (center[x,y], height). Ap cho ban ve/khong gian hien hanh.</summary>
        public static object Zoom(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            if (doc != Application.DocumentManager.MdiActiveDocument) Application.DocumentManager.MdiActiveDocument = doc;
            dynamic acad = Application.AcadApplication;
            string mode = (a.Str("mode") ?? "extents").ToLowerInvariant();
            if (mode == "window")
            {
                var b = a.NumArr("bbox") ?? throw new BridgeException("Thieu bbox");
                acad.ZoomWindow(new double[] { b[0], b[1], 0 }, new double[] { b[2], b[3], 0 });
            }
            else if (mode == "center")
            {
                var c = a.NumArr("center") ?? throw new BridgeException("Thieu center");
                acad.ZoomCenter(new double[] { c[0], c[1], 0 }, a.Num("height", 10000));
            }
            else acad.ZoomExtents();
            return mode;
        });

        public static object Regen(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            using (doc.LockDocument()) doc.Editor.Regen();
            return "ok";
        });

        public static object VarGet(Args a) => MainThread.Run(() =>
        {
            var names = a.StrList("names") ?? throw new BridgeException("Thieu 'names'");
            var r = new Dictionary<string, object>();
            foreach (var n in names)
            {
                try { r[n] = U.Clean(Application.GetSystemVariable(n)); }
                catch (Exception ex) { r[n] = "LOI: " + ex.Message; }
            }
            return r;
        });

        public static object VarSet(Args a) => MainThread.Run(() =>
        {
            string n = a.ReqStr("name");
            object cur = Application.GetSystemVariable(n);
            object v = a.Raw("value");
            object val = cur is short ? (object)Convert.ToInt16(Args.ToDouble(v))
                       : cur is int ? Convert.ToInt32(Args.ToDouble(v))
                       : cur is double ? Args.ToDouble(v)
                       : Convert.ToString(v);
            var doc = Application.DocumentManager.MdiActiveDocument;
            using (doc?.LockDocument()) Application.SetSystemVariable(n, val);
            return U.Clean(Application.GetSystemVariable(n));
        });

        /// <summary>Luu ban ve (QSAVE). Quy tac: chi luu 1 lan cuoi dot viec, backup truoc khi sua.</summary>
        public static object Save(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            Docs.EnsureIdle(doc, a);
            dynamic adoc = DocumentExtension.GetAcadDocument(doc);
            adoc.Save();
            return doc.Name;
        }, 15000, 600000);

        /// <summary>
        /// Backup = copy file .dwg dang luu tren dia sang path (hoac thu muc dir / mac dinh &lt;thu muc dwg&gt;\_backup).
        /// Neu ban ve dang co thay doi chua luu (DBMOD != 0) thi canh bao: ban backup la ban da luu gan nhat.
        /// </summary>
        public static object Backup(Args a)
        {
            var info = MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                int dbmod = 0;
                if (doc == Application.DocumentManager.MdiActiveDocument)
                    try { dbmod = Convert.ToInt32(Application.GetSystemVariable("DBMOD")); } catch { }
                return (doc.Name, dbmod);
            });
            string src = info.Item1;
            if (!File.Exists(src)) throw new BridgeException("Ban ve chua tung luu ra dia: " + src);
            string path = a.Str("path");
            if (string.IsNullOrEmpty(path))
            {
                string dir = a.Str("dir") ?? Path.Combine(Path.GetDirectoryName(src), "_backup");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, Path.GetFileNameWithoutExtension(src) + "_" + DateTime.Now.ToString("yyMMdd_HHmmss") + ".dwg");
            }
            else Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Copy(src, path, false);
            return new Dictionary<string, object>
            {
                ["backup"] = path,
                ["bytes"] = new FileInfo(path).Length,
                ["warning"] = info.Item2 != 0 ? "Ban ve co thay doi CHUA LUU (DBMOD=" + info.Item2 + "): backup la ban da luu gan nhat tren dia." : null,
            };
        }

        // ------------------------------------------------------------ tuong tac voi user
        static T InCommandContext<T>(Func<Editor, T> f, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<T>();
            MainThread.Run(() =>
            {
                Application.DocumentManager.ExecuteInCommandContextAsync(o =>
                {
                    try { tcs.TrySetResult(f(Application.DocumentManager.MdiActiveDocument.Editor)); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                    return Task.CompletedTask;
                }, null);
                return 0;
            });
            if (!tcs.Task.Wait(timeoutMs)) throw new BusyException("Het thoi gian cho nguoi dung thao tac");
            return tcs.Task.Result;
        }

        /// <summary>Nho user click count diem tren man hinh (Esc = dung).</summary>
        public static object PickPoints(Args a)
        {
            int count = Math.Max(1, a.Int("count", 1));
            string prompt = a.Str("prompt") ?? "Chon diem";
            return InCommandContext(ed =>
            {
                var pts = new List<object>();
                Point3d? last = null;
                for (int i = 0; i < count; i++)
                {
                    var o = new PromptPointOptions($"\n{prompt} {i + 1}/{count}: ") { AllowNone = true };
                    if (last.HasValue) { o.UseBasePoint = true; o.BasePoint = last.Value; }
                    var r = ed.GetPoint(o);
                    if (r.Status != PromptStatus.OK) break;
                    var w = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    last = r.Value;
                    pts.Add(U.P(w));
                }
                return pts;
            }, a.Int("timeout_s", 300) * 1000);
        }

        /// <summary>Nho user chon doi tuong; tra thong tin tung doi tuong.</summary>
        public static object SelectOnScreen(Args a)
        {
            string prompt = a.Str("prompt") ?? "Chon doi tuong";
            string xapp = a.Str("xdata_app");
            return InCommandContext(ed =>
            {
                var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n" + prompt + ": " });
                var res = new List<object>();
                if (r.Status != PromptStatus.OK) return res;
                var db = ed.Document.Database;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var id in r.Value.GetObjectIds())
                        if (tr.GetObject(id, OpenMode.ForRead) is Entity e) res.Add(Info(tr, e, true, xapp));
                    tr.Commit();
                }
                return res;
            }, a.Int("timeout_s", 300) * 1000);
        }
    }
}
