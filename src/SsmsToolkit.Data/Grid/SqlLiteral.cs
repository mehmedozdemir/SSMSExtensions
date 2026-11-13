using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;

namespace SsmsToolkit.Data.Grid
{
    /// <summary>Formats grid values as T-SQL literals (for INSERT scripts and IN lists).</summary>
    public static class SqlLiteral
    {
        private static readonly HashSet<string> IntegerTypes = Set("bigint", "int", "smallint", "tinyint");
        private static readonly HashSet<string> ExactTypes = Set("decimal", "numeric", "money", "smallmoney");
        private static readonly HashSet<string> FloatTypes = Set("float", "real");
        private static readonly HashSet<string> AnsiTypes = Set("char", "varchar", "text");
        private static readonly HashSet<string> BinaryTypes = Set("binary", "varbinary", "image", "timestamp", "rowversion");

        /// <summary>True for types whose values are plain numbers in T-SQL and JSON.</summary>
        public static bool IsNumericType(string sqlTypeName) =>
            IntegerTypes.Contains(sqlTypeName) || ExactTypes.Contains(sqlTypeName) || FloatTypes.Contains(sqlTypeName);

        public static string Format(object value, string sqlTypeName)
        {
            if (GridSelectionData.IsNull(value))
            {
                return "NULL";
            }

            var type = (sqlTypeName ?? string.Empty).ToLowerInvariant();
            value = Unwrap(value);

            if (type == "bit")
            {
                return ToBoolean(value) ? "1" : "0";
            }
            if (IsNumericType(type) || (type.Length == 0 && IsClrNumber(value)))
            {
                var number = FormatNumber(value);
                if (number != null)
                {
                    return number;
                }
            }
            if (value is byte[] bytes)
            {
                return Hex(bytes);
            }
            if (BinaryTypes.Contains(type) && value is string s0 && s0.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return s0;
            }

            // Unicode prefix only for text; dates, times and GUIDs are plain ISO / canonical strings.
            string text = FormatText(value, type);
            bool unicode = value is string && !AnsiTypes.Contains(type);
            return (unicode ? "N'" : "'") + text.Replace("'", "''") + "'";
        }

        /// <summary>Invariant text for a value: ISO dates, invariant numbers, hex for binary.</summary>
        public static string FormatText(object value, string sqlTypeName)
        {
            if (GridSelectionData.IsNull(value))
            {
                return null;
            }

            var type = (sqlTypeName ?? string.Empty).ToLowerInvariant();
            value = Unwrap(value);
            switch (value)
            {
                case string s:
                    return s;
                case byte[] bytes:
                    return Hex(bytes);
                case bool b:
                    return b ? "1" : "0";
                case DateTime dt:
                    return FormatDateTime(dt, type);
                case DateTimeOffset dto:
                    return dto.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
                case TimeSpan ts:
                    return ts.ToString(ts.Ticks % TimeSpan.TicksPerSecond == 0 ? @"hh\:mm\:ss" : @"hh\:mm\:ss\.FFFFFFF", CultureInfo.InvariantCulture);
                case Guid g:
                    return g.ToString("D").ToUpperInvariant();
                case double d:
                    return d.ToString("R", CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Converts a numeric grid value to decimal; null when it is not a (finite) number.</summary>
        public static decimal? ToDecimal(object value, string sqlTypeName = null)
        {
            if (GridSelectionData.IsNull(value))
            {
                return null;
            }

            value = Unwrap(value);
            try
            {
                switch (value)
                {
                    case decimal m: return m;
                    case int i: return i;
                    case long l: return l;
                    case short sh: return sh;
                    case byte by: return by;
                    case double d: return double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 7.9e28 ? (decimal?)null : (decimal)d;
                    case float f: return float.IsNaN(f) || float.IsInfinity(f) || Math.Abs(f) > 7.9e28f ? (decimal?)null : (decimal)f;
                    case string s when IsNumericType((sqlTypeName ?? string.Empty).ToLowerInvariant()):
                        return decimal.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null;
                    default: return null;
                }
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        private static object Unwrap(object value)
        {
            switch (value)
            {
                case SqlDecimal sd:
                    try { return sd.Value; } catch (OverflowException) { return sd.ToString(); }
                case SqlMoney sm: return sm.Value;
                case SqlString ss: return ss.Value;
                case SqlInt32 si: return si.Value;
                case SqlInt64 sl: return sl.Value;
                case SqlInt16 s16: return s16.Value;
                case SqlByte sb: return sb.Value;
                case SqlDouble sdo: return sdo.Value;
                case SqlSingle ssi: return ssi.Value;
                case SqlBoolean sbo: return sbo.Value;
                case SqlDateTime sdt: return sdt.Value;
                case SqlGuid sg: return sg.Value;
                case SqlBinary sbi: return sbi.Value;
                case SqlBytes sby: return sby.Value;
                case SqlChars sch: return new string(sch.Value);
                case SqlXml sx: return sx.Value;
                default: return value;
            }
        }

        private static string FormatNumber(object value)
        {
            switch (value)
            {
                case double d:
                    return double.IsNaN(d) || double.IsInfinity(d) ? null : d.ToString("R", CultureInfo.InvariantCulture);
                case float f:
                    return float.IsNaN(f) || float.IsInfinity(f) ? null : f.ToString("R", CultureInfo.InvariantCulture);
                case string s:
                    var t = s.Trim();
                    return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                           || double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? t : null;
                case IFormattable formattable when IsClrNumber(value):
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return null;
            }
        }

        private static bool IsClrNumber(object value) =>
            value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint
            || value is long || value is ulong || value is decimal || value is double || value is float;

        private static bool ToBoolean(object value)
        {
            switch (value)
            {
                case bool b: return b;
                case string s: return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                default:
                    try { return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0; } catch { return false; }
            }
        }

        private static string FormatDateTime(DateTime dt, string type)
        {
            switch (type)
            {
                case "date":
                    return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "datetime":
                case "smalldatetime":
                    return dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
                default:
                    return dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(2 + bytes.Length * 2).Append("0x");
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static HashSet<string> Set(params string[] items) => new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
    }
}
