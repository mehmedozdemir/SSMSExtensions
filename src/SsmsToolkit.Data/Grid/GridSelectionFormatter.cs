using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SsmsToolkit.Data.Grid
{
    public enum GridCopyFormat
    {
        Csv,
        Json,
        Markdown,
        Insert,
        InList,
    }

    /// <summary>Turns a grid selection into clipboard text. Pure functions; no SSMS dependency.</summary>
    public static class GridSelectionFormatter
    {
        public static string Format(GridSelectionData data, GridCopyFormat format)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            switch (format)
            {
                case GridCopyFormat.Csv: return ToCsv(data);
                case GridCopyFormat.Json: return ToJson(data);
                case GridCopyFormat.Markdown: return ToMarkdown(data);
                case GridCopyFormat.Insert: return ToInsert(data);
                case GridCopyFormat.InList: return ToInList(data);
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        public static string ToCsv(GridSelectionData data, char separator = ',')
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(separator.ToString(), data.Columns.Select(c => CsvField(c.Name, separator)))).Append("\r\n");
            foreach (var row in data.Rows)
            {
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(separator);
                    }
                    var text = SqlLiteral.FormatText(row[i], data.Columns[i].SqlTypeName);
                    if (text != null)
                    {
                        sb.Append(CsvField(text, separator));
                    }
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        public static string ToJson(GridSelectionData data)
        {
            var names = UniqueNames(data.Columns.Select(c => c.Name));
            var sb = new StringBuilder("[");
            for (int r = 0; r < data.Rows.Count; r++)
            {
                sb.Append(r == 0 ? "\r\n  {" : ",\r\n  {");
                var row = data.Rows[r];
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(JsonString(names[i])).Append(": ").Append(JsonValue(row[i], data.Columns[i].SqlTypeName));
                }
                sb.Append('}');
            }
            sb.Append(data.Rows.Count == 0 ? "]" : "\r\n]");
            return sb.ToString();
        }

        public static string ToMarkdown(GridSelectionData data)
        {
            var sb = new StringBuilder();
            sb.Append("| ").Append(string.Join(" | ", data.Columns.Select(c => MarkdownCell(c.Name)))).Append(" |\r\n");
            sb.Append('|').Append(string.Join("|", data.Columns.Select(c => SqlLiteral.IsNumericType(c.SqlTypeName) ? " ---: " : " --- "))).Append("|\r\n");
            foreach (var row in data.Rows)
            {
                sb.Append("| ");
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(" | ");
                    }
                    var text = SqlLiteral.FormatText(row[i], data.Columns[i].SqlTypeName);
                    sb.Append(text == null ? "NULL" : MarkdownCell(text));
                }
                sb.Append(" |\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Multi-row INSERT statements (1000 rows each) into the source table from the result schema, or a placeholder.
        /// The "INSERT statements" dialog uses <see cref="InsertScriptBuilder"/> directly with the user's options.
        /// </summary>
        public static string ToInsert(GridSelectionData data) =>
            InsertScriptBuilder.Build(data.Columns, data.Rows, new InsertScriptOptions
            {
                TableName = InsertScriptBuilder.DefaultTableName(data.Columns),
                RowsPerStatement = InsertScriptOptions.MaxRowsPerStatement,
                ExcludeGeneratedColumns = false,
            });

        /// <summary>
        /// "IN (...)" with the distinct non-NULL values of the selection, in selection order (NULL never matches IN).
        /// With several columns selected, values are taken row by row.
        /// </summary>
        public static string ToInList(GridSelectionData data)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var literals = new List<string>();
            foreach (var row in data.Rows)
            {
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    if (GridSelectionData.IsNull(row[i]))
                    {
                        continue;
                    }
                    var literal = SqlLiteral.Format(row[i], data.Columns[i].SqlTypeName);
                    if (seen.Add(literal))
                    {
                        literals.Add(literal);
                    }
                }
            }

            if (literals.Count == 0)
            {
                return "IN (NULL)";
            }

            // Short lists on one line; long lists wrapped every 10 values for readability.
            if (literals.Count <= 10)
            {
                return "IN (" + string.Join(", ", literals) + ")";
            }

            var sb = new StringBuilder("IN (\r\n");
            for (int i = 0; i < literals.Count; i += 10)
            {
                sb.Append("    ").Append(string.Join(", ", literals.Skip(i).Take(10)));
                sb.Append(i + 10 < literals.Count ? ",\r\n" : "\r\n");
            }
            return sb.Append(')').ToString();
        }

        private static string CsvField(string text, char separator)
        {
            if (text.IndexOfAny(new[] { separator, '"', '\r', '\n' }) < 0 && text.Trim() == text)
            {
                return text;
            }
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string MarkdownCell(string text) =>
            text.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

        private static string JsonValue(object value, string sqlTypeName)
        {
            if (GridSelectionData.IsNull(value))
            {
                return "null";
            }

            var type = (sqlTypeName ?? string.Empty).ToLowerInvariant();
            if (type == "bit" || value is bool)
            {
                return SqlLiteral.Format(value, "bit") == "1" ? "true" : "false";
            }
            if (SqlLiteral.IsNumericType(type))
            {
                var number = SqlLiteral.Format(value, type);
                if (number != "NULL" && !number.StartsWith("N'", StringComparison.Ordinal) && !number.StartsWith("'", StringComparison.Ordinal))
                {
                    return number;
                }
            }
            return JsonString(SqlLiteral.FormatText(value, type));
        }

        private static string JsonString(string text)
        {
            var sb = new StringBuilder(text.Length + 2).Append('"');
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Unnamed or duplicate columns ("(No column name)", two "Id"s) get unique JSON keys.</summary>
        private static string[] UniqueNames(IEnumerable<string> names)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            int index = 0;
            foreach (var raw in names)
            {
                index++;
                var name = string.IsNullOrEmpty(raw) ? "Column" + index : raw;
                var candidate = name;
                for (int n = 2; !used.Add(candidate); n++)
                {
                    candidate = name + "_" + n;
                }
                result.Add(candidate);
            }
            return result.ToArray();
        }
    }
}
