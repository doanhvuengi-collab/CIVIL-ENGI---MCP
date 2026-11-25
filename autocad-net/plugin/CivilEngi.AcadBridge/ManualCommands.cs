using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(CivilEngi.AcadBridge.ManualCommands))]

namespace CivilEngi.AcadBridge
{
    /// <summary>
    /// Lenh go tay trong AutoCAD, dung chung code voi lenh Claude goi qua pipe:
    ///   CE_DANTRANG  - dan trang chi tiet dam 1 tang (TAOMV + dat ten to + sua net ngan + doi ten dam bi cat + bao loi)
    ///   CE_SOATTO    - soat cac to co san (chi bao loi, hoi co sua khong)
    ///   CE_NETKHUAT  - buoc cuoi dot: moi net ngan trong layout xx-4xx ve LTscale 0.5, width 0.3
    ///   CE_CHUPTO    - chup to hien hanh ra PNG
    /// </summary>
    public class ManualCommands
    {
        // nho gia tri lan truoc trong phien
        static string _mau = "", _tang = "", _prefix = "02";

        static Editor Ed => Application.DocumentManager.MdiActiveDocument.Editor;

        static string AskStr(string msg, string def, bool spaces = true)
        {
            var o = new PromptStringOptions($"\n{msg}{(string.IsNullOrEmpty(def) ? "" : " <" + def + ">")}: ") { AllowSpaces = spaces };
            var r = Ed.GetString(o);
            if (r.Status != PromptStatus.OK) return null;
            return string.IsNullOrWhiteSpace(r.StringResult) ? def : r.StringResult.Trim();
        }

        static void Print(Dictionary<string, object> r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"\n=== {r["layouts"]} ({r["so_to"]} to) | da sua {((IList)r["da_sua"]).Count} | loi {r["so_loi"]}");
            foreach (Dictionary<string, object> f in (IList)r["da_sua"]) sb.Append($"\n  [sua] {f["layout"]}: {f["msg"]}");
            foreach (Dictionary<string, object> w in (IList)r["loi"]) sb.Append($"\n  [LOI] {w["layout"]} {w["kind"]}: {w["msg"]}");
            if (r.TryGetValue("ghi_chu", out var gc)) foreach (var g in (IList)gc) sb.Append($"\n  [ghi chu] {g}");
            if (r.TryGetValue("anh", out var an)) foreach (var g in (IList)an) sb.Append($"\n  [anh] {g}");
            sb.Append("\n");
            Plugin.WriteLine(sb.ToString());
        }

        /// <summary>Chay viec tren thread nen (sau khi lenh nay ket thuc) de TAOMV chay duoc tren dong lenh.</summary>
        static void RunBackground(string name, Func<object> work)
        {
            Task.Run(() =>
            {
                try
                {
                    // cho lenh go tay ket thuc han (CMDACTIVE = 0)
                    for (int i = 0; i < 50; i++)
                    {
                        if (MainThread.Run(() => Docs.CmdActive(), 3000) == 0) break;
                        Thread.Sleep(200);
                    }
                    var r = work();
                    MainThread.Run(() => { Print((Dictionary<string, object>)r); return 0; });
                }
                catch (System.Exception ex)
                {
                    Log.Write(name + " loi: " + ex);
                    try { MainThread.Run(() => { Plugin.WriteLine($"\n{name} LOI: {ex.Message}\n"); return 0; }); } catch { }
                }
            });
        }

