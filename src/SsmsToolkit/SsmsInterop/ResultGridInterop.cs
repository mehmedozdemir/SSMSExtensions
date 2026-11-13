using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.SqlServer.Management.QueryExecution;
using Microsoft.SqlServer.Management.UI.Grid;
using SsmsToolkit.Data.Grid;
using SsmsToolkit.Presentation.ResultGrid;

namespace SsmsToolkit.SsmsInterop
{
    /// <summary>
    /// Reads the SSMS results grid (Microsoft.SqlServer.GridControl). Only public members of public SSMS types are used:
    /// the focused WinForms control is walked up to a <see cref="GridControl"/> whose storage is SSMS's result set
    /// (<see cref="IGridResultSet"/>). <see cref="GridControl.SelectedCells"/> returns storage column indexes; in grid
    /// mode storage column 0 is the row-number column. Everything here runs on the UI thread.
    /// </summary>
    internal static class ResultGridInterop
    {
        /// <summary>The results grid that has keyboard focus, or null.</summary>
        public static GridControl GetFocusedResultsGrid()
        {
            var handle = GetFocus();
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            for (var control = Control.FromChildHandle(handle); control != null; control = control.Parent)
            {
                if (control is GridControl grid)
                {
                    return IsResultsGrid(grid) ? grid : null;
                }
            }
            return null;
        }

        public static bool IsResultsGrid(GridControl grid) =>
            grid != null && !grid.IsDisposed && grid.GridStorage is IGridResultSet;

        /// <summary>Number of selected cells (data columns only), counting overlapping blocks once per block.</summary>
        public static long CountSelectedCells(GridControl grid)
        {
            var layout = Layout.For(grid);
            return layout == null ? 0 : layout.Blocks.Sum(b => (long)b.Columns.Count * b.RowCount);
        }

        /// <summary>
        /// The selection as a rectangle: every selected column (display order) × every selected row (ascending).
        /// For a single block this is exactly the selection. Returns null when nothing is selected.
        /// </summary>
        public static GridSelectionData ReadSelection(GridControl grid)
        {
            var layout = Layout.For(grid);
            if (layout == null || layout.Blocks.Count == 0)
            {
                return null;
            }

            var storageColumns = layout.Blocks.SelectMany(b => b.Columns).Distinct()
                .OrderBy(c => SafeUiIndex(grid, c)).ToList();
            var columns = storageColumns.Select(layout.Column).ToList();

            var rows = new List<object[]>();
            foreach (long row in MergedRows(layout.Blocks))
            {
                var values = new object[storageColumns.Count];
                for (int i = 0; i < storageColumns.Count; i++)
                {
                    values[i] = layout.ResultSet.GetCellData(row, storageColumns[i]);
                }
                rows.Add(values);
            }
            return new GridSelectionData(columns, rows);
        }

        /// <summary>
        /// Lazy row source over the selection (selected columns × selected rows), or null when nothing is selected.
        /// Rows are read from the grid storage only when asked for, so large results are not copied up front.
        /// </summary>
        public static IGridRowSource CreateSelectionSource(GridControl grid)
        {
            var layout = Layout.For(grid);
            if (layout == null || layout.Blocks.Count == 0)
            {
                return null;
            }

            var storageColumns = layout.Blocks.SelectMany(b => b.Columns).Distinct().OrderBy(c => SafeUiIndex(grid, c)).ToArray();
            return new RowSource(layout, storageColumns, MergedRows(layout.Blocks).ToArray());
        }

        /// <summary>Lazy row source over the whole result set (all data columns in display order).</summary>
        public static IGridRowSource CreateAllRowsSource(GridControl grid)
        {
            var layout = Layout.For(grid);
            if (layout == null)
            {
                return null;
            }

            var storageColumns = layout.AllStorageColumns().OrderBy(c => SafeUiIndex(grid, c)).ToArray();
            return new RowSource(layout, storageColumns, null);
        }

        private sealed class RowSource : IGridRowSource
        {
            private readonly Layout _layout;
            private readonly int[] _storageColumns;
            private readonly long[] _rows;

            public RowSource(Layout layout, int[] storageColumns, long[] rows)
            {
                _layout = layout;
                _storageColumns = storageColumns;
                _rows = rows;
                Columns = storageColumns.Select(layout.Column).ToList();
                RowCount = rows?.LongLength ?? layout.ResultSet.TotalNumberOfRows;
            }

            public IReadOnlyList<ResultColumn> Columns { get; }

            public long RowCount { get; }

            public object[] ReadRow(long index)
            {
                long row = _rows == null ? index : _rows[index];
                var values = new object[_storageColumns.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = _layout.ResultSet.GetCellData(row, _storageColumns[i]);
                }
                return values;
            }
        }

