using System;
using System.Globalization;

namespace SsmsToolkit.Data.Model
{
    /// <summary>Formats sys.columns type information like SSMS: nvarchar(50), decimal(18,2), varchar(max).</summary>
    public static class SqlTypeFormatter
    {
        /// <param name="typeName">TYPE_NAME(user_type_id); may be an alias type.</param>
        /// <param name="systemTypeName">TYPE_NAME(system_type_id); the base type that defines the length rules.</param>
        public static string Format(string typeName, string systemTypeName, int maxLength, int precision, int scale, bool isUserDefined)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return string.Empty;
            }

            // Alias types (e.g. dbo.Phone) are shown by their own name, like SSMS does.
            if (isUserDefined)
            {
                return typeName;
            }

            var baseType = (systemTypeName ?? typeName).ToLowerInvariant();
            switch (baseType)
            {
                case "nvarchar":
                case "nchar":
                    return typeName + "(" + (maxLength == -1 ? "max" : (maxLength / 2).ToString(CultureInfo.InvariantCulture)) + ")";
                case "varchar":
                case "char":
                case "varbinary":
                case "binary":
                    return typeName + "(" + (maxLength == -1 ? "max" : maxLength.ToString(CultureInfo.InvariantCulture)) + ")";
                case "decimal":
                case "numeric":
                    return typeName + "(" + precision.ToString(CultureInfo.InvariantCulture) + "," + scale.ToString(CultureInfo.InvariantCulture) + ")";
                case "datetime2":
                case "datetimeoffset":
                case "time":
                    return typeName + "(" + scale.ToString(CultureInfo.InvariantCulture) + ")";
                case "float":
                    return precision == 53 ? typeName : typeName + "(" + precision.ToString(CultureInfo.InvariantCulture) + ")";
                case "vector":
                    // vector(n) stores 4 bytes per float32 dimension plus an 8-byte header.
                    return maxLength > 8 ? typeName + "(" + ((maxLength - 8) / 4).ToString(CultureInfo.InvariantCulture) + ")" : typeName;
                default:
                    return typeName;
            }
        }

        /// <summary>Hex preview of binary values for grids; long values are truncated.</summary>
        public static string FormatBinary(byte[] value, int maxBytes = 32)
        {
            if (value == null)
            {
                return null;
            }

            int n = Math.Min(value.Length, maxBytes);
            var hex = BitConverter.ToString(value, 0, n).Replace("-", string.Empty);
            return "0x" + hex + (value.Length > maxBytes ? "…" : string.Empty);
        }
    }
}
