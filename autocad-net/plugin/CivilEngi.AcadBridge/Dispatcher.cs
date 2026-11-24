using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CivilEngi.AcadBridge
{
    internal static class Dispatcher
    {
        delegate object Handler(Args a);

        static readonly Dictionary<string, Handler> Map = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase)
        {
            ["ping"] = Commands.Ping,
            ["help"] = a => new List<string>(Map.Keys),
            ["status"] = Commands.Status,
            ["doc_activate"] = Commands.DocActivate,
            ["layouts"] = Commands.Layouts,
            ["layout_set"] = Commands.LayoutSet,
            ["layout_rename"] = Commands.LayoutRename,
            ["layout_reorder"] = Commands.LayoutReorder,
            ["query"] = Commands.Query,
            ["get"] = Commands.Get,
            ["delete"] = Commands.Delete,
            ["move"] = Commands.Move,
            ["copy"] = Commands.Copy,
            ["set_props"] = Commands.SetProps,
            ["set_text"] = Commands.SetText,
            ["set_attributes"] = Commands.SetAttributes,
            ["set_xdata"] = Commands.SetXData,
            ["draw"] = Commands.Draw,
            ["viewports"] = Commands.Viewports,
            ["viewport_set"] = Commands.ViewportSet,
            ["viewport_cut_check"] = Commands.ViewportCutCheck,
            ["find_overlaps"] = Commands.FindOverlaps,
            ["dce_sck"] = Commands.DceSck,
            ["zoom"] = Commands.Zoom,
            ["regen"] = Commands.Regen,
            ["var_get"] = Commands.VarGet,
            ["var_set"] = Commands.VarSet,
            ["save"] = Commands.Save,
            ["backup"] = Commands.Backup,
            ["pick_points"] = Commands.PickPoints,
            ["select"] = Commands.SelectOnScreen,
            ["command"] = CommandRunner.Run,
            ["command_status"] = CommandRunner.Status,
            ["command_cancel"] = CommandRunner.Cancel,
            ["dialogs"] = a => DialogWatcher.List(),
            ["dialog_click"] = DialogWatcher.ClickCmd,
        };

        public static string Handle(string line)
        {
            object id = null;
            string cmd = null;
            var sw = Stopwatch.StartNew();
            try
            {
                var req = Args.AsDict(Json.Parse(line));
                req.TryGetValue("id", out id);
                cmd = req.TryGetValue("cmd", out var c) ? Convert.ToString(c) : null;
                if (string.IsNullOrEmpty(cmd)) throw new BridgeException("Thieu 'cmd'");
                if (!Map.TryGetValue(cmd, out var h)) throw new BridgeException("Lenh khong co: " + cmd + ". Goi 'help' de xem danh sach.");
                var result = h(new Args(req.TryGetValue("args", out var a) ? a : null));
                return Json.Write(new Dictionary<string, object> { ["id"] = id, ["ok"] = true, ["ms"] = sw.ElapsedMilliseconds, ["result"] = result });
            }
            catch (Exception ex)
            {
                if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null) ex = tie.InnerException;
                string kind = ex is BusyException ? "busy" : ex is BridgeException ? "error" : "exception";
                if (kind == "exception") Log.Write($"Lenh {cmd} loi: {ex}");
                return Json.Write(new Dictionary<string, object>
                {
                    ["id"] = id, ["ok"] = false, ["kind"] = kind, ["ms"] = sw.ElapsedMilliseconds,
                    ["error"] = ex.GetType().Name == "Exception" || ex is BridgeException || ex is BusyException ? ex.Message : ex.GetType().Name + ": " + ex.Message
                });
            }
        }
    }
}
