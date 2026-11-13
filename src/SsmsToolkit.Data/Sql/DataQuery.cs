using System;
using System.Globalization;
using System.Text;
using SsmsToolkit.Data.Model;

namespace SsmsToolkit.Data.Sql
{
    /// <summary>Parameters of the Data tab query.</summary>
    public sealed class DataQuery
    {
        public const int MaxRowLimit = 100000;

        public DataQuery(ResolvedObject target, int top, string where = null, string orderByColumn = null, bool descending = false)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (top < 1 || top > MaxRowLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(top), "Row limit must be between 1 and " + MaxRowLimit + ".");
            }

            Top = top;
            Where = string.IsNullOrWhiteSpace(where) ? null : where.Trim();
            OrderByColumn = string.IsNullOrEmpty(orderByColumn) ? null : orderByColumn;
            Descending = descending;
        }

        public ResolvedObject Target { get; }
        public int Top { get; }
        public string Where { get; }
        public string OrderByColumn { get; }
        public bool Descending { get; }
    }

    /// <summary>Builds the read-only Data tab query. Identifiers are always bracket-quoted.</summary>
    public static class DataQueryBuilder
    {
        public const string TopParameterName = "@top";

        /// <summary>Parameterized command text: SELECT TOP (@top) ...</summary>
        public static string BuildCommandText(DataQuery q) => Build(q, TopParameterName);

        /// <summary>Executable text with a literal TOP, for "open as query" and for validation.</summary>
        public static string BuildDisplayText(DataQuery q) => Build(q, q.Top.ToString(CultureInfo.InvariantCulture)) + ";";

        /// <returns>null when the query is safe to run, otherwise the reason.</returns>
        public static string Validate(DataQuery q)
        {
            return q.Where == null ? null : ReadOnlyQueryValidator.Validate(Build(q, q.Top.ToString(CultureInfo.InvariantCulture)));
        }

        private static string Build(DataQuery q, string top)
        {
            var sb = new StringBuilder();
            sb.Append("SELECT TOP (").Append(top).Append(") *\r\nFROM ")
              .Append(ObjectName.QuoteIdentifier(q.Target.Database)).Append('.')
              .Append(ObjectName.QuoteIdentifier(q.Target.Schema)).Append('.')
              .Append(ObjectName.QuoteIdentifier(q.Target.Name));

            if (q.Where != null)
            {
                // Own lines so a trailing "-- comment" in the filter cannot swallow the closing parenthesis.
                sb.Append("\r\nWHERE (\r\n").Append(q.Where).Append("\r\n)");
            }

            if (q.OrderByColumn != null)
            {
                sb.Append("\r\nORDER BY ").Append(ObjectName.QuoteIdentifier(q.OrderByColumn)).Append(q.Descending ? " DESC" : " ASC");
            }

            return sb.ToString();
        }
    }
}
