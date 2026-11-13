using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Parsing;

namespace SsmsToolkit.Data.Grid
{
    /// <summary>Options of the "INSERT statements" generator.</summary>
    public sealed class InsertScriptOptions
    {
        /// <summary>SQL Server accepts at most 1000 row value expressions per INSERT ... VALUES.</summary>
        public const int MaxRowsPerStatement = 1000;

        /// <summary>Target table as typed by the user, e.g. "dbo.Orders" or "[sales].[Order Lines]".</summary>
        public string TableName { get; set; }

        /// <summary>1 = one INSERT per row; more = multi-row VALUES lists (capped at 1000).</summary>
        public int RowsPerStatement { get; set; } = 1;

        /// <summary>Leaves out identity, computed and rowversion columns.</summary>
        public bool ExcludeGeneratedColumns { get; set; } = true;

        /// <summary>Wraps the script in SET IDENTITY_INSERT ON/OFF when an identity column is included.</summary>
        public bool IdentityInsert { get; set; }

        /// <summary>Brackets around table and column names.</summary>
        public bool QuoteIdentifiers { get; set; } = true;
    }

    /// <summary>
    /// Builds INSERT statements row by row, so callers can report progress and cancel between rows.
    /// Call <see cref="Begin"/>, <see cref="AppendRow"/> for each row, then <see cref="End"/>.
    /// </summary>
    public sealed class InsertScriptBuilder
    {
        public const string PlaceholderTable = "[TableName]";

        private readonly IReadOnlyList<ResultColumn> _columns;
        private readonly InsertScriptOptions _options;
        private readonly int[] _included;
        private readonly string _header;
        private readonly int _rowsPerStatement;
        private int _rowsInStatement;

        public InsertScriptBuilder(IReadOnlyList<ResultColumn> columns, InsertScriptOptions options)
        {
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            _options = options ?? new InsertScriptOptions();
            _rowsPerStatement = Math.Max(1, Math.Min(InsertScriptOptions.MaxRowsPerStatement, _options.RowsPerStatement));

            _included = Enumerable.Range(0, columns.Count)
                .Where(i => !IsRowVersion(columns[i]) && !(_options.ExcludeGeneratedColumns && (columns[i].IsIdentity || columns[i].IsComputed)))
                .ToArray();

            Target = FormatTableName(_options.TableName, _options.QuoteIdentifiers);
            HasIdentityColumn = _included.Any(i => columns[i].IsIdentity);
            _header = "INSERT INTO " + Target + " ("
                      + string.Join(", ", _included.Select(i => Identifier(columns[i].Name))) + ")";
        }

        /// <summary>The formatted target table (or <see cref="PlaceholderTable"/>).</summary>
        public string Target { get; }

        public int ColumnCount => _included.Length;

        /// <summary>True when an included column is an identity column (inserting it needs IDENTITY_INSERT).</summary>
        public bool HasIdentityColumn { get; }

        /// <summary>Builds the whole script at once.</summary>
        public static string Build(IReadOnlyList<ResultColumn> columns, IEnumerable<object[]> rows, InsertScriptOptions options)
        {
            var builder = new InsertScriptBuilder(columns, options);
            var sb = new StringBuilder();
            builder.Begin(sb);
            foreach (var row in rows)
            {
                builder.AppendRow(sb, row);
            }
            builder.End(sb);
            return sb.ToString();
        }

        /// <summary>
        /// The source table reported by the result schema ("schema.table"), when every column comes from the same
        /// table; otherwise null.
        /// </summary>
        public static string DefaultTableName(IReadOnlyList<ResultColumn> columns)
        {
            var tables = columns.Select(c => (c.BaseSchema, c.BaseTable)).Distinct().ToList();
            return tables.Count == 1 && tables[0].BaseTable != null
                ? ObjectName.QuoteIdentifier(tables[0].BaseSchema ?? "dbo") + "." + ObjectName.QuoteIdentifier(tables[0].BaseTable)
                : null;
        }

        /// <summary>
        /// Parses "schema.table" / "[db].[schema].[table]" and re-quotes each part. Unparsable text is kept as typed;
        /// empty text gives <see cref="PlaceholderTable"/>.
        /// </summary>
        public static string FormatTableName(string text, bool quote = true)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return PlaceholderTable;
            }

            text = text.Trim();
            IEnumerable<string> parts;
            var name = SqlNameResolver.ParseMultipartName(text);
            if (name != null)
            {
                parts = new[] { name.Server, name.Database, name.Schema, name.Name }.SkipWhile(p => p == null);
            }
            else if (SimpleDottedName.IsMatch(text))
            {
                // Keywords such as "dbo.Order" do not tokenize as identifiers; plain dotted names are still names.
                parts = text.Split('.');
            }
            else
            {
                return text;
            }

            return string.Join(".", parts.Select(p => string.IsNullOrEmpty(p) ? string.Empty : quote ? ObjectName.QuoteIdentifier(p) : p));
        }

        /// <summary>"schema.table" for the table name box: plain parts when they are simple names, bracketed otherwise.</summary>
        public static string DisplayName(ObjectName name)
        {
            var parts = new[] { name.Server, name.Database, name.Schema, name.Name }.SkipWhile(p => p == null)
                .Select(p => p ?? string.Empty);
            return string.Join(".", parts.Select(p => p.Length == 0 || SimpleDottedName.IsMatch(p) && !p.Contains(".") ? p : ObjectName.QuoteIdentifier(p)));
        }

        private static readonly System.Text.RegularExpressions.Regex SimpleDottedName =
            new System.Text.RegularExpressions.Regex(@"^[\p{L}_#@][\p{L}\p{N}_#@$]*(\.[\p{L}_#@][\p{L}\p{N}_#@$]*){0,3}$");

        public void Begin(StringBuilder sb)
        {
            if (_options.IdentityInsert && HasIdentityColumn)
            {
                sb.Append("SET IDENTITY_INSERT ").Append(Target).Append(" ON;\r\n");
            }
            _rowsInStatement = 0;
        }

        public void AppendRow(StringBuilder sb, object[] row)
        {
            if (_included.Length == 0)
            {
                return;
            }

            var values = "(" + string.Join(", ", _included.Select(i => SqlLiteral.Format(row[i], _columns[i].SqlTypeName))) + ")";
            if (_rowsPerStatement == 1)
            {
                sb.Append(_header).Append(" VALUES ").Append(values).Append(";\r\n");
                return;
            }

            if (_rowsInStatement == _rowsPerStatement)
            {
                sb.Append(";\r\n");
                _rowsInStatement = 0;
            }
            sb.Append(_rowsInStatement == 0 ? _header + "\r\nVALUES\r\n    " : ",\r\n    ").Append(values);
            _rowsInStatement++;
        }

        public void End(StringBuilder sb)
        {
            if (_rowsPerStatement > 1 && _rowsInStatement > 0)
            {
                sb.Append(";\r\n");
            }
            _rowsInStatement = 0;
            if (_options.IdentityInsert && HasIdentityColumn)
            {
                sb.Append("SET IDENTITY_INSERT ").Append(Target).Append(" OFF;\r\n");
            }
        }

        private string Identifier(string name) => _options.QuoteIdentifiers ? ObjectName.QuoteIdentifier(name) : name;

        private static bool IsRowVersion(ResultColumn column) =>
            string.Equals(column.SqlTypeName, "timestamp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(column.SqlTypeName, "rowversion", StringComparison.OrdinalIgnoreCase);
    }
}
