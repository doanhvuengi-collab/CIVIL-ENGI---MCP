using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CivilEngi.AcadBridge
{
    /// <summary>
    /// Dan trang chi tiet dam DCE gom trong 1 lenh: TAOMV 1 tang -> dat ten to + khung ten -> soat/sua tung to
    /// (net ngan DCE_NetKhuat, ten dam bi view cat, dam vat 2 to, view noi ngan, view chong / ngoai khung)
    /// -> chi tra danh sach loi ngan + anh cac to co canh bao.
    ///
    /// Quy tac cung (user): KHONG dong vao zigzag / net cat trong MODEL; KHONG xoa viewport; chi xoa net ngan
    /// cua to 1 dam; backup truoc, chi luu khi save=true (1 lan cuoi dot).
    /// </summary>
    internal static class DanTrang
    {
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.###", IC);

        // ------------------------------------------------------------ hop chu nhat
        internal struct Box
        {
            public double X0, Y0, X1, Y1;
            public Box(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public Box(Extents3d e) : this(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y) { }
            public double W => X1 - X0;
            public double H => Y1 - Y0;
            public double CX => (X0 + X1) / 2;
            public double CY => (Y0 + Y1) / 2;
            public double Area => Math.Max(0, W) * Math.Max(0, H);
            public bool Touch(Box b) => X1 > b.X0 && X0 < b.X1 && Y1 > b.Y0 && Y0 < b.Y1;
            public bool Inside(Box b, double tol) => X0 >= b.X0 - tol && X1 <= b.X1 + tol && Y0 >= b.Y0 - tol && Y1 <= b.Y1 + tol;
            public bool HasPt(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
            public double InterArea(Box b) => Math.Max(0, Math.Min(X1, b.X1) - Math.Max(X0, b.X0)) * Math.Max(0, Math.Min(Y1, b.Y1) - Math.Max(Y0, b.Y0));
            public Box Shift(double dx, double dy) => new Box(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy);
            public Box Union(Box b) => new Box(Math.Min(X0, b.X0), Math.Min(Y0, b.Y0), Math.Max(X1, b.X1), Math.Max(Y1, b.Y1));
            public double[] Arr() => new[] { U.R(X0), U.R(Y0), U.R(X1), U.R(Y1) };
        }

        class Vp
        {
            public string Layout, Handle;
            public Box Paper, Model;
            public string Beam;       // khoa dam (handle khung Defpoints) neu xac dinh duoc
        }

        class Ent
        {
            public ObjectId Id; public string Handle, Type, Layer, Label; public Box B;
        }

        internal class Opts
        {
            public bool Fix;                       // false = chi bao loi
            public bool FixNames = true;           // doi ten dam bi view cat
            public bool NetKhuat = true;           // sua / xoa net ngan
            public double NetLts = 0.5, NetWidth = 0.3;
            public string NetLayer = "DCE_NetKhuat";
            public List<string> NameBlocks = new List<string> { "Dce_KhTenDam" };
            public List<string> SkipLayers = new List<string> { "DCE_MCZiCZac", "Defpoints" };
            public List<string> SkipLabels = new List<string> { "*ThepSan*" };
            public string FrameLayer = "Defpoints";
            public double MaxSize = 20000;         // bo qua doi tuong model co bbox lon hon (block thep san...)
            public double[] PaperFrame = { 20, 6, 532.5, 414 };
            public double ShortRatio = 0.35;       // view noi < 35% view truoc = ngan
            public double Tol = 1;

            public static Opts From(Args a, bool fixDefault)
            {
                var o = new Opts { Fix = a.Has("fix") ? a.Bool("fix") : fixDefault };
                if (a.Has("fix_names")) o.FixNames = a.Bool("fix_names");
                if (a.Has("net_khuat")) o.NetKhuat = a.Bool("net_khuat");
                if (a.Has("net_ltscale")) o.NetLts = a.Num("net_ltscale");
                if (a.Has("net_width")) o.NetWidth = a.Num("net_width");
                if (a.Has("net_layer")) o.NetLayer = a.Str("net_layer");
                o.NameBlocks = a.StrList("name_blocks") ?? o.NameBlocks;
                o.SkipLayers = a.StrList("skip_layers") ?? o.SkipLayers;
                o.SkipLabels = a.StrList("skip_labels") ?? o.SkipLabels;
                if (a.Has("frame_layer")) o.FrameLayer = a.Str("frame_layer");
                if (a.Has("max_size")) o.MaxSize = a.Num("max_size");
                if (a.Has("paper_frame")) o.PaperFrame = a.NumArr("paper_frame");
                if (a.Has("short_ratio")) o.ShortRatio = a.Num("short_ratio");
                if (a.Has("tol")) o.Tol = a.Num("tol");
                return o;
            }
        }

        // ------------------------------------------------------------ ket qua gon
        class Report
        {
            public readonly List<Dictionary<string, object>> Warnings = new List<Dictionary<string, object>>();
            public readonly List<Dictionary<string, object>> Fixed = new List<Dictionary<string, object>>();
            public readonly List<string> Notes = new List<string>();
            public void Warn(string layout, string kind, string msg, object extra = null)
            {
                var d = new Dictionary<string, object> { ["layout"] = layout, ["kind"] = kind, ["msg"] = msg };
                if (extra != null) d["handles"] = extra;
                Warnings.Add(d);
            }
            public void Fix(string layout, string kind, string msg)
                => Fixed.Add(new Dictionary<string, object> { ["layout"] = layout, ["kind"] = kind, ["msg"] = msg });
            public List<string> WarnLayouts() => Warnings.Select(w => (string)w["layout"]).Where(l => l != null).Distinct().ToList();
        }

        static string BeamLabel(Transaction tr, BlockReference br)
        {
            string any = null;
            foreach (ObjectId aid in br.AttributeCollection)
            {
                if (aid.IsErased) continue;
                var ar = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                var t = ar.TextString ?? "";
                if (ar.Tag.Equals("TENDAM", StringComparison.OrdinalIgnoreCase)) return CleanName(t);
                if (any == null && t.Trim().Length > 0) any = t;
            }
            return CleanName(any ?? U.H(br.ObjectId));
        }
        static string CleanName(string s) => Regex.Replace(s ?? "", @"%%[uUoO]|DẦM\s*", "").Trim();

        // ============================================================ SOAT / SUA cac to
        /// <summary>Soat (va sua neu o.Fix) cac layout. Chay tren main thread trong 1 transaction.</summary>
        static Report Soat(Document doc, List<string> names, Opts o)
        {
            var rep = new Report();
            var db = doc.Database;
            var skipLayers = U.Wilds(o.SkipLayers);
            var skipLabels = U.Wilds(o.SkipLabels);
            var nameBlocks = U.Wilds(o.NameBlocks);
            var paperFrame = new Box(o.PaperFrame[0], o.PaperFrame[1], o.PaperFrame[2], o.PaperFrame[3]);

            using (o.Fix ? doc.LockDocument() : null)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // ---- 1. viewport cua cac to
                var vps = new List<Vp>();
                var byLayout = new Dictionary<string, List<Vp>>();
                foreach (var n in names)
                {
                    var list = new List<Vp>();
                    foreach (var vp in Commands.LayoutViewports(tr, db, n))
                    {
                        if (Math.Abs(vp.TwistAngle) > 1e-9) { rep.Notes.Add($"{n}: bo qua viewport xoay {U.H(vp.ObjectId)}"); continue; }
                        double s = vp.CustomScale > 0 ? vp.CustomScale : 1;
                        double hw = vp.Width / 2 / s, hh = vp.Height / 2 / s;
                        double cx = vp.ViewCenter.X + vp.ViewTarget.X, cy = vp.ViewCenter.Y + vp.ViewTarget.Y;
                        list.Add(new Vp
                        {
                            Layout = n, Handle = U.H(vp.ObjectId),
                            Paper = new Box(vp.CenterPoint.X - vp.Width / 2, vp.CenterPoint.Y - vp.Height / 2, vp.CenterPoint.X + vp.Width / 2, vp.CenterPoint.Y + vp.Height / 2),
                            Model = new Box(cx - hw, cy - hh, cx + hw, cy + hh),
                        });
                    }
                    byLayout[n] = list;
                    vps.AddRange(list);
                }
                if (vps.Count == 0) { rep.Notes.Add("Khong co viewport nao trong cac to da chon"); tr.Commit(); return rep; }
                var area = vps.Select(v => v.Model).Aggregate((p, q) => p.Union(q));
                area = new Box(area.X0 - 5000, area.Y0 - 5000, area.X1 + 5000, area.Y1 + 5000);

                // ---- 2. doi tuong model trong vung cac view
                var beamNames = new List<Ent>();
                var others = new List<Ent>();
                var frames = new List<Ent>();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    string dxf = id.ObjectClass.DxfName;
                    bool isText = dxf == "TEXT" || dxf == "MTEXT", isIns = dxf == "INSERT", isDim = dxf == "DIMENSION", isPl = dxf == "LWPOLYLINE";
                    if (!isText && !isIns && !isDim && !isPl) continue;
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    if (isPl)
                    {
                        if (!e.Layer.Equals(o.FrameLayer, StringComparison.OrdinalIgnoreCase) || !((Polyline)e).Closed) continue;
                    }
                    var x = U.Ext(e);
                    if (!x.HasValue) continue;
                    var b = new Box(x.Value);
                    if (!b.Touch(area)) continue;
                    var en = new Ent { Id = id, Handle = U.H(id), Type = dxf, Layer = e.Layer, B = b };
                    if (isPl) { frames.Add(en); continue; }
                    if (e is BlockReference br)
                    {
                        string bn = U.BlockName(tr, br);
                        if (nameBlocks.Any(r => r.IsMatch(bn))) { en.Label = BeamLabel(tr, br); beamNames.Add(en); continue; }
                        en.Label = bn;
                    }
                    else if (e is DBText t) en.Label = t.TextString;
                    else if (e is MText m) en.Label = m.Text;
                    else if (e is Dimension) en.Label = "DIM";
                    others.Add(en);
                }

                // khung dam = khung Defpoints chua toi da 1 ten dam (khung lon chua ca tang thi bo)
                var beamFrames = frames.Where(f => beamNames.Count(nm => f.B.HasPt(nm.B.CX, nm.B.CY)) <= 1)
                                       .Where(f => f.B.W < 200000 && f.B.H < 200000).ToList();
                string FrameLabel(Ent f)
                {
                    var nm = beamNames.FirstOrDefault(z => f.B.HasPt(z.B.CX, z.B.CY));
                    return nm?.Label ?? ("khung " + f.Handle);
                }
                foreach (var v in vps)
                {
                    var f = beamFrames.Where(z => z.B.HasPt(v.Model.CX, v.Model.CY)).OrderBy(z => z.B.Area).FirstOrDefault();
                    if (f != null) v.Beam = f.Handle;
                }
                if (beamFrames.Count == 0) rep.Notes.Add("Khong thay khung dam (Defpoints) -> bo qua kiem dam vat 2 to / view noi ngan theo dam");

                // ---- 3. view ngoai khung giay, view chong nhau
                foreach (var n in names)
                {
                    var list = byLayout[n];
                    foreach (var v in list)
                        if (!v.Paper.Inside(paperFrame, 0.5))
                            rep.Warn(n, "vp_ngoai_khung", $"view {v.Handle} lo ra ngoai khung giay {string.Join(",", v.Paper.Arr())}", new[] { v.Handle });
                    for (int i = 0; i < list.Count; i++)
                        for (int j = i + 1; j < list.Count; j++)
                            if (list[i].Paper.InterArea(list[j].Paper) > 4)
                                rep.Warn(n, "vp_chong", $"view {list[i].Handle} chong view {list[j].Handle}", new[] { list[i].Handle, list[j].Handle });
                }

                // ---- 4. dam vat 2 to
                foreach (var g in vps.Where(v => v.Beam != null).GroupBy(v => v.Beam))
                {
                    var ls = g.Select(v => v.Layout).Distinct().ToList();
                    if (ls.Count > 1)
                    {
                        var f = beamFrames.First(z => z.Handle == g.Key);
                        rep.Warn(ls[0], "dam_vat_2_to", $"dam {FrameLabel(f)} nam tren {ls.Count} to: {string.Join(", ", ls)}", g.Select(v => v.Handle).ToList());
                    }
                }

                // ---- 5. view noi ngan: 2 view cung dam, cung dai y, noi tiep theo x, view sau qua ngan
                for (int i = 0; i < vps.Count; i++)
                    for (int j = 0; j < vps.Count; j++)
                    {
                        if (i == j) continue;
                        var A = vps[i]; var B = vps[j];
                        if (A.Beam != null && B.Beam != null && A.Beam != B.Beam) continue;
                        double yov = Math.Min(A.Model.Y1, B.Model.Y1) - Math.Max(A.Model.Y0, B.Model.Y0);
                        if (yov < 0.8 * Math.Min(A.Model.H, B.Model.H)) continue;
                        if (Math.Abs(B.Model.X0 - A.Model.X1) > Math.Max(50, 0.02 * A.Model.W)) continue;
                        if (B.Model.W < o.ShortRatio * A.Model.W)
                            rep.Warn(B.Layout, "view_noi_ngan", $"view {B.Handle} ({F(B.Model.W)}) chi bang {B.Model.W / A.Model.W:P0} view truoc {A.Handle} ({A.Layout}) -> can nhac doi diem cat ve goi som hon", new[] { A.Handle, B.Handle });
                    }

                // ---- 6. ten dam bi view cat (+ doi ten neu duoc)
                var movedIds = new HashSet<ObjectId>();
                foreach (var nm in beamNames)
                {
                    var cuts = vps.Where(v => nm.B.Touch(v.Model) && !nm.B.Inside(v.Model, o.Tol)).ToList();
                    if (cuts.Count == 0) continue;
                    if (vps.Any(v => nm.B.Inside(v.Model, o.Tol)) && cuts.All(c => nm.B.InterArea(c.Model) < 0.02 * nm.B.Area)) continue; // cham mep rat it
                    var host = cuts.OrderByDescending(v => nm.B.InterArea(v.Model)).First();
                    // TL / chu di kem ten (cung hang, sat ben)
                    var group = new List<Ent> { nm };
                    foreach (var ot in others)
                    {
                        if (ot.Type == "DIMENSION" || ot.B.W > 3 * nm.B.W) continue;
                        double yo = Math.Min(ot.B.Y1, nm.B.Y1) - Math.Max(ot.B.Y0, nm.B.Y0);
                        if (yo < 0.5 * Math.Min(ot.B.H, nm.B.H)) continue;
                        double gap = Math.Max(ot.B.X0 - nm.B.X1, nm.B.X0 - ot.B.X1);
                        if (gap <= Math.Max(300, 0.3 * nm.B.W)) group.Add(ot);
                    }
                    var gb = group.Select(g => g.B).Aggregate((p, q) => p.Union(q));
                    string where = string.Join(", ", cuts.Select(c => c.Layout).Distinct());
                    double margin = Math.Max(150, 0.02 * host.Model.W);
                    if (gb.Y0 < host.Model.Y0 - o.Tol || gb.Y1 > host.Model.Y1 + o.Tol || gb.W > host.Model.W - 2 * margin)
                    {
                        rep.Warn(host.Layout, "ten_bi_cat", $"ten {nm.Label} bi view cat ({where}), khong tu doi duoc (cat theo phuong dung / view hep)", new[] { nm.Handle });
                        continue;
                    }
                    var groupIds = new HashSet<string>(group.Select(g => g.Handle));
                    var obstacles = others.Where(z => !groupIds.Contains(z.Handle) && z.B.Touch(host.Model) && z.B.W < o.MaxSize && z.B.H < o.MaxSize).ToList();
                    obstacles.AddRange(beamNames.Where(z => z != nm && z.B.Touch(host.Model)));
                    double target = host.Model.CX - gb.CX;
                    double lo = host.Model.X0 + margin - gb.X0, hi = host.Model.X1 - margin - gb.X1; // khoang dx hop le
                    double? best = null;
                    double step = Math.Max(100, (hi - lo) / 40);
                    for (int k = 0; k <= 80 && best == null; k++)
                    {
                        foreach (double sgn in new[] { 1.0, -1.0 })
                        {
                            double dx = target + sgn * k * step;
                            if (dx < lo || dx > hi) continue;
                            var nb = gb.Shift(dx, 0);
                            if (obstacles.Any(z => z.B.Touch(nb))) continue;
                            if (vps.Any(v => v != host && nb.Touch(v.Model) && !nb.Inside(v.Model, o.Tol))) continue;
                            best = dx; break;
                        }
                    }
                    if (best == null)
                    {
                        rep.Warn(host.Layout, "ten_bi_cat", $"ten {nm.Label} bi view cat ({where}), khong tim duoc cho trong de doi", new[] { nm.Handle });
                        continue;
                    }
                    if (!(o.Fix && o.FixNames))
                    {
                        rep.Warn(host.Layout, "ten_bi_cat", $"ten {nm.Label} bi view cat ({where}); de xuat doi dx={F(best.Value)}", group.Select(g => g.Handle).ToList());
                        continue;
                    }
                    var m = Matrix3d.Displacement(new Vector3d(best.Value, 0, 0));
                    foreach (var g in group)
                    {
                        if (movedIds.Contains(g.Id)) continue;
                        var e = (Entity)tr.GetObject(g.Id, OpenMode.ForWrite, false, true);
                        e.TransformBy(m);
                        movedIds.Add(g.Id);
                        g.B = g.B.Shift(best.Value, 0);
                    }
                    rep.Fix(host.Layout, "doi_ten", $"doi ten {nm.Label} dx={F(best.Value)} vao giua view {host.Handle} ({group.Count} doi tuong)");
                }

                // ---- 7. chu / block khac bi view cat (chi bao, gom theo to)
                foreach (var n in names)
                {
                    var hits = new List<(string h, string label)>();
                    foreach (var v in byLayout[n])
                        foreach (var ot in others)
                        {
                            if (ot.Type == "DIMENSION" || movedIds.Contains(ot.Id)) continue;
                            if (ot.B.W > o.MaxSize || ot.B.H > o.MaxSize) continue;
                            if (U.AnyMatch(skipLayers, ot.Layer) && skipLayers != null) continue;
                            if (skipLabels != null && skipLabels.Any(r => r.IsMatch(ot.Label ?? ""))) continue;
                            if (!ot.B.Touch(v.Model) || ot.B.Inside(v.Model, o.Tol)) continue;
                            if (ot.B.InterArea(v.Model) < 0.05 * Math.Max(ot.B.Area, 1e-9)) continue; // chi cham mep
                            hits.Add((ot.Handle, (ot.Label ?? "").Length > 30 ? ot.Label.Substring(0, 30) : ot.Label));
                        }
                    if (hits.Count > 0)
                        rep.Warn(n, "chu_bi_cat", $"{hits.Count} chu/ky hieu bi vien view cat, vd: {string.Join(" | ", hits.Take(4).Select(h => h.label))}", hits.Select(h => h.h).Distinct().Take(20).ToList());
                }

                // ---- 8. net ngan DCE_NetKhuat trong layout
                if (o.NetKhuat)
                {
                    foreach (var n in names)
                    {
                        var list = byLayout[n];
                        var beamsHere = beamNames.Where(nm => list.Any(v => v.Model.HasPt(nm.B.CX, nm.B.CY))).Select(nm => nm.Label).Distinct().ToList();
                        var lo = Docs.FindLayout(tr, db, n);
                        var btr = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead);
                        var nets = new List<ObjectId>();
                        foreach (ObjectId id in btr)
                        {
                            if (id.IsErased) continue;
                            var dxf = id.ObjectClass.DxfName;
                            if (dxf != "LWPOLYLINE" && dxf != "LINE") continue;
                            var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                            if (e.Layer.Equals(o.NetLayer, StringComparison.OrdinalIgnoreCase)) nets.Add(id);
                        }
                        if (nets.Count == 0) continue;
                        if (beamsHere.Count == 0) { rep.Warn(n, "khong_thay_ten", "to khong thay ten dam nao trong view -> khong dong vao net ngan"); continue; }
                        if (beamsHere.Count == 1)
                        {
                            if (o.Fix) { foreach (var id in nets) ((Entity)tr.GetObject(id, OpenMode.ForWrite, false, true)).Erase(); rep.Fix(n, "xoa_net_ngan", $"to 1 dam ({beamsHere[0]}): xoa {nets.Count} net ngan"); }
                            else rep.Warn(n, "net_ngan", $"to 1 dam ({beamsHere[0]}): can xoa {nets.Count} net ngan");
                            continue;
                        }
                        int changed = FixNets(tr, nets, o);
                        if (changed > 0)
                        {
                            if (o.Fix) rep.Fix(n, "net_ngan", $"{changed}/{nets.Count} net ngan: LTscale {F(o.NetLts)}, width {F(o.NetWidth)}, gióng thang");
                            else rep.Warn(n, "net_ngan", $"{changed}/{nets.Count} net ngan chua dung LTscale {F(o.NetLts)} / width {F(o.NetWidth)} / chua thang");
                        }
                    }
                }
                tr.Commit();
            }
            return rep;
        }

        /// <summary>Net ngan: LTscale, width, nan thang (gan ngang -> ngang, gan dung -> dung), deu dau cac net ngang. Tra so net can/da sua.</summary>
        static int FixNets(Transaction tr, List<ObjectId> nets, Opts o)
        {
            const double straightTol = 3, endTol = 10; // mm giay
            int changed = 0;
            var horiz = new List<Polyline>();
            foreach (var id in nets)
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                bool need = Math.Abs(e.LinetypeScale - o.NetLts) > 1e-6;
                if (e is Polyline pl)
                {
                    if (Math.Abs(pl.ConstantWidth - o.NetWidth) > 1e-6) need = true;
                    var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(pl.GetPoint2dAt).ToList();
                    double dy = pts.Max(p => p.Y) - pts.Min(p => p.Y), dx = pts.Max(p => p.X) - pts.Min(p => p.X);
                    bool isH = dy > 1e-6 && dy <= straightTol && dx > 10 * dy, isV = dx > 1e-6 && dx <= straightTol && dy > 10 * dx;
                    if (isH || isV) need = true;
                    if (dy <= straightTol && dx > 10 * Math.Max(dy, 1e-6)) horiz.Add(pl);
                    if (need && o.Fix)
                    {
                        pl.UpgradeOpen();
                        pl.LinetypeScale = o.NetLts;
                        pl.ConstantWidth = o.NetWidth;
                        if (isH) { double y = pts.Average(p => p.Y); for (int i = 0; i < pts.Count; i++) pl.SetPointAt(i, new Point2d(pts[i].X, y)); }
                        if (isV) { double x = pts.Average(p => p.X); for (int i = 0; i < pts.Count; i++) pl.SetPointAt(i, new Point2d(x, pts[i].Y)); }
                    }
                }
                else if (e is Line ln)
                {
                    double dy = Math.Abs(ln.EndPoint.Y - ln.StartPoint.Y), dx = Math.Abs(ln.EndPoint.X - ln.StartPoint.X);
                    bool isH = dy > 1e-6 && dy <= straightTol && dx > 10 * dy, isV = dx > 1e-6 && dx <= straightTol && dy > 10 * dx;
                    if (isH || isV) need = true;
                    if (need && o.Fix)
                    {
                        ln.UpgradeOpen();
                        ln.LinetypeScale = o.NetLts;
                        if (isH) { double y = (ln.StartPoint.Y + ln.EndPoint.Y) / 2; ln.StartPoint = new Point3d(ln.StartPoint.X, y, 0); ln.EndPoint = new Point3d(ln.EndPoint.X, y, 0); }
                        if (isV) { double x = (ln.StartPoint.X + ln.EndPoint.X) / 2; ln.StartPoint = new Point3d(x, ln.StartPoint.Y, 0); ln.EndPoint = new Point3d(x, ln.EndPoint.Y, 0); }
                    }
                }
                if (need) changed++;
            }
            // gióng thang: cac net ngang 2 dinh co dau trai / phai lech nhau < endTol -> deu ve cung x
            var two = horiz.Where(p => p.NumberOfVertices == 2).ToList();
            foreach (bool left in new[] { true, false })
            {
                var ends = two.Select(p => (pl: p, i: (p.GetPoint2dAt(0).X < p.GetPoint2dAt(1).X) == left ? 0 : 1)).ToList();
                var used = new HashSet<Polyline>();
                foreach (var a in ends)
                {
                    if (used.Contains(a.pl)) continue;
                    double xa = a.pl.GetPoint2dAt(a.i).X;
                    var grp = ends.Where(b => !used.Contains(b.pl) && Math.Abs(b.pl.GetPoint2dAt(b.i).X - xa) < endTol).ToList();
                    if (grp.Count < 2) continue;
                    double xt = left ? grp.Min(b => b.pl.GetPoint2dAt(b.i).X) : grp.Max(b => b.pl.GetPoint2dAt(b.i).X);
                    foreach (var b in grp)
                    {
                        used.Add(b.pl);
                        var p = b.pl.GetPoint2dAt(b.i);
                        if (Math.Abs(p.X - xt) < 1e-6) continue;
                        if (o.Fix) { if (!b.pl.IsWriteEnabled) b.pl.UpgradeOpen(); b.pl.SetPointAt(b.i, new Point2d(xt, p.Y)); }
                        changed++;
                    }
                }
            }
            return changed;
        }

        static Dictionary<string, object> Pack(Report rep, List<string> layouts, Dictionary<string, object> extra = null)
        {
            var r = new Dictionary<string, object>
            {
                ["layouts"] = layouts.Count == 0 ? "" : layouts.Count == 1 ? layouts[0] : layouts[0] + ".." + layouts[layouts.Count - 1],
                ["so_to"] = layouts.Count,
                ["so_loi"] = rep.Warnings.Count,
                ["loi"] = rep.Warnings,
                ["da_sua"] = rep.Fixed,
            };
            if (rep.Notes.Count > 0) r["ghi_chu"] = rep.Notes;
            if (extra != null) foreach (var kv in extra) r[kv.Key] = kv.Value;
            return r;
        }

        static List<string> ResolveLayouts(Transaction tr, Database db, Args a)
        {
            var names = a.StrList("layouts");
            var all = new List<(string n, int o)>();
            var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry en in dict)
            {
                var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead);
                if (!lo.ModelType) all.Add((lo.LayoutName, lo.TabOrder));
            }
            if (names != null) return names;
            if (a.Has("pattern"))
            {
                var rx = new Regex(a.Str("pattern"), RegexOptions.IgnoreCase);
                return all.Where(x => rx.IsMatch(x.n)).OrderBy(x => x.o).Select(x => x.n).ToList();
            }
            if (a.Has("from") && a.Has("to"))
            {
                // vd from="02-426", to="02-436"
                var m0 = Regex.Match(a.Str("from"), @"^(.*?)(\d+)$"); var m1 = Regex.Match(a.Str("to"), @"^(.*?)(\d+)$");
                if (!m0.Success || !m1.Success) throw new BridgeException("from/to phai dang <tien to><so>, vd 02-401");
                int s = int.Parse(m0.Groups[2].Value), e = int.Parse(m1.Groups[2].Value);
                var res = new List<string>();
                for (int i = s; i <= e; i++)
                {
                    var n = m0.Groups[1].Value + i.ToString(new string('0', m0.Groups[2].Value.Length));
                    if (all.Any(x => x.n.Equals(n, StringComparison.OrdinalIgnoreCase))) res.Add(n);
                }
                return res;
            }
            throw new BridgeException("Can layouts[], pattern (regex) hoac from/to");
        }

        /// <summary>
        /// Soat (fix=false, mac dinh) hoac sua (fix=true) cac to dam co san.
        /// args: layouts[] | pattern | from+to; fix, fix_names, net_khuat, zoom, shots, max_shots... (xem Opts).
        /// </summary>
        public static object SoatTo(Args a)
        {
            var o = Opts.From(a, false);
            var res = MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                if (o.Fix) Docs.EnsureIdle(doc, a);
                List<string> names;
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction()) { names = ResolveLayouts(tr, doc.Database, a); tr.Commit(); }
                return (names, Soat(doc, names, o));
            }, 15000, 900000);
            var extra = new Dictionary<string, object>();
            if (o.Fix && a.Bool("zoom", true)) ZoomAll(a, res.names);
            if (a.Bool("shots", false)) extra["anh"] = Shots(a, res.Item2.WarnLayouts().Take(a.Int("max_shots", 6)).ToList());
            return Pack(res.Item2, res.names, extra);
        }

        /// <summary>Buoc cuoi dot: moi net ngan trong cac layout khop pattern (mac dinh ^\d+-4\d\d$) -> LTscale 0.5 (+ width).</summary>
        public static object NetKhuatAll(Args a)
        {
            var pat = new Regex(a.Str("pattern") ?? @"^\d+-4\d\d$", RegexOptions.IgnoreCase);
            string layer = a.Str("net_layer") ?? "DCE_NetKhuat";
            double lts = a.Num("net_ltscale", 0.5);
            double? width = a.Has("net_width") ? a.Num("net_width") : 0.3;
            bool run = a.Bool("run", true);
            return MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                if (run) Docs.EnsureIdle(doc, a);
                var db = doc.Database;
                int n = 0, nl = 0; var touched = new List<string>();
                using (run ? doc.LockDocument() : null)
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry en in dict)
                    {
                        var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead);
                        if (lo.ModelType || !pat.IsMatch(lo.LayoutName)) continue;
                        nl++;
                        var btr = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead);
                        int k = 0;
                        foreach (ObjectId id in btr)
                        {
                            if (id.IsErased) continue;
                            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (e == null || !e.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase) || e is Viewport) continue;
                            bool need = Math.Abs(e.LinetypeScale - lts) > 1e-6 || (width.HasValue && e is Polyline p0 && Math.Abs(p0.ConstantWidth - width.Value) > 1e-6);
                            if (!need) continue;
                            k++;
                            if (!run) continue;
                            e.UpgradeOpen();
                            e.LinetypeScale = lts;
                            if (width.HasValue && e is Polyline p) p.ConstantWidth = width.Value;
                        }
                        if (k > 0) touched.Add(lo.LayoutName + ":" + k);
                        n += k;
                    }
                    tr.Commit();
                }
                return new Dictionary<string, object> { ["run"] = run, ["so_layout"] = nl, ["so_net"] = n, ["chi_tiet"] = touched };
            }, 15000, 600000);
        }

        // ============================================================ TAOMV 1 tang + toan bo quy trinh
        /// <summary>
        /// Dan trang 1 tang. args bat buoc: mau (layout mau), window [x1,y1,x2,y2] (vung model cua tang), tang ("1", "3-4", "TUM", "MAI"),
        /// prefix ("02"). Tuy chon: start (so to dau, mac dinh = so 4xx lon nhat + 1), ticks {ten o: true/false},
        /// insert_point [20.8,413.2], title_find ("ĐIỂN HÌNH"), title_format ("DẦM {tang} - {i}"), fix (true), fix_names, net_khuat,
        /// zoom (true), shots (true), max_shots (6), backup (true), save (false), timeout_s (600).
        /// </summary>
        static int _busy;
        public static object DanTrangTang(Args a)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new BusyException("Dang dan trang mot tang khac (lenh CE_DANTRANG hoac Claude). Cho xong roi chay lai.");
            try { return DanTrangCore(a); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static object DanTrangCore(Args a)
        {
            string mau = a.ReqStr("mau");
            var win = a.NumArr("window") ?? throw new BridgeException("Thieu window [x1,y1,x2,y2]");
            if (win.Length < 4) throw new BridgeException("window can 4 so");
            string tang = TangName(a.ReqStr("tang"));
            string prefix = a.ReqStr("prefix");
            var o = Opts.From(a, true);
            var log = new List<string>();
            var rep0 = new Report();

            // 0. backup
            if (a.Bool("backup", true))
            {
                try { var b = (Dictionary<string, object>)Commands.Backup(new Args(new Dictionary<string, object> { ["doc"] = a.Str("doc") })); log.Add("backup " + b["backup"]); if (b["warning"] != null) rep0.Notes.Add((string)b["warning"]); }
                catch (Exception ex) { rep0.Notes.Add("Khong backup duoc: " + ex.Message); }
            }

            // 1. layout truoc khi chay + so to bat dau
            var before = LayoutNames(a);
            var rxNum = new Regex("^" + Regex.Escape(prefix) + @"-(4\d\d)$");
            int start = a.Has("start") ? a.Int("start") : before.Select(n => rxNum.Match(n)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(400).Max() + 1;

            // 2. TAOMV
            var tao = RunTaomv(a, mau, win, rep0);
            log.Add("taomv " + tao);
            var rxNew = new Regex("^" + Regex.Escape(mau) + @" \((\d+)\)$");
            var created = LayoutNames(a).Where(n => !before.Contains(n) && rxNew.IsMatch(n))
                                        .OrderBy(n => int.Parse(rxNew.Match(n).Groups[1].Value)).ToList();
            if (created.Count == 0)
                throw new BridgeException($"TAOMV khong tao layout '{mau} (n)' nao. {tao}. {string.Join("; ", rep0.Notes)}");

            // 3. dat ten to + khung ten
            var finalNames = created.Select((n, i) => $"{prefix}-{start + i}").ToList();
            var clash = finalNames.Where(n => before.Contains(n)).ToList();
            if (clash.Count > 0)
            {
                rep0.Warn(null, "trung_ten", "Ten to da ton tai, giu ten tam '" + mau + " (n)': " + string.Join(", ", clash));
                finalNames = created;
            }
            else
            {
                string find = a.Str("title_find") ?? "ĐIỂN HÌNH";
                string fmt = a.Str("title_format") ?? "DẦM {tang} - {i}";
                MainThread.Run(() =>
                {
                    var doc = Docs.Resolve(a);
                    var db = doc.Database;
                    using (doc.LockDocument())
                    {
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            for (int i = 0; i < created.Count; i++)
                            {
                                var lo = Docs.FindLayout(tr, db, created[i]);
                                var btr = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead);
                                int hit = 0;
                                foreach (ObjectId id in btr)
                                {
                                    if (id.IsErased || id.ObjectClass.DxfName != "TEXT") continue;
                                    var t = (DBText)tr.GetObject(id, OpenMode.ForRead);
                                    if (t.TextString.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    t.UpgradeOpen();
                                    t.TextString = fmt.Replace("{tang}", tang).Replace("{i}", (i + 1).ToString("00"));
                                    hit++;
                                }
                                if (hit == 0) rep0.Warn(finalNames[i], "khung_ten", $"khong thay chu '{find}' de dat ten khung");
                            }
                            tr.Commit();
                        }
                        for (int i = 0; i < created.Count; i++) LayoutManager.Current.RenameLayout(created[i], finalNames[i]);
                    }
                    return 0;
                });
                // sap tab: dat ngay sau to prefix-4xx cuoi cung co san
                try
                {
                    var order = LayoutNames(a, true).Where(n => !finalNames.Contains(n)).ToList();
                    int at = order.FindLastIndex(n => rxNum.IsMatch(n));
                    order.InsertRange(at < 0 ? order.Count : at + 1, finalNames);
                    Commands.LayoutReorder(new Args(new Dictionary<string, object> { ["names"] = order, ["doc"] = a.Str("doc") }));
                }
                catch (Exception ex) { rep0.Notes.Add("Sap tab loi: " + ex.Message); }
            }

            // 4. soat + sua
            var rep = MainThread.Run(() => Soat(Docs.Resolve(a), finalNames, o), 15000, 900000);
            rep.Warnings.InsertRange(0, rep0.Warnings);
            rep.Notes.InsertRange(0, rep0.Notes);

            // 5. zoom, anh, luu
            if (a.Bool("zoom", true)) ZoomAll(a, finalNames);
            var extra = new Dictionary<string, object> { ["tang"] = tang, ["log"] = log };
            if (a.Bool("shots", true)) extra["anh"] = Shots(a, rep.WarnLayouts().Take(a.Int("max_shots", 6)).ToList());
            if (a.Bool("save", false)) { Commands.Save(new Args(new Dictionary<string, object> { ["doc"] = a.Str("doc") })); extra["saved"] = true; }
            return Pack(rep, finalNames, extra);
        }

        static string TangName(string t)
        {
            t = (t ?? "").Trim();
            var up = t.ToUpperInvariant();
            if (up.StartsWith("TẦNG")) return up;
            if (up == "MAI" || up == "MÁI") return "TẦNG MÁI";
            if (up == "TUM") return "TẦNG TUM";
            return "TẦNG " + up;
        }

        static List<string> LayoutNames(Args a, bool byTab = false) => MainThread.Run(() =>
        {
            var doc = Docs.Resolve(a);
            var r = new List<(string, int)>();
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry en in dict)
                {
                    var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead);
                    if (!lo.ModelType) r.Add((lo.LayoutName, lo.TabOrder));
                }
                tr.Commit();
            }
            return (byTab ? r.OrderBy(x => x.Item2) : r.AsEnumerable()).Select(x => x.Item1).ToList();
        });

        static Dictionary<string, object> Cmd(Args a, string command, double timeout, double stall, bool force)
        {
            var d = new Dictionary<string, object>
            {
                ["command"] = command, ["wait"] = true, ["timeout_s"] = timeout, ["stall_s"] = stall,
                ["return_handles"] = false, ["force"] = force, ["doc"] = a.Str("doc"),
            };
            if (!force) d["layout"] = "Model";
            return (Dictionary<string, object>)CommandRunner.Run(new Args(d));
        }

        /// <summary>Mo form LayoutVPs, chon layout mau, dat o tick, bam Tao MV, chon crossing C 2 goc, diem chen. Khong SendKeys.</summary>
        static string RunTaomv(Args a, string mau, double[] w, Report rep)
        {
            var old = W32.FindTop("LayoutVPs");
            if (old != IntPtr.Zero) { W32.PostMessage(old, W32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(2000); }
            Cmd(a, "TAOMV\n", 20, 5, false);
            var f = IntPtr.Zero;
            for (int i = 0; i < 40 && f == IntPtr.Zero; i++) { f = W32.FindTop("LayoutVPs"); if (f == IntPtr.Zero) Thread.Sleep(500); }
            if (f == IntPtr.Zero) throw new BridgeException("Khong thay form LayoutVPs sau khi go TAOMV (add-in DCE da nap chua?)");
            Thread.Sleep(500);
            var kids = W32.Children(f);

            // combo layout mau = combobox rong nhat
            var cmb = kids.Where(k => k.cls.IndexOf("COMBOBOX", StringComparison.OrdinalIgnoreCase) >= 0).OrderByDescending(k => k.width).FirstOrDefault();
            if (cmb.h == IntPtr.Zero) throw new BridgeException("Form LayoutVPs khong co combobox layout mau");
            int idx = (int)W32.SendMessage(cmb.h, W32.CB_FINDSTRINGEXACT, (IntPtr)(-1), mau);
            if (idx < 0) { W32.PostMessage(f, W32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); throw new BridgeException("Form TAOMV khong co layout mau '" + mau + "'"); }
            W32.SendMessage(cmb.h, W32.CB_SETCURSEL, (IntPtr)idx, IntPtr.Zero);
            W32.SendMessage(W32.GetParent(cmb.h), W32.WM_COMMAND, (IntPtr)((W32.CBN_SELCHANGE << 16) | (W32.GetDlgCtrlID(cmb.h) & 0xFFFF)), cmb.h);
            Thread.Sleep(300);

            // o tick: chi bam khi trang thai khac mong muon, doc lai de xac nhan
            var ticks = a.Dict("ticks") ?? new Dictionary<string, object>
            {
                ["New - Copy layout"] = true, ["Tự động xếp Khung tên"] = true, ["Sắp xếp tối ưu"] = true, ["Cắt đúng trục"] = true,
            };
            foreach (var kv in ticks)
            {
                bool want = Args.ToDouble(kv.Value is bool bb ? (bb ? 1 : 0) : kv.Value) != 0;
                var c = kids.FirstOrDefault(k => W32.Norm(k.text).Equals(W32.Norm(kv.Key), StringComparison.OrdinalIgnoreCase));
                if (c.h == IntPtr.Zero) { rep.Notes.Add("Form TAOMV khong co o '" + kv.Key + "'"); continue; }
                if (!W32.IsCheckBox(c.h)) { rep.Notes.Add("Khong doc duoc trang thai o '" + kv.Key + "' -> giu nguyen, kiem tay"); continue; }
                if (W32.Checked(c.h) == want) continue;
                W32.Press(c.h, false); Thread.Sleep(300);
                if (W32.Checked(c.h) != want) rep.Notes.Add("O '" + kv.Key + "' khong doi duoc trang thai");
            }

            var btn = kids.FirstOrDefault(k => W32.Norm(k.text).Equals("Tạo MV", StringComparison.OrdinalIgnoreCase));
            if (btn.h == IntPtr.Zero) throw new BridgeException("Form LayoutVPs khong co nut 'Tạo MV'");
            W32.Press(btn.h, true);
            Thread.Sleep(3000);

            string P(double x, double y) => F(x) + "," + F(y);
            var r1 = Cmd(a, $"C\n{P(w[0], w[1])}\n{P(w[2], w[3])}\n\n", 90, 15, true);
            var ip = a.NumArr("insert_point") ?? new[] { 20.8, 413.2 };
            var r2 = Cmd(a, P(ip[0], ip[1]) + "\n", a.Num("timeout_s", 600), 40, true);
            string st = (string)r2["state"];
            for (int i = 0; i < 5 && st == "running"; i++)
            {
                var r3 = (Dictionary<string, object>)CommandRunner.Status(new Args(new Dictionary<string, object> { ["timeout_s"] = 300, ["stall_s"] = 40, ["return_handles"] = false }));
                st = (string)r3["state"];
            }
            if (st != "done") rep.Notes.Add("TAOMV ket thuc o trang thai '" + st + "' (kiem dong lenh AutoCAD)");
            return $"chon={r1["state"]} chen={st}";
        }

        // ============================================================ zoom + anh
        static void ZoomAll(Args a, List<string> names)
        {
            MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                if (doc != Application.DocumentManager.MdiActiveDocument) Application.DocumentManager.MdiActiveDocument = doc;
                dynamic acad = Application.AcadApplication;
                foreach (var n in names)
                {
                    try { using (doc.LockDocument()) LayoutManager.Current.CurrentLayout = n; acad.ZoomExtents(); }
                    catch (Exception ex) { Log.Write("zoom " + n + ": " + ex.Message); }
                }
                return 0;
            }, 15000, 600000);
        }

        static List<string> Shots(Args a, List<string> names)
        {
            var res = new List<string>();
            string dir = a.Str("shot_dir") ?? Path.Combine(Path.GetTempPath(), "CivilEngiShots");
            foreach (var n in names)
            {
                try { res.Add(Shot(a, n, dir, a.Int("max_width", 1400))); }
                catch (Exception ex) { res.Add(n + ": loi chup " + ex.Message); }
            }
            return res;
        }

        /// <summary>Chup 1 hay nhieu layout (Z E roi chup cua so AutoCAD) ra PNG. args: layouts[] | layout, shot_dir, max_width.</summary>
        public static object LayoutShot(Args a)
        {
            var names = a.StrList("layouts") ?? new List<string> { a.ReqStr("layout") };
            return Shots(a, names);
        }

        static string Shot(Args a, string layout, string dir, int maxW)
        {
            var info = MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                if (doc != Application.DocumentManager.MdiActiveDocument) Application.DocumentManager.MdiActiveDocument = doc;
                using (doc.LockDocument()) LayoutManager.Current.CurrentLayout = layout;
                dynamic acad = Application.AcadApplication;
                acad.ZoomExtents();
                try { doc.Editor.UpdateScreen(); } catch { }
                try { Application.UpdateScreen(); } catch { }
                return (Application.MainWindow.Handle, Path.GetFileNameWithoutExtension(doc.Name));
            });
            Thread.Sleep(a.Int("delay_ms", 900));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Safe(info.Item2) + "_" + Safe(layout) + ".png");
            W32.Capture(info.Item1, path, maxW);
            return path;
        }
        static string Safe(string s) => Regex.Replace(s ?? "", @"[\\/:*?""<>|]", "_");
    }

    /// <summary>Win32 cho form add-in va chup cua so.</summary>
    internal static class W32
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, StringBuilder l, uint flags, uint timeout, out IntPtr res);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

        public const uint WM_CLOSE = 0x10, WM_COMMAND = 0x111, WM_GETTEXT = 0xD, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202,
                          BM_GETCHECK = 0xF0, CB_FINDSTRINGEXACT = 0x158, CB_SETCURSEL = 0x14E, CBN_SELCHANGE = 1;
        static readonly uint Pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static string Norm(string s) => (s ?? "").Replace("&", "").Trim();
        static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
        public static string Txt(IntPtr h)
        {
            var s = new StringBuilder(1024);
            SendMessageTimeout(h, WM_GETTEXT, (IntPtr)1024, s, 2, 500, out _);
            return s.ToString();
        }

        public static IntPtr FindTop(string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != Pid || !IsWindowVisible(h)) return true;
                if (Txt(h).Trim().Equals(title, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static List<(IntPtr h, string cls, string text, int width)> Children(IntPtr f)
        {
            var r = new List<(IntPtr, string, string, int)>();
            EnumChildWindows(f, (c, l) =>
            {
                GetWindowRect(c, out var rc);
                r.Add((c, Cls(c), Txt(c), rc.R - rc.L));
                return true;
            }, IntPtr.Zero);
            return r;
        }

        public static bool IsCheckBox(IntPtr h)
        {
            int st = GetWindowLong(h, -16) & 0xF;
            return st == 2 || st == 3 || st == 5 || st == 6;
        }
        public static bool Checked(IntPtr h) => SendMessage(h, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() == 1;

        public static void Press(IntPtr h, bool post)
        {
            var lp = (IntPtr)((5 << 16) | 5);
            if (post) { PostMessage(h, WM_LBUTTONDOWN, (IntPtr)1, lp); PostMessage(h, WM_LBUTTONUP, IntPtr.Zero, lp); }
            else { SendMessage(h, WM_LBUTTONDOWN, (IntPtr)1, lp); SendMessage(h, WM_LBUTTONUP, IntPtr.Zero, lp); }
        }

        /// <summary>Chup cua so (PrintWindow, loi thi chup man hinh vung cua so), thu nho ve maxW, luu PNG.</summary>
        public static void Capture(IntPtr hwnd, string path, int maxW)
        {
            GetWindowRect(hwnd, out var rc);
            int w = Math.Max(1, rc.R - rc.L), h = Math.Max(1, rc.B - rc.T);
            using (var bmp = new System.Drawing.Bitmap(w, h))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    bool ok = PrintWindow(hwnd, hdc, 2);
                    g.ReleaseHdc(hdc);
                    if (!ok || Uniform(bmp)) g.CopyFromScreen(rc.L, rc.T, 0, 0, new System.Drawing.Size(w, h));
                }
                double k = Math.Min(1.0, maxW / (double)w);
                using (var outB = new System.Drawing.Bitmap(bmp, Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k))))
                    outB.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        static bool Uniform(System.Drawing.Bitmap b)
        {
            var c0 = b.GetPixel(b.Width / 2, b.Height / 2);
            for (int i = 1; i < 10; i++)
                for (int j = 1; j < 10; j++)
                    if (b.GetPixel(b.Width * i / 10, b.Height * j / 10) != c0) return false;
            return true;
        }
    }
}
