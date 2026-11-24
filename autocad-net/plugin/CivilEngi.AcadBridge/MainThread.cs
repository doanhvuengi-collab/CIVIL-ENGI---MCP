using System;
using System.Threading;
using System.Windows.Forms;

namespace CivilEngi.AcadBridge
{
    /// <summary>Loi khi AutoCAD khong nhan viec trong thoi gian cho (dang tinh nang / treo).</summary>
    public class BusyException : Exception
    {
        public BusyException(string m) : base(m) { }
    }

    /// <summary>Loi nghiep vu tra ve cho client (khong ghi stack).</summary>
    public class BridgeException : Exception
    {
        public BridgeException(string m) : base(m) { }
    }

    /// <summary>
    /// Day viec tu thread pipe sang main thread AutoCAD bang Control.BeginInvoke
    /// (chay trong application context, ca khi AutoCAD dang cho nhap lenh / mo hop thoai).
    /// </summary>
    internal static class MainThread
    {
        static Control _ctl;

        public static void Init()
        {
            _ctl = new Control();
            _ctl.CreateControl();
            var _ = _ctl.Handle;
        }

        public static T Run<T>(Func<T> f, int startTimeoutMs = 15000, int runTimeoutMs = 600000)
        {
            if (_ctl == null) throw new InvalidOperationException("MainThread chua Init");
            if (!_ctl.InvokeRequired) return f();

            var lk = new object();
            bool started = false, cancelled = false;
            T result = default;
            Exception err = null;
            using (var done = new ManualResetEventSlim(false))
            {
                _ctl.BeginInvoke((Action)(() =>
                {
                    lock (lk)
                    {
                        if (cancelled) return;
                        started = true;
                    }
                    try { result = f(); }
                    catch (Exception ex) { err = ex; }
                    finally { try { done.Set(); } catch { } }
                }));

                if (!done.Wait(startTimeoutMs))
                {
                    lock (lk)
                    {
                        if (!started)
                        {
                            cancelled = true;
                            throw new BusyException($"AutoCAD khong nhan viec sau {startTimeoutMs / 1000.0:0.#} s (dang ban tinh toan / co hop thoai treo).");
                        }
                    }
                    if (!done.Wait(runTimeoutMs))
                        throw new BusyException("Viec chay tren AutoCAD qua lau (> " + runTimeoutMs / 1000 + " s).");
                }
                if (err != null)
                {
                    if (err is System.Reflection.TargetInvocationException tie && tie.InnerException != null) err = tie.InnerException;
                    throw err;
                }
                return result;
            }
        }
    }
}
