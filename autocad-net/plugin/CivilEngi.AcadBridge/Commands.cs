using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CivilEngi.AcadBridge
{
    internal static partial class Commands
    {
        // ------------------------------------------------------------ trang thai
        public static object Ping(Args a)
        {
            var r = new Dictionary<string, object>
            {
                ["bridge"] = Plugin.Version,
                ["pipe"] = Plugin.Server?.Name,
                ["pid"] = Process.GetCurrentProcess().Id,
            };
            try
            {
                MainThread.Run(() =>
                {
                    r["acad"] = Application.Version.ToString();
                    r["doc"] = Path.GetFileName(Application.DocumentManager.MdiActiveDocument?.Name ?? "");
                    r["cmdactive"] = Docs.CmdActive();
                    return 0;
                }, a.Int("timeout_ms", 3000));
                r["main_thread"] = "ok";
            }
            catch (BusyException ex) { r["main_thread"] = "busy: " + ex.Message; }
            return r;
        }

        public static object Status(Args a) => MainThread.Run(() =>
        {
            var dm = Application.DocumentManager;
            var docs = new List<object>();
            foreach (Document d in dm)
            {
                docs.Add(new Dictionary<string, object>
                {
                    ["name"] = Path.GetFileName(d.Name),
                    ["path"] = d.Name,
                    ["active"] = d == dm.MdiActiveDocument,
                    ["read_only"] = d.IsReadOnly,
                });
            }
            var doc = dm.MdiActiveDocument;
            var r = new Dictionary<string, object>
            {
                ["bridge"] = Plugin.Version,
                ["pid"] = Process.GetCurrentProcess().Id,
                ["pipe"] = Plugin.Server?.Name,
                ["docs"] = docs,
                ["active_doc"] = doc == null ? null : Path.GetFileName(doc.Name),
                ["cmdactive"] = Docs.CmdActive(),
                ["cmdnames"] = Docs.CmdNames(),
            };
            if (doc != null)
            {
                try { r["dbmod"] = Convert.ToInt32(Application.GetSystemVariable("DBMOD")); } catch { }
                try { r["layout"] = LayoutManager.Current.CurrentLayout; } catch { }
                try { r["tilemode"] = Convert.ToInt32(Application.GetSystemVariable("TILEMODE")); } catch { }
            }
            return r;
        });

        public static object DocActivate(Args a) => MainThread.Run(() =>
        {
            var d = Docs.Resolve(a);
            Application.DocumentManager.MdiActiveDocument = d;
            return Path.GetFileName(d.Name);
        });

        // ------------------------------------------------------------ layout
        public static object Layouts(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            var db = doc.Database;
            var list = new List<Dictionary<string, object>>();
            string cur = null;
            try { if (doc == Application.DocumentManager.MdiActiveDocument) cur = LayoutManager.Current.CurrentLayout; } catch { }
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry en in dict)
                {
                    var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead);
                    int nvp = 0;
                    if (!lo.ModelType) try { nvp = LayoutViewports(tr, db, lo.LayoutName).Count; } catch { }
                    list.Add(new Dictionary<string, object>
                    {
                        ["name"] = lo.LayoutName,
                        ["tab_order"] = lo.TabOrder,
                        ["current"] = lo.LayoutName == cur,
                        ["viewports"] = nvp,
                    });
                }
                tr.Commit();
            }
            return list.OrderBy(x => (int)x["tab_order"]).ToList();
        });

        public static object LayoutSet(Args a)
        {
            string name = a.ReqStr("name");
            var r = MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                if (doc != Application.DocumentManager.MdiActiveDocument) Application.DocumentManager.MdiActiveDocument = doc;
                Docs.EnsureIdle(doc, a);
                using (doc.LockDocument())
                    LayoutManager.Current.CurrentLayout = name;
                return LayoutManager.Current.CurrentLayout;
            });
            if (a.Bool("zoom_extents")) Zoom(new Args(new Dictionary<string, object> { ["mode"] = "extents" }));
            return r;
        }

        public static object LayoutRename(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            if (doc != Application.DocumentManager.MdiActiveDocument) Application.DocumentManager.MdiActiveDocument = doc;
            Docs.EnsureIdle(doc, a);
            using (doc.LockDocument())
                LayoutManager.Current.RenameLayout(a.ReqStr("old"), a.ReqStr("new"));
            return a.ReqStr("new");
        });

        /// <summary>Sap tab theo thu tu danh sach names (Model luon dau). Layout khong co trong danh sach giu sau cung.</summary>
        public static object LayoutReorder(Args a) => MainThread.Run(() =>
        {
            var names = a.StrList("names") ?? throw new BridgeException("Thieu 'names'");
            var doc = Docs.Resolve(a);
            Docs.EnsureIdle(doc, a);
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                var all = new List<Layout>();
                foreach (DBDictionaryEntry en in dict)
                {
                    var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead);
                    if (!lo.ModelType) all.Add(lo);
                }
                var ordered = names.Select(n => all.FirstOrDefault(l => l.LayoutName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                                   .Where(l => l != null).ToList();
                var missing = names.Where(n => !all.Any(l => l.LayoutName.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
                ordered.AddRange(all.Where(l => !ordered.Contains(l)).OrderBy(l => l.TabOrder));
                int i = 1;
                foreach (var lo in ordered)
                {
                    if (lo.TabOrder != i) { lo.UpgradeOpen(); lo.TabOrder = i; }
                    i++;
                }
                tr.Commit();
                try { doc.Editor.Regen(); } catch { }
                return new Dictionary<string, object> { ["order"] = ordered.Select(l => l.LayoutName).ToList(), ["missing"] = missing };
            }
        });

        // ------------------------------------------------------------ doc doi tuong
        static Dictionary<string, object> Info(Transaction tr, Entity e, bool details, string xdataApp, bool withBox = true)
        {
            var d = new Dictionary<string, object>
            {
                ["handle"] = U.H(e.ObjectId),
                ["type"] = e.GetRXClass().DxfName,
                ["layer"] = e.Layer,
            };
            if (withBox) { var x = U.Ext(e); if (x.HasValue) d["bbox"] = U.Box(x.Value); }
            switch (e)
            {
                case BlockReference br:
                    d["name"] = U.BlockName(tr, br);
                    d["insert"] = U.P(br.Position);
                    if (details)
                    {
                        d["rotation_deg"] = U.R(br.Rotation * 180 / Math.PI, 4);
                        d["scale"] = new[] { U.R(br.ScaleFactors.X, 4), U.R(br.ScaleFactors.Y, 4) };
                    }
                    if (br.AttributeCollection.Count > 0)
                    {
                        var at = new Dictionary<string, object>();
                        foreach (ObjectId aid in br.AttributeCollection)
                        {
                            if (aid.IsErased) continue;
                            var ar = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                            at[ar.Tag] = ar.TextString;
                        }
                        d["attributes"] = at;
                    }
                    if (details && br.IsDynamicBlock)
                    {
                        var dp = new Dictionary<string, object>();
                        foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                            if (p.PropertyName != "Origin") dp[p.PropertyName] = U.Clean(p.Value);
                        d["dynamic"] = dp;
                    }
                    break;
                case DBText t:
                    d["text"] = t.TextString;
                    d["insert"] = U.P(t.Position);
                    d["height"] = U.R(t.Height);
                    if (details) d["rotation_deg"] = U.R(t.Rotation * 180 / Math.PI, 4);
                    break;
                case MText m:
                    d["text"] = m.Text;
                    d["insert"] = U.P(m.Location);
                    d["height"] = U.R(m.TextHeight);
                    if (details) { d["contents"] = m.Contents; d["rotation_deg"] = U.R(m.Rotation * 180 / Math.PI, 4); }
                    break;
                case Line l:
                    d["start"] = U.P(l.StartPoint); d["end"] = U.P(l.EndPoint);
                    break;
                case Polyline pl:
                    d["closed"] = pl.Closed;
                    d["n"] = pl.NumberOfVertices;
                    if (details || pl.NumberOfVertices <= 8)
                    {
                        var pts = new List<double[]>();
                        for (int i = 0; i < Math.Min(pl.NumberOfVertices, 2000); i++) pts.Add(U.P(pl.GetPoint2dAt(i)));
                        d["points"] = pts;
                    }
                    break;
                case Circle c:
                    d["center"] = U.P(c.Center); d["radius"] = U.R(c.Radius);
                    break;
                case Arc ar2:
                    d["center"] = U.P(ar2.Center); d["radius"] = U.R(ar2.Radius);
                    if (details) { d["start_deg"] = U.R(ar2.StartAngle * 180 / Math.PI, 3); d["end_deg"] = U.R(ar2.EndAngle * 180 / Math.PI, 3); }
                    break;
                case Dimension dm:
                    try { d["measurement"] = U.R(dm.Measurement); } catch { }
                    if (!string.IsNullOrEmpty(dm.DimensionText)) d["text"] = dm.DimensionText;
                    break;
                case Hatch h:
                    d["pattern"] = h.PatternName;
                    if (details) { try { d["area"] = U.R(h.Area); } catch { } }
                    break;
                case Viewport vp:
                    d["number"] = vp.Number;
                    d["center"] = U.P(vp.CenterPoint);
                    d["width"] = U.R(vp.Width); d["height"] = U.R(vp.Height);
                    break;
            }
            if (details) d["color"] = e.ColorIndex;
            if (!string.IsNullOrEmpty(xdataApp))
            {
                ResultBuffer rb = xdataApp == "*" ? e.XData : e.GetXDataForApplication(xdataApp);
                if (rb != null)
                {
                    d["xdata"] = rb.AsArray().Select(tv => new object[] { (int)tv.TypeCode, U.Clean(tv.Value) }).ToList();
                    rb.Dispose();
                }
            }
            return d;
        }

        /// <summary>
        /// Quet doi tuong theo vung / layer / loai / ten block / chu - doc thang database, khong phu thuoc man hinh.
        /// args: doc, space(model|current|ten layout), bbox[x0,y0,x1,y1], mode(cross|inside|center),
        ///       layers[], types[] (DXF: LINE, LWPOLYLINE, INSERT, TEXT, MTEXT, DIMENSION, HATCH...), blocks[],
        ///       text (wildcard tren noi dung chu / thuoc tinh), xdata_app, has_xdata, details, limit, count_only
        /// </summary>
        public static object Query(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            var db = doc.Database;
            var bb = a.NumArr("bbox");
            string mode = (a.Str("mode") ?? "cross").ToLowerInvariant();
            var layers = U.Wilds(a.StrList("layers"));
            var types = U.Wilds(a.StrList("types"));
            var blocks = U.Wilds(a.StrList("blocks"));
            var textRx = a.Has("text") ? U.Wild(a.Str("text")) : null;
            string xapp = a.Str("xdata_app");
            string hasX = a.Str("has_xdata");
            bool details = a.Bool("details");
            bool countOnly = a.Bool("count_only");
            int limit = a.Int("limit", 5000);
            var items = new List<object>();
            var byType = new Dictionary<string, int>();
            var byLayer = new Dictionary<string, int>();
            int total = 0;
            if (bb != null && bb.Length < 4) throw new BridgeException("bbox can 4 so [x0,y0,x1,y1]");
            double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            if (bb != null) { x0 = Math.Min(bb[0], bb[2]); x1 = Math.Max(bb[0], bb[2]); y0 = Math.Min(bb[1], bb[3]); y1 = Math.Max(bb[1], bb[3]); }

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(Docs.Space(tr, db, a.Str("space")), OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased) continue;
                    string dxf = id.ObjectClass.DxfName;
                    if (!U.AnyMatch(types, dxf) && !U.AnyMatch(types, id.ObjectClass.Name)) continue;
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    if (!U.AnyMatch(layers, e.Layer)) continue;
                    if (blocks != null)
                    {
                        if (!(e is BlockReference br0) || !U.AnyMatch(blocks, U.BlockName(tr, br0))) continue;
                    }
                    if (hasX != null)
                    {
                        using (var rb = e.GetXDataForApplication(hasX)) if (rb == null) continue;
                    }
                    Extents3d? ext = null;
                    if (bb != null)
                    {
                        ext = U.Ext(e);
                        if (!ext.HasValue) continue;
                        var mn = ext.Value.MinPoint; var mx = ext.Value.MaxPoint;
                        bool ok;
                        if (mode == "inside") ok = mn.X >= x0 && mx.X <= x1 && mn.Y >= y0 && mx.Y <= y1;
                        else if (mode == "center") { double cx = (mn.X + mx.X) / 2, cy = (mn.Y + mx.Y) / 2; ok = cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1; }
                        else ok = mx.X >= x0 && mn.X <= x1 && mx.Y >= y0 && mn.Y <= y1;
                        if (!ok) continue;
                    }
                    if (textRx != null && !TextMatch(tr, e, textRx)) continue;
                    total++;
                    byType[dxf] = byType.TryGetValue(dxf, out var n1) ? n1 + 1 : 1;
                    byLayer[e.Layer] = byLayer.TryGetValue(e.Layer, out var n2) ? n2 + 1 : 1;
                    if (!countOnly && items.Count < limit) items.Add(Info(tr, e, details, xapp));
                }
                tr.Commit();
            }
            return new Dictionary<string, object>
            {
                ["total"] = total, ["returned"] = items.Count, ["truncated"] = total > items.Count && !countOnly,
                ["by_type"] = byType, ["by_layer"] = byLayer, ["items"] = items,
            };
        }, 15000, 600000);

        static bool TextMatch(Transaction tr, Entity e, System.Text.RegularExpressions.Regex rx)
        {
            switch (e)
            {
                case DBText t: return rx.IsMatch(t.TextString ?? "");
                case MText m: return rx.IsMatch(m.Text ?? "") || rx.IsMatch(m.Contents ?? "");
                case Dimension d: return rx.IsMatch(d.DimensionText ?? "");
                case BlockReference br:
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        if (aid.IsErased) continue;
                        var ar = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                        if (rx.IsMatch(ar.TextString ?? "")) return true;
                    }
                    return false;
            }
            return false;
        }

        public static object Get(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            var db = doc.Database;
            var hs = a.StrList("handles") ?? throw new BridgeException("Thieu 'handles'");
            var res = new List<object>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var h in hs)
                {
                    var id = U.Id(db, h);
                    if (id.IsNull) { res.Add(new Dictionary<string, object> { ["handle"] = h, ["error"] = "khong ton tai" }); continue; }
                    var o = tr.GetObject(id, OpenMode.ForRead);
                    if (o is Entity e) res.Add(Info(tr, e, a.Bool("details", true), a.Str("xdata_app")));
                    else res.Add(new Dictionary<string, object> { ["handle"] = h, ["type"] = o.GetRXClass().Name });
                }
                tr.Commit();
            }
            return res;
        });

        // ------------------------------------------------------------ sua doi tuong
        delegate void EntAction(Transaction tr, Entity e);

        static object ForEachEntity(Args a, EntAction act, bool lockLayers = true)
        {
            return MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                Docs.EnsureIdle(doc, a);
                var db = doc.Database;
                var hs = a.StrList("handles") ?? throw new BridgeException("Thieu 'handles'");
                int ok = 0;
                var fail = new Dictionary<string, string>();
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var h in hs)
                    {
                        var id = U.Id(db, h);
                        if (id.IsNull) { fail[h] = "khong ton tai"; continue; }
                        try
                        {
                            var e = tr.GetObject(id, OpenMode.ForWrite, false, true) as Entity;
                            if (e == null) { fail[h] = "khong phai entity"; continue; }
                            act(tr, e);
                            ok++;
                        }
                        catch (Exception ex) { fail[h] = ex.Message; }
                    }
                    tr.Commit();
                }
                return new Dictionary<string, object> { ["ok"] = ok, ["failed"] = fail };
            }, 15000, 600000);
        }

        /// <summary>Xoa theo handle. Nho quy tac: chi xoa doi tuong minh tao / da duoc user dong y.</summary>
        public static object Delete(Args a) => ForEachEntity(a, (tr, e) => e.Erase());

        public static object Move(Args a)
        {
            var v = new Vector3d(a.Num("dx"), a.Num("dy"), 0);
            var m = Matrix3d.Displacement(v);
            return ForEachEntity(a, (tr, e) => e.TransformBy(m));
        }

        /// <summary>Copy (deep clone, giu thuoc tinh block, xdata) roi dich dx,dy. Tra map handle cu -> moi.</summary>
        public static object Copy(Args a) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            Docs.EnsureIdle(doc, a);
            var db = doc.Database;
            var hs = a.StrList("handles") ?? throw new BridgeException("Thieu 'handles'");
            var m = Matrix3d.Displacement(new Vector3d(a.Num("dx"), a.Num("dy"), 0));
            var map = new Dictionary<string, object>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = hs.Select(h => U.Id(db, h)).Where(i => !i.IsNull).ToList();
                foreach (var grp in ids.GroupBy(i => ((Entity)tr.GetObject(i, OpenMode.ForRead)).OwnerId))
                {
                    var col = new ObjectIdCollection(grp.ToArray());
                    var idm = new IdMapping();
                    db.DeepCloneObjects(col, grp.Key, idm, false);
                    foreach (IdPair p in idm)
                    {
                        if (!p.IsPrimary || !p.IsCloned) continue;
                        if (tr.GetObject(p.Value, OpenMode.ForWrite) is Entity ne)
                        {
                            ne.TransformBy(m);
                            map[U.H(p.Key)] = U.H(p.Value);
                        }
                    }
                }
                tr.Commit();
            }
            return map;
        }, 15000, 600000);

        public static object SetProps(Args a)
        {
            string layer = a.Str("layer");
            int? color = a.Has("color") ? a.Int("color") : (int?)null;
            string lt = a.Str("linetype");
            return MainThread.Run(() =>
            {
                if (layer != null)
                {
                    var doc = Docs.Resolve(a);
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    { U.EnsureLayer(tr, doc.Database, layer); tr.Commit(); }
                }
                return ForEachEntity(a, (tr, e) =>
                {
                    if (layer != null) e.Layer = layer;
                    if (color.HasValue) e.ColorIndex = color.Value;
                    if (lt != null) e.Linetype = lt;
                });
            });
        }

        public static object SetText(Args a)
        {
            string text = a.ReqStr("text");
            return ForEachEntity(a, (tr, e) =>
            {
                switch (e)
                {
                    case DBText t: t.TextString = text; break;
                    case MText m: m.Contents = text; break;
                    case Dimension d: d.DimensionText = text; break;
                    default: throw new BridgeException("khong phai TEXT/MTEXT/DIMENSION");
                }
            });
        }

        /// <summary>Sua thuoc tinh block: values {TAG: gia tri}.</summary>
        public static object SetAttributes(Args a)
        {
            var vals = a.Dict("values") ?? throw new BridgeException("Thieu 'values'");
            return ForEachEntity(a, (tr, e) =>
            {
                if (!(e is BlockReference br)) throw new BridgeException("khong phai block");
                foreach (ObjectId aid in br.AttributeCollection)
                {
                    var ar = (AttributeReference)tr.GetObject(aid, OpenMode.ForWrite);
                    foreach (var kv in vals)
                        if (kv.Key.Equals(ar.Tag, StringComparison.OrdinalIgnoreCase))
                            ar.TextString = Convert.ToString(kv.Value);
                }
                if (br.IsDynamicBlock)
                    foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                        foreach (var kv in vals)
                            if (kv.Key.Equals(p.PropertyName, StringComparison.OrdinalIgnoreCase) && !p.ReadOnly)
                                p.Value = ConvertLike(p.Value, kv.Value);
            });
        }

        internal static object ConvertLike(object current, object v)
        {
            switch (current)
            {
                case double _: return Args.ToDouble(v);
                case short _: return Convert.ToInt16(Args.ToDouble(v));
                case int _: return Convert.ToInt32(Args.ToDouble(v));
                default: return Convert.ToString(v);
            }
        }

        /// <summary>Thay toan bo XData cua 1 app: values [[code, value], ...] (khong gom dong 1001). values rong = xoa.</summary>
        public static object SetXData(Args a)
        {
            string app = a.ReqStr("app");
            var vals = a.List("values") ?? new List<object>();
            return MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var rat = (RegAppTable)tr.GetObject(doc.Database.RegAppTableId, OpenMode.ForRead);
                    if (!rat.Has(app))
                    {
                        rat.UpgradeOpen();
                        var r = new RegAppTableRecord { Name = app };
                        rat.Add(r); tr.AddNewlyCreatedDBObject(r, true);
                    }
                    tr.Commit();
                }
                return ForEachEntity(a, (tr, e) =>
                {
                    var tvs = new List<TypedValue> { new TypedValue(1001, app) };
                    foreach (var item in vals)
                    {
                        var pair = ((System.Collections.IEnumerable)item).Cast<object>().ToArray();
                        int code = (int)Args.ToDouble(pair[0]);
                        object v = pair[1];
                        object val;
                        if (code == 1070) val = Convert.ToInt16(Args.ToDouble(v));
                        else if (code == 1071) val = Convert.ToInt32(Args.ToDouble(v));
                        else if (code >= 1040 && code <= 1042) val = Args.ToDouble(v);
                        else if (code >= 1010 && code <= 1013) val = U.Pt(v);
                        else val = Convert.ToString(v);
                        tvs.Add(new TypedValue(code, val));
                    }
                    using (var rb = new ResultBuffer(tvs.ToArray())) e.XData = rb;
                });
            });
        }
    }
}
