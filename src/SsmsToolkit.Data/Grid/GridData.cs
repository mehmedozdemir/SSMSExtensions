using System;
using System.Collections.Generic;

namespace SsmsToolkit.Data.Grid
{
    /// <summary>A result column as reported by the grid's schema table.</summary>
    public sealed class ResultColumn
    {
        public ResultColumn(string name, string sqlTypeName, Type clrType = null, string baseSchema = null, string baseTable = null)
        {
            Name = name ?? string.Empty;
            SqlTypeName = (sqlTypeName ?? string.Empty).Trim();
            ClrType = clrType;
            BaseSchema = string.IsNullOrWhiteSpace(baseSchema) ? null : baseSchema;
            BaseTable = string.IsNullOrWhiteSpace(baseTable) ? null : baseTable;
        }

        public string Name { get; }

        /// <summary>Server type name without length, e.g. "nvarchar", "decimal", "datetime2".</summary>
        public string SqlTypeName { get; }

        public Type ClrType { get; }

        /// <summary>Source table, when the provider reports it (often null for ad-hoc queries).</summary>
        public string BaseSchema { get; }

        public string BaseTable { get; }
    }

    /// <summary>
    /// A rectangular copy of selected grid cells: the selected columns (in display order) and, for each selected row,
    /// one value per column. Values are what the grid stores: CLR values, <see cref="DBNull"/> or null for NULL.
    /// </summary>
    public sealed class GridSelectionData
    {
        public GridSelectionData(IReadOnlyList<ResultColumn> columns, IReadOnlyList<object[]> rows)
        {
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        public IReadOnlyList<ResultColumn> Columns { get; }

        public IReadOnlyList<object[]> Rows { get; }

        public static bool IsNull(object value) =>
            value == null || value is DBNull || (value is System.Data.SqlTypes.INullable n && n.IsNull);
    }
}
