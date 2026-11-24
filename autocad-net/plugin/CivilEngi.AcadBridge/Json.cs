using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace CivilEngi.AcadBridge
{
    internal static class Json
    {
        static JavaScriptSerializer Ser() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
        public static object Parse(string s) => Ser().DeserializeObject(s);
        public static string Write(object o) => Ser().Serialize(o);
    }

    /// <summary>Doc tham so JSON an toan (so co the la int/decimal, mang la object[]).</summary>
    internal class Args
    {
        readonly Dictionary<string, object> _d;
        public Args(object o)
        {
            _d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (o is IDictionary<string, object> m) foreach (var kv in m) _d[kv.Key] = kv.Value;
        }
        public bool Has(string k) => _d.TryGetValue(k, out var v) && v != null;
        public object Raw(string k) => _d.TryGetValue(k, out var v) ? v : null;

        public string Str(string k, string def = null) => Has(k) ? Convert.ToString(_d[k], CultureInfo.InvariantCulture) : def;
        public double Num(string k, double def = 0) => Has(k) ? ToDouble(_d[k]) : def;
        public int Int(string k, int def = 0) => Has(k) ? (int)Math.Round(ToDouble(_d[k])) : def;
        public bool Bool(string k, bool def = false)
        {
            if (!Has(k)) return def;
            var v = _d[k];
            if (v is bool b) return b;
            var s = Convert.ToString(v, CultureInfo.InvariantCulture).ToLowerInvariant();
            return s == "1" || s == "true" || s == "yes";
        }
        public string ReqStr(string k) => Str(k) ?? throw new BridgeException("Thieu tham so '" + k + "'");
        public double ReqNum(string k) => Has(k) ? ToDouble(_d[k]) : throw new BridgeException("Thieu tham so '" + k + "'");

        public List<string> StrList(string k)
        {
            if (!Has(k)) return null;
            var v = _d[k];
            if (v is string s) return s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            if (v is IEnumerable e) return e.Cast<object>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
            return new List<string> { Convert.ToString(v, CultureInfo.InvariantCulture) };
        }
        public double[] NumArr(string k) => Has(k) ? ToNumArr(_d[k]) : null;
        public Dictionary<string, object> Dict(string k) => Has(k) ? AsDict(_d[k]) : null;
        public List<object> List(string k) => Has(k) && _d[k] is IEnumerable e && !(_d[k] is string) ? e.Cast<object>().ToList() : null;
        public Args Sub(string k) => new Args(Raw(k));

        public static double ToDouble(object v)
        {
            if (v == null) return 0;
            if (v is string s) return double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }
        public static double[] ToNumArr(object v)
        {
            if (v is IEnumerable e && !(v is string)) return e.Cast<object>().Select(ToDouble).ToArray();
            throw new BridgeException("Can mang so, nhan: " + v);
        }
        public static Dictionary<string, object> AsDict(object v)
        {
            var r = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (v is IDictionary<string, object> m) foreach (var kv in m) r[kv.Key] = kv.Value;
            return r;
        }
    }
}
