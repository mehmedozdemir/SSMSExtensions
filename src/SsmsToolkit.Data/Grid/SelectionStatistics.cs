using System.Collections.Generic;
using System.Globalization;

namespace SsmsToolkit.Data.Grid
{
    /// <summary>Count / sum / average / min / max of selected grid cells (like a spreadsheet status bar).</summary>
    public sealed class SelectionStatistics
    {
        private SelectionStatistics()
        {
        }

        /// <summary>Selected cells (including NULLs).</summary>
        public long Cells { get; private set; }

        public long NonNull { get; private set; }

        public long Distinct { get; private set; }

        /// <summary>Cells that hold a number; Sum/Average/Min/Max are over these only.</summary>
        public long Numeric { get; private set; }

        public decimal? Sum { get; private set; }

        public decimal? Average { get; private set; }

        public decimal? Min { get; private set; }

        public decimal? Max { get; private set; }

        /// <summary>True when the selection had more cells than the limit and only the first ones were counted.</summary>
        public bool Truncated { get; private set; }

        /// <param name="cells">Selected values paired with their column's SQL type name.</param>
        public static SelectionStatistics Compute(IEnumerable<(object Value, string SqlTypeName)> cells, bool truncated = false)
        {
            var stats = new SelectionStatistics { Truncated = truncated };
            var distinct = new HashSet<string>();
            decimal sum = 0;
            bool overflow = false;

            foreach (var (value, type) in cells)
            {
                stats.Cells++;
                if (GridSelectionData.IsNull(value))
                {
                    continue;
                }

                stats.NonNull++;
                distinct.Add(SqlLiteral.FormatText(value, type));

                var number = SqlLiteral.ToDecimal(value, type);
                if (number == null)
                {
                    continue;
                }

                stats.Numeric++;
                stats.Min = stats.Min == null || number < stats.Min ? number : stats.Min;
                stats.Max = stats.Max == null || number > stats.Max ? number : stats.Max;
                if (!overflow)
                {
                    try
                    {
                        sum += number.Value;
                    }
                    catch (System.OverflowException)
                    {
                        overflow = true;
                    }
                }
            }

            stats.Distinct = distinct.Count;
            if (stats.Numeric > 0 && !overflow)
            {
                stats.Sum = sum;
                stats.Average = sum / stats.Numeric;
            }
            return stats;
        }

        /// <summary>Compact status bar text, e.g. "Count: 12 · Distinct: 10 · Sum: 1,234.5 · Avg: 102.88 · Min: 1 · Max: 500".</summary>
        public string ToStatusText(IStatisticsLabels labels, CultureInfo culture)
        {
            var parts = new List<string>
            {
                labels.Count + ": " + NonNull.ToString("N0", culture) + (NonNull != Cells ? " / " + Cells.ToString("N0", culture) : string.Empty),
                labels.Distinct + ": " + Distinct.ToString("N0", culture),
            };
            if (Numeric > 0)
            {
                if (Sum != null)
                {
                    parts.Add(labels.Sum + ": " + Number(Sum.Value, culture));
                    parts.Add(labels.Average + ": " + Number(Average.Value, culture));
                }
                parts.Add(labels.Min + ": " + Number(Min.Value, culture));
                parts.Add(labels.Max + ": " + Number(Max.Value, culture));
            }

            var text = string.Join("   ", parts);
            return Truncated ? text + "   " + labels.Truncated : text;
        }

        private static string Number(decimal value, CultureInfo culture)
        {
            // Up to 4 decimals, no trailing zeros, with group separators.
            var rounded = decimal.Round(value, 4);
            var text = rounded.ToString(CultureInfo.InvariantCulture);
            int dot = text.IndexOf('.');
            int decimals = dot < 0 ? 0 : text.Substring(dot + 1).TrimEnd('0').Length;
            return rounded.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), culture);
        }
    }

    /// <summary>Localized labels for <see cref="SelectionStatistics.ToStatusText"/>.</summary>
    public interface IStatisticsLabels
    {
        string Count { get; }

        string Distinct { get; }

        string Sum { get; }

        string Average { get; }

        string Min { get; }

        string Max { get; }

        string Truncated { get; }
    }
}
