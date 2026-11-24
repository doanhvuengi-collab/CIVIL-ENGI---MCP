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
            public List<(IntPtr h, string text)> Combos = new List<(IntPtr, string)>();
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
                // form WinForms cua add-in (co hoac khong co cua so cha). Bo qua cua so an / rong.
                bool isForm = cls.StartsWith("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) && Txt(h).Trim().Length > 0;
                if (!isDlg && !isForm) return true;
                var d = new Dlg { Hwnd = h, Title = Txt(h), Class = cls };
                EnumChildWindows(h, (c, l2) =>
                {
                    if (!IsWindowVisible(c)) return true;
                    string cc = Cls(c);
                    string t = Txt(c);
                    if (cc.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0) { if (t.Length > 0) d.Buttons.Add((c, t)); }
                    else if (cc.IndexOf("COMBOBOX", StringComparison.OrdinalIgnoreCase) >= 0) d.Combos.Add((c, t));
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
            ["checked"] = d.Buttons.Where(b => IsCheck(b.h)).Select(b => Norm(b.text) + "=" + (Checked(b.h) ? "1" : "0")).ToList(),
            ["combos"] = d.Combos.Select((c, i) => new Dictionary<string, object> { ["index"] = i, ["text"] = c.text, ["items"] = ComboItems(c.h) }).Cast<object>().ToList(),
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

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, StringBuilder l);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        const uint BM_GETCHECK = 0xF0, CB_GETCOUNT = 0x146, CB_GETLBTEXT = 0x148, CB_GETLBTEXTLEN = 0x149,
                   CB_SELECTSTRING = 0x14D, CB_GETCURSEL = 0x147, WM_COMMAND = 0x111, CBN_SELCHANGE = 1;

        static bool IsCheck(IntPtr h)
        {
            int st = GetWindowLong(h, -16) & 0xF; // BS_TYPEMASK
            return st == 2 || st == 3 || st == 5 || st == 6; // checkbox / auto / 3state
        }
        static bool Checked(IntPtr h) => SendMessage(h, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() == 1;

        static List<string> ComboItems(IntPtr h)
        {
            var r = new List<string>();
            int n = (int)SendMessage(h, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < Math.Min(n, 500); i++)
            {
                int len = (int)SendMessage(h, CB_GETLBTEXTLEN, (IntPtr)i, IntPtr.Zero);
                if (len < 0) continue;
                var sb = new StringBuilder(len + 2);
                SendMessage(h, CB_GETLBTEXT, (IntPtr)i, sb);
                r.Add(sb.ToString());
            }
            return r;
        }

        /// <summary>Chon muc trong combobox cua form: title (chuoi con), index (thu tu combo, xem 'dialogs') hoac current (text hien tai), value.</summary>
        public static object SelectCmd(Args a)
        {
            string title = a.Str("title"), value = a.ReqStr("value"), cur = a.Str("current");
            foreach (var d in Find())
            {
                if (title != null && d.Title.IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0) continue;
                for (int i = 0; i < d.Combos.Count; i++)
                {
                    var c = d.Combos[i];
                    if (a.Has("index") && a.Int("index") != i) continue;
                    if (cur != null && c.text.IndexOf(cur, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int idx = (int)SendMessage(c.h, CB_SELECTSTRING, (IntPtr)(-1), value);
                    if (idx < 0) throw new BridgeException("Combo khong co muc '" + value + "'. Cac muc: " + string.Join(" | ", ComboItems(c.h)));
                    int id = GetDlgCtrlID(c.h);
                    SendMessage(GetParent(c.h), WM_COMMAND, (IntPtr)((CBN_SELCHANGE << 16) | (id & 0xFFFF)), c.h);
                    return new Dictionary<string, object> { ["dialog"] = d.Title, ["combo"] = i, ["selected"] = Txt(c.h) };
                }
            }
            throw new BridgeException("Khong thay combobox phu hop. Goi 'dialogs' de xem.");
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