        /// <summary>Statistics over the selected cells; stops after <paramref name="maxCells"/> cells.</summary>
        public static SelectionStatistics ReadStatistics(GridControl grid, int maxCells)
        {
            var layout = Layout.For(grid);
            if (layout == null || layout.Blocks.Count == 0)
            {
                return null;
            }

            var cells = new List<(object, string)>();
            foreach (var block in layout.Blocks)
            {
                for (long row = block.Top; row <= block.Bottom; row++)
                {
                    foreach (var col in block.Columns)
                    {
                        if (cells.Count >= maxCells)
                        {
                            return SelectionStatistics.Compute(cells, truncated: true);
                        }
                        cells.Add((layout.ResultSet.GetCellData(row, col), layout.Column(col).SqlTypeName));
                    }
                }
            }
            return SelectionStatistics.Compute(cells);
        }

        private static IEnumerable<long> MergedRows(IEnumerable<Block> blocks)
        {
            long next = long.MinValue;
            foreach (var block in blocks.OrderBy(b => b.Top))
            {
                for (long row = Math.Max(block.Top, next); row <= block.Bottom; row++)
                {
                    yield return row;
                }
                next = Math.Max(next, block.Bottom + 1);
            }
        }

        private static int SafeUiIndex(GridControl grid, int storageColumn)
        {
            try
            {
                return grid.GetUIColumnIndexByStorageIndex(storageColumn);
            }
            catch (Exception)
            {
                return storageColumn;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        private sealed class Block
        {
            public long Top;
            public long Bottom;
            public List<int> Columns;

            public long RowCount => Bottom - Top + 1;
        }

        /// <summary>Selected blocks clipped to data rows/columns, plus column metadata.</summary>
        private sealed class Layout
        {
            private readonly Dictionary<int, ResultColumn> _columns = new Dictionary<int, ResultColumn>();
            private int _offset;

            public IGridResultSet ResultSet { get; private set; }

            public List<Block> Blocks { get; } = new List<Block>();

            public static Layout For(GridControl grid)
            {
                if (!IsResultsGrid(grid))
                {
                    return null;
                }

                var rs = (IGridResultSet)grid.GridStorage;
                int dataColumns = rs.NumberOfDataColumns;
                long rowCount = rs.TotalNumberOfRows;
                // Grid mode adds the row-number column at storage index 0.
                var layout = new Layout { ResultSet = rs, _offset = Math.Max(0, Math.Min(1, grid.ColumnsNumber - dataColumns)) };

                foreach (BlockOfCells cells in grid.SelectedCells)
                {
                    if (cells == null || cells.IsEmpty)
                    {
                        continue;
                    }

                    var columns = Enumerable.Range(cells.X, cells.Right - cells.X + 1)
                        .Where(c => c - layout._offset >= 0 && c - layout._offset < dataColumns)
                        .ToList();
                    long top = Math.Max(0, cells.Y);
                    long bottom = Math.Min(rowCount - 1, cells.Bottom);
                    if (columns.Count > 0 && bottom >= top)
                    {
                        layout.Blocks.Add(new Block { Top = top, Bottom = bottom, Columns = columns });
                    }
                }
                return layout;
            }

            public ResultColumn Column(int storageColumn)
            {
                if (_columns.TryGetValue(storageColumn, out var column))
                {
                    return column;
                }

                int data = storageColumn - _offset;
                string name = data < ResultSet.ColumnNames.Count ? ResultSet.ColumnNames[data] : null;
                DataRow schema = null;
                try
                {
                    schema = ResultSet.GetSchemaRow(data);
                }
                catch (Exception)
                {
                    // Schema is optional; values are still formatted from their CLR types.
                }

                column = new ResultColumn(
                    name,
                    Text(schema, "DataTypeName"),
                    Value(schema, "DataType") as Type,
                    Text(schema, "BaseSchemaName"),
                    Text(schema, "BaseTableName"))
                {
                    IsIdentity = Flag(schema, "IsIdentity") || Flag(schema, "IsAutoIncrement"),
                    IsComputed = Flag(schema, "IsExpression"),
                };
                _columns[storageColumn] = column;
                return column;
            }

            private static object Value(DataRow row, string column) =>
                row != null && row.Table.Columns.Contains(column) && !(row[column] is DBNull) ? row[column] : null;

            private static string Text(DataRow row, string column) => Value(row, column) as string;

            private static bool Flag(DataRow row, string column) => Value(row, column) is bool b && b;

            /// <summary>Every data column's storage index.</summary>
            public IEnumerable<int> AllStorageColumns() => Enumerable.Range(_offset, ResultSet.NumberOfDataColumns);
        }
    }
}
