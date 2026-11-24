using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CivilEngi.AcadBridge
{
    /// <summary>
    /// Doc / bam hop thoai cua chinh process AutoCAD (MessageBox #32770, form WinForms cua add-in DCE...)
    /// bang Win32 tu thread pipe – khong can SendKeys, khong chiem ban phim.
    /// </summary>
    internal static class DialogWatcher
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, StringBuilder l, uint flags, uint timeout, out IntPtr res);

        const uint WM_GETTEXT = 0x000D, BM_CLICK = 0x00F5, GW_OWNER = 4, SMTO_ABORTIFHUNG = 2;
        static readonly uint Pid = (uint)Process.GetCurrentProcess().Id;

        static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
        static string Txt(IntPtr h)
        {
            var s = new StringBuilder(1024);
            SendMessageTimeout(h, WM_GETTEXT, (IntPtr)1024, s, SMTO_ABORTIFHUNG, 500, out _);
            return s.ToString();
        }
        static string Norm(string s) => (s ?? "").Replace("&", "").Trim();

        internal class Dlg
        {
            public IntPtr Hwnd; public string Title; public string Class;
            public List<string> Texts = new List<string>();
            public List<(IntPtr h, string text)> Buttons = new List<(IntPtr, string)>();
        }

        static List<Dlg> Find()
        {
            var res = new List<Dlg>();
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != Pid || !IsWindowVisible(h)) return true;
                string cls = Cls(h);
                bool isDlg = cls == "#32770";
                bool isForm = cls.StartsWith("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) && GetWindow(h, GW_OWNER) != IntPtr.Zero;
                if (!isDlg && !isForm) return true;
                var d = new Dlg { Hwnd = h, Title = Txt(h), Class = cls };
                EnumChildWindows(h, (c, l2) =>
                {
                    if (!IsWindowVisible(c)) return true;
                    string cc = Cls(c);
                    string t = Txt(c);
                    if (cc.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0) { if (t.Length > 0) d.Buttons.Add((c, t)); }
                    else if ((cc == "Static" || cc.IndexOf("STATIC", StringComparison.OrdinalIgnoreCase) >= 0) && t.Trim().Length > 0) d.Texts.Add(t.Trim());
                    return true;
                }, IntPtr.Zero);
                res.Add(d);
                return true;
            }, IntPtr.Zero);
            return res;
        }

        public static object List() => Find().Select(d => new Dictionary<string, object>
        {
            ["hwnd"] = d.Hwnd.ToInt64(),
            ["title"] = d.Title,
            ["class"] = d.Class,
            ["texts"] = d.Texts,
            ["buttons"] = d.Buttons.Select(b => Norm(b.text)).ToList(),
        }).Cast<object>().ToList();

        internal class Rule
        {
            public string Title, Text, Button;
            public static Rule From(object o)
            {
                var a = new Args(o);
                return new Rule { Title = a.Str("title"), Text = a.Str("text"), Button = a.ReqStr("button") };
            }
        }

        /// <summary>Bam nut theo luat; tra mo ta cac lan bam.</summary>
        public static List<string> Apply(IEnumerable<Rule> rules)
        {
            var clicked = new List<string>();
            var rs = rules?.ToList();
            if (rs == null || rs.Count == 0) return clicked;
            foreach (var d in Find())
            {
                foreach (var r in rs)
                {
                    if (r.Title != null && d.Title.IndexOf(r.Title, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (r.Text != null && !d.Texts.Any(t => t.IndexOf(r.Text, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    var b = d.Buttons.FirstOrDefault(x => Norm(x.text).Equals(Norm(r.Button), StringComparison.OrdinalIgnoreCase));
                    if (b.h == IntPtr.Zero || !IsWindowEnabled(b.h)) continue;
                    PostMessage(b.h, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    clicked.Add($"[{d.Title}] {string.Join(" ", d.Texts).Trim()} -> {Norm(b.text)}");
                    break;
                }
            }
            return clicked;
        }

        public static object ClickCmd(Args a)
        {
            var r = new Rule { Title = a.Str("title"), Text = a.Str("text"), Button = a.ReqStr("button") };
            var c = Apply(new[] { r });
            if (c.Count == 0) throw new BridgeException("Khong thay hop thoai / nut phu hop. Goi 'dialogs' de xem.");
            return c;
        }
    }
}
