using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CivilEngi.AcadBridge
{
    internal static class U
    {
        public static double R(double v, int d = 3) => Math.Round(v, d);
        public static double[] P(Point3d p) => new[] { R(p.X), R(p.Y) };
        public static double[] P(Point2d p) => new[] { R(p.X), R(p.Y) };
        public static Point3d Pt(double[] a) => new Point3d(a[0], a[1], a.Length > 2 ? a[2] : 0);
        public static Point3d Pt(object o) => Pt(Args.ToNumArr(o));

        public static string H(ObjectId id) => id.Handle.Value.ToString("X");

        public static ObjectId Id(Database db, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return ObjectId.Null;
            long v;
            if (!long.TryParse(handle.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return ObjectId.Null;
            return db.TryGetObjectId(new Handle(v), out var id) && !id.IsErased ? id : ObjectId.Null;
        }

        public static Regex Wild(string pat)
        {
            var rx = "^" + Regex.Escape(pat).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        public static List<Regex> Wilds(List<string> pats) => pats?.Select(Wild).ToList();
        public static bool AnyMatch(List<Regex> rs, string s) => rs == null || rs.Any(r => r.IsMatch(s ?? ""));

        public static Extents3d? Ext(Entity e)
        {
            try
            {
                var x = e.GeometricExtents;
                if (double.IsNaN(x.MinPoint.X) || x.MaxPoint.X - x.MinPoint.X > 1e20) return null;
                return x;
            }
            catch { return null; }
        }
        public static double[] Box(Extents3d x) => new[] { R(x.MinPoint.X), R(x.MinPoint.Y), R(x.MaxPoint.X), R(x.MaxPoint.Y) };

        public static string BlockName(Transaction tr, BlockReference br)
        {
            try
            {
                var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
            }
            catch { return br.Name; }
        }

        public static void EnsureLayer(Transaction tr, Database db, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        public static object Clean(object v)
        {
            switch (v)
            {
                case null: return null;
                case double d: return R(d, 6);
                case Point3d p: return new[] { R(p.X), R(p.Y), R(p.Z) };
                case Point2d p2: return new[] { R(p2.X), R(p2.Y) };
                case Vector3d vv: return new[] { R(vv.X), R(vv.Y), R(vv.Z) };
                case ObjectId oid: return oid.IsNull ? null : H(oid);
                case Handle h: return h.Value.ToString("X");
                case string s: return s;
                case bool b: return b;
                case short or int or long or float: return v;
                default: return v.ToString();
            }
        }
    }

    /// <summary>Chon ban ve, khong gian, kiem trang thai lenh. Chi goi tren main thread.</summary>
    internal static class Docs
    {
        public static Document Resolve(Args a)
        {
            var want = a.Str("doc");
            var dm = Application.DocumentManager;
            if (string.IsNullOrWhiteSpace(want))
                return dm.MdiActiveDocument ?? throw new BridgeException("Khong co ban ve nao dang mo");
            foreach (Document d in dm)
                if (System.IO.Path.GetFileName(d.Name).IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return d;
            throw new BridgeException("Khong thay ban ve chua '" + want + "'");
        }

        public static int CmdActive()
        {
            try { return Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")); } catch { return 0; }
        }
        public static string CmdNames()
        {
            try { return Convert.ToString(Application.GetSystemVariable("CMDNAMES")); } catch { return ""; }
        }

        /// <summary>Chan sua ban ve khi AutoCAD dang chay lenh (tranh hong du lieu), tru khi force=true.</summary>
        public static void EnsureIdle(Document doc, Args a)
        {
            if (a.Bool("force")) return;
            if (doc != Application.DocumentManager.MdiActiveDocument) return;
            int c = CmdActive();
            if (c != 0)
                throw new BusyException($"AutoCAD dang chay lenh '{CmdNames()}' (CMDACTIVE={c}). Cho lenh xong, goi command_cancel, hoac gui force=true.");
        }

        public static ObjectId Space(Transaction tr, Database db, string space)
        {
            if (string.IsNullOrWhiteSpace(space) || space.Equals("model", StringComparison.OrdinalIgnoreCase))
                return SymbolUtilityServices.GetBlockModelSpaceId(db);
            if (space.Equals("current", StringComparison.OrdinalIgnoreCase)) return db.CurrentSpaceId;
            var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry en in dict)
                if (en.Key.Equals(space, StringComparison.OrdinalIgnoreCase))
                    return ((Layout)tr.GetObject(en.Value, OpenMode.ForRead)).BlockTableRecordId;
            throw new BridgeException("Khong co layout '" + space + "'");
        }

        public static Layout FindLayout(Transaction tr, Database db, string name)
        {
            var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry en in dict)
                if (en.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (Layout)tr.GetObject(en.Value, OpenMode.ForRead);
            throw new BridgeException("Khong co layout '" + name + "'");
        }
    }
}
