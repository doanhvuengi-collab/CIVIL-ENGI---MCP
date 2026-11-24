using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CivilEngi.AcadBridge
{
    /// <summary>
    /// Chay lenh AutoCAD / add-in (VDAll, VD, TAOMV...) bang SendStringToExecute, ghi lai doi tuong moi tao,
    /// tu bam hop thoai theo luat, va KHONG treo: neu lenh dung cho nhap qua stall_s giay thi tra "waiting_input".
    /// </summary>
    internal static class CommandRunner
    {
        class Job
        {
            public string Token, Command, Doc;
            public Database Db;
            public Stopwatch Clock = Stopwatch.StartNew();
            public readonly List<ObjectId> Appended = new List<ObjectId>();
            public ObjectEventHandler Handler;
            public List<DialogWatcher.Rule> Rules = new List<DialogWatcher.Rule>();
            public List<string> Clicked = new List<string>();
            public bool SawActive, Recording = true;
            public string State = "starting", CmdNames = "";
            public int Count { get { lock (Appended) return Appended.Count; } }
        }

        static readonly object Lk = new object();
        static Job _current;
        static int _seq;

        public static object Run(Args a)
        {
            string cmd = a.ReqStr("command");
            if (!(cmd.EndsWith("\n") || cmd.EndsWith(" "))) cmd += "\n";
            var job = new Job { Command = cmd, Token = "cmd" + Interlocked.Increment(ref _seq) };
            var rules = a.List("auto_answer");
            if (rules != null) job.Rules = rules.Select(DialogWatcher.Rule.From).ToList();

            Job prev;
            lock (Lk) { prev = _current; _current = job; }
            if (prev != null && prev.Recording) StopRecording(prev);

            MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                if (doc != Application.DocumentManager.MdiActiveDocument) Application.DocumentManager.MdiActiveDocument = doc;
                Docs.EnsureIdle(doc, a);
                job.Doc = Path.GetFileName(doc.Name);
                job.Db = doc.Database;
                job.Handler = (s, e) =>
                {
                    try { if (e.DBObject is Entity) lock (job.Appended) job.Appended.Add(e.DBObject.ObjectId); } catch { }
                };
                job.Db.ObjectAppended += job.Handler;
                if (a.Has("layout")) using (doc.LockDocument()) LayoutManager.Current.CurrentLayout = a.Str("layout");
                doc.SendStringToExecute(cmd, true, false, false);
                return 0;
            });
            Log.Write($"command {job.Token}: {cmd.Replace("\n", "\\n")}");
            if (!a.Bool("wait", true)) return new Dictionary<string, object> { ["token"] = job.Token, ["state"] = "sent" };
            return Wait(job, a);
        }

        static object Wait(Job job, Args a)
        {
            double timeout = a.Num("timeout_s", 120), stall = a.Num("stall_s", 20);
            var sw = Stopwatch.StartNew();
            var lastProgress = Stopwatch.StartNew();
            int lastCount = job.Count, idle = 0;
            while (true)
            {
                Thread.Sleep(300);
                job.Clicked.AddRange(DialogWatcher.Apply(job.Rules));
                int c = job.Count;
                if (c != lastCount) { lastCount = c; lastProgress.Restart(); }
                int active; bool responsive = true;
                try
                {
                    var st = MainThread.Run(() => (Docs.CmdActive(), Docs.CmdNames()), 1500);
                    active = st.Item1; job.CmdNames = st.Item2;
                }
                catch (BusyException) { active = 1; responsive = false; lastProgress.Restart(); }

                if (active != 0)
                {
                    job.SawActive = true; idle = 0;
                    bool dialogOpen = (active & 8) != 0;
                    if (responsive && !dialogOpen && stall > 0 && lastProgress.Elapsed.TotalSeconds > stall)
                    { job.State = "waiting_input"; break; }
                    if (dialogOpen && stall > 0 && lastProgress.Elapsed.TotalSeconds > stall)
                    { job.State = "dialog_open"; break; }
                }
                else if (job.SawActive || c > 0 || job.Clock.Elapsed.TotalSeconds > 3)
                {
                    if (++idle >= 3) { job.State = "done"; break; }
                }
                if (sw.Elapsed.TotalSeconds > timeout) { job.State = "running"; break; }
            }
            if (job.State == "done") StopRecording(job);
            return Summary(job, a);
        }

        static void StopRecording(Job job)
        {
            if (!job.Recording) return;
            job.Recording = false;
            try { MainThread.Run(() => { job.Db.ObjectAppended -= job.Handler; return 0; }, 5000); }
            catch (Exception ex) { Log.Write("StopRecording: " + ex.Message); }
        }

        static object Summary(Job job, Args a)
        {
            bool withHandles = a.Bool("return_handles", true);
            int limit = a.Int("limit", 5000);
            var r = new Dictionary<string, object>
            {
                ["token"] = job.Token, ["state"] = job.State, ["doc"] = job.Doc,
                ["elapsed_s"] = Math.Round(job.Clock.Elapsed.TotalSeconds, 1), ["cmdnames"] = job.CmdNames,
                ["dialogs_clicked"] = job.Clicked,
            };
            if (job.State == "waiting_input") r["hint"] = "Lenh dang cho nhap. Gui them bang command (force=true) hoac command_cancel.";
            if (job.State == "dialog_open") r["hint"] = "Co hop thoai dang mo. Goi dialogs de xem, dialog_click de bam.";
            try
            {
                MainThread.Run(() =>
                {
                    List<ObjectId> ids;
                    lock (job.Appended) ids = job.Appended.ToList();
                    var handles = new List<string>();
                    var byLayer = new Dictionary<string, int>();
                    var byType = new Dictionary<string, int>();
                    double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                    using (var tr = job.Db.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (var id in ids)
                        {
                            if (id.IsNull || id.IsErased) continue;
                            if (!(tr.GetObject(id, OpenMode.ForRead, true) is Entity e) || e.IsErased) continue;
                            if (!(tr.GetObject(e.OwnerId, OpenMode.ForRead) is BlockTableRecord owner) || !owner.IsLayout) continue;
                            handles.Add(U.H(id));
                            byLayer[e.Layer] = byLayer.TryGetValue(e.Layer, out var n) ? n + 1 : 1;
                            var t = id.ObjectClass.DxfName;
                            byType[t] = byType.TryGetValue(t, out var m) ? m + 1 : 1;
                            var x = U.Ext(e);
                            if (x.HasValue)
                            {
                                x0 = Math.Min(x0, x.Value.MinPoint.X); y0 = Math.Min(y0, x.Value.MinPoint.Y);
                                x1 = Math.Max(x1, x.Value.MaxPoint.X); y1 = Math.Max(y1, x.Value.MaxPoint.Y);
                            }
                        }
                        tr.Commit();
                    }
                    r["new_count"] = handles.Count;
                    r["by_layer"] = byLayer;
                    r["by_type"] = byType;
                    if (handles.Count > 0 && x0 < double.MaxValue) r["bbox"] = new[] { U.R(x0), U.R(y0), U.R(x1), U.R(y1) };
                    if (withHandles) r["handles"] = handles.Take(limit).ToList();
                    return 0;
                }, 3000, 120000);
            }
            catch (BusyException ex) { r["new_count_pending"] = job.Count; r["note"] = ex.Message; }
            return r;
        }

        /// <summary>Theo doi tiep lenh da gui (token mac dinh = lenh gan nhat). timeout_s mac dinh 0 = xem ngay.</summary>
        public static object Status(Args a)
        {
            Job job;
            lock (Lk) job = _current;
            if (job == null) throw new BridgeException("Chua co lenh nao");
            if (a.Has("token") && a.Str("token") != job.Token) throw new BridgeException("Chi theo doi duoc lenh gan nhat: " + job.Token);
            if (!job.Recording) return Summary(job, a);
            var d = new Dictionary<string, object>();
            foreach (var k in new[] { "stall_s", "return_handles", "limit" }) if (a.Has(k)) d[k] = a.Raw(k);
            d["timeout_s"] = a.Num("timeout_s", 0);
            return Wait(job, new Args(d));
        }

        /// <summary>Huy lenh dang chay (Esc Esc) va dung ghi.</summary>
        public static object Cancel(Args a)
        {
            MainThread.Run(() =>
            {
                var doc = Docs.Resolve(a);
                doc.SendStringToExecute("\x03\x03", true, false, false);
                return 0;
            });
            Thread.Sleep(500);
            Job job;
            lock (Lk) job = _current;
            if (job != null && job.Recording) { job.State = "cancelled"; StopRecording(job); return Summary(job, a); }
            return "sent";
        }
    }
}