        [CommandMethod("CE_DANTRANG")]
        public void DanTrangCmd()
        {
            var ed = Ed;
            var mau = AskStr("Layout mau (to TRONG chi co khung ten)", _mau); if (string.IsNullOrEmpty(mau)) return;
            var p1 = ed.GetPoint("\nVung tang trong Model - goc thu nhat: ");
            if (p1.Status != PromptStatus.OK) return;
            var p2 = ed.GetCorner("\nGoc doi dien: ", p1.Value);
            if (p2.Status != PromptStatus.OK) return;
            var tang = AskStr("Ten tang (vd 1, 3-4, TUM, MAI)", _tang); if (string.IsNullOrEmpty(tang)) return;
            var prefix = AskStr("Tien to so to", _prefix, false); if (string.IsNullOrEmpty(prefix)) return;
            var io = new PromptIntegerOptions("\nSo to bat dau (Enter = tu dong, tiep so 4xx lon nhat)") { AllowNone = true, AllowZero = false, AllowNegative = false };
            var st = ed.GetInteger(io);
            if (st.Status != PromptStatus.OK && st.Status != PromptStatus.None) return;
            var kw = new PromptKeywordOptions("\nLuu ban ve khi xong? [Co/Khong]", "Co Khong") { AllowNone = true };
            kw.Keywords.Default = "Khong";
            var sv = ed.GetKeywords(kw);
            if (sv.Status != PromptStatus.OK && sv.Status != PromptStatus.None) return;
            _mau = mau; _tang = tang; _prefix = prefix;

            var a1 = p1.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var a2 = p2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var d = new Dictionary<string, object>
            {
                ["mau"] = mau,
                ["window"] = new object[] { Math.Min(a1.X, a2.X), Math.Min(a1.Y, a2.Y), Math.Max(a1.X, a2.X), Math.Max(a1.Y, a2.Y) },
                ["tang"] = tang, ["prefix"] = prefix,
                ["shots"] = false, ["save"] = sv.Status == PromptStatus.OK && sv.StringResult == "Co",
                ["doc"] = System.IO.Path.GetFileName(ed.Document.Name),
            };
            if (st.Status == PromptStatus.OK) d["start"] = st.Value;
            ed.WriteMessage("\nCE_DANTRANG: bat dau (TAOMV, dat ten, soat). Dung thao tac AutoCAD toi khi in ket qua...");
            RunBackground("CE_DANTRANG", () => DanTrang.DanTrangTang(new Args(d)));
        }

        [CommandMethod("CE_SOATTO")]
        public void SoatToCmd()
        {
            var cur = LayoutManager.Current.CurrentLayout;
            var from = AskStr("To dau (vd 02-426)", cur == "Model" ? "" : cur, false); if (string.IsNullOrEmpty(from)) return;
            var to = AskStr("To cuoi", from, false); if (string.IsNullOrEmpty(to)) return;
            var kw = new PromptKeywordOptions("\nSua luon (net ngan, doi ten dam bi cat)? [Co/Khong]", "Co Khong") { AllowNone = true };
            kw.Keywords.Default = "Khong";
            var sv = Ed.GetKeywords(kw);
            if (sv.Status != PromptStatus.OK && sv.Status != PromptStatus.None) return;
            bool fix = sv.Status == PromptStatus.OK && sv.StringResult == "Co";
            var d = new Dictionary<string, object>
            {
                ["from"] = from, ["to"] = to, ["fix"] = fix, ["zoom"] = false,
                ["doc"] = System.IO.Path.GetFileName(Ed.Document.Name),
            };
            Print((Dictionary<string, object>)DanTrang.SoatTo(new Args(d)));
        }

        [CommandMethod("CE_NETKHUAT")]
        public void NetKhuatCmd()
        {
            var pat = AskStr("Mau ten layout (regex)", @"^\d+-4\d\d$", false); if (pat == null) return;
            var r = (Dictionary<string, object>)DanTrang.NetKhuatAll(new Args(new Dictionary<string, object>
            {
                ["pattern"] = pat, ["doc"] = System.IO.Path.GetFileName(Ed.Document.Name),
            }));
            Plugin.WriteLine($"\nCE_NETKHUAT: {r["so_layout"]} layout, sua {r["so_net"]} net ngan (LTscale 0.5, width 0.3).\n");
        }

        [CommandMethod("CE_CHUPTO")]
        public void ChupToCmd()
        {
            var cur = LayoutManager.Current.CurrentLayout;
            var d = new Dictionary<string, object> { ["layout"] = cur, ["doc"] = System.IO.Path.GetFileName(Ed.Document.Name) };
            RunBackgroundShot(d);
        }

        static void RunBackgroundShot(Dictionary<string, object> d)
        {
            Task.Run(() =>
            {
                try
                {
                    Thread.Sleep(500);
                    var r = (List<string>)DanTrang.LayoutShot(new Args(d));
                    MainThread.Run(() => { Plugin.WriteLine("\nCE_CHUPTO: " + string.Join(", ", r) + "\n"); return 0; });
                }
                catch (System.Exception ex) { Log.Write("CE_CHUPTO loi: " + ex); }
            });
        }
    }
}
