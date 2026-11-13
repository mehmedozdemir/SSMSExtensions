using System;
using System.Globalization;
using System.Linq;
using SsmsToolkit.Data.Grid;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class GridFormattingTests
    {
        private static GridSelectionData Sample(string table = null) => new GridSelectionData(
            new[]
            {
                new ResultColumn("Id", "int", typeof(int), "dbo", table),
                new ResultColumn("Name", "nvarchar", typeof(string), "dbo", table),
                new ResultColumn("Price", "decimal", typeof(decimal), "dbo", table),
                new ResultColumn("Created", "datetime2", typeof(DateTime), "dbo", table),
                new ResultColumn("Active", "bit", typeof(bool), "dbo", table),
            },
            new[]
            {
                new object[] { 1, "O'Brien, \"Bob\"", 12.50m, new DateTime(2026, 1, 2, 3, 4, 5), true },
                new object[] { 2, null, DBNull.Value, new DateTime(2026, 1, 2, 3, 4, 5, 123), false },
            });

        [Theory]
        [InlineData(null, "int", "NULL")]
        [InlineData(42, "int", "42")]
        [InlineData(-1.5, "float", "-1.5")]
        [InlineData("abc", "varchar", "'abc'")]
        [InlineData("it's", "nvarchar", "N'it''s'")]
        [InlineData(true, "bit", "1")]
        [InlineData("0", "bit", "0")]
        [InlineData("12.30", "decimal", "12.30")]
        [InlineData("x", "sql_variant", "N'x'")]
        public void Literals(object value, string type, string expected) =>
            Assert.Equal(expected, SqlLiteral.Format(value, type));

        [Fact]
        public void Literal_special_types()
        {
            Assert.Equal("12.50", SqlLiteral.Format(12.50m, "money"));
            Assert.Equal("'2026-01-02'", SqlLiteral.Format(new DateTime(2026, 1, 2), "date"));
            Assert.Equal("'2026-01-02T03:04:05.000'", SqlLiteral.Format(new DateTime(2026, 1, 2, 3, 4, 5), "datetime"));
            Assert.Equal("'2026-01-02T03:04:05.1234567'", SqlLiteral.Format(new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234567), "datetime2"));
            Assert.Equal("'2026-01-02T03:04:05+03:00'", SqlLiteral.Format(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)), "datetimeoffset"));
            Assert.Equal("'13:45:00'", SqlLiteral.Format(new TimeSpan(13, 45, 0), "time"));
            Assert.Equal("0x00FF10", SqlLiteral.Format(new byte[] { 0, 255, 16 }, "varbinary"));
            Assert.Equal("'6F9619FF-8B86-D011-B42D-00C04FC964FF'", SqlLiteral.Format(new Guid("6F9619FF-8B86-D011-B42D-00C04FC964FF"), "uniqueidentifier"));
            Assert.Equal("NULL", SqlLiteral.Format(System.Data.SqlTypes.SqlInt32.Null, "int"));
            Assert.Equal("7", SqlLiteral.Format(new System.Data.SqlTypes.SqlInt32(7), "int"));
        }

        [Fact]
        public void Literals_do_not_depend_on_current_culture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                Assert.Equal("1234.5", SqlLiteral.Format(1234.5m, "decimal"));
                Assert.Equal("1234.5", SqlLiteral.Format(1234.5, "float"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Csv_quotes_when_needed_and_leaves_null_empty()
        {
            var csv = GridSelectionFormatter.ToCsv(Sample());
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.Equal("Id,Name,Price,Created,Active", lines[0]);
            Assert.Equal("1,\"O'Brien, \"\"Bob\"\"\",12.50,2026-01-02T03:04:05,1", lines[1]);
            Assert.Equal("2,,,2026-01-02T03:04:05.123,0", lines[2]);
        }

        [Fact]
        public void Json_uses_native_numbers_booleans_and_nulls()
        {
            var json = GridSelectionFormatter.ToJson(Sample());
            Assert.Contains("{\"Id\": 1, \"Name\": \"O'Brien, \\\"Bob\\\"\", \"Price\": 12.50, \"Created\": \"2026-01-02T03:04:05\", \"Active\": true}", json);
            Assert.Contains("{\"Id\": 2, \"Name\": null, \"Price\": null, \"Created\": \"2026-01-02T03:04:05.123\", \"Active\": false}", json);
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        [Fact]
        public void Json_makes_duplicate_and_empty_names_unique()
        {
            var data = new GridSelectionData(
                new[] { new ResultColumn("Id", "int"), new ResultColumn("Id", "int"), new ResultColumn("", "int") },
                new[] { new object[] { 1, 2, 3 } });
            Assert.Contains("{\"Id\": 1, \"Id_2\": 2, \"Column3\": 3}", GridSelectionFormatter.ToJson(data));
        }

        [Fact]
        public void Markdown_escapes_pipes_and_newlines_and_right_aligns_numbers()
        {
            var data = new GridSelectionData(
                new[] { new ResultColumn("Text", "nvarchar"), new ResultColumn("N", "int") },
                new[] { new object[] { "a|b\r\nc", null } });
            var md = GridSelectionFormatter.ToMarkdown(data).Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.Equal("| Text | N |", md[0]);
            Assert.Equal("| --- | ---: |", md[1]);
            Assert.Equal("| a\\|b<br>c | NULL |", md[2]);
        }

        [Fact]
        public void Insert_uses_placeholder_table_without_schema_info()
        {
            var sql = GridSelectionFormatter.ToInsert(Sample());
            Assert.StartsWith("INSERT INTO [TableName] ([Id], [Name], [Price], [Created], [Active])\r\nVALUES\r\n", sql);
            Assert.Contains("    (1, N'O''Brien, \"Bob\"', 12.50, '2026-01-02T03:04:05', 1),\r\n", sql);
            Assert.Contains("    (2, NULL, NULL, '2026-01-02T03:04:05.123', 0);\r\n", sql);
        }

        [Fact]
        public void Insert_uses_base_table_and_skips_rowversion()
        {
            var data = new GridSelectionData(
                new[] { new ResultColumn("Id", "int", null, "sales", "Order"), new ResultColumn("RV", "timestamp", null, "sales", "Order") },
                new[] { new object[] { 1, new byte[] { 1 } } });
            Assert.StartsWith("INSERT INTO [sales].[Order] ([Id])\r\nVALUES\r\n    (1);", GridSelectionFormatter.ToInsert(data));
        }

        [Fact]
        public void Insert_splits_every_1000_rows()
        {
            var rows = Enumerable.Range(1, 2500).Select(i => new object[] { i }).ToArray();
            var sql = GridSelectionFormatter.ToInsert(new GridSelectionData(new[] { new ResultColumn("Id", "int") }, rows));
            Assert.Equal(3, sql.Split(new[] { "INSERT INTO" }, StringSplitOptions.None).Length - 1);
        }

        [Fact]
        public void InList_is_distinct_and_skips_nulls()
        {
            var data = new GridSelectionData(
                new[] { new ResultColumn("Code", "varchar") },
                new[] { new object[] { "A" }, new object[] { null }, new object[] { "B" }, new object[] { "A" } });
            Assert.Equal("IN ('A', 'B')", GridSelectionFormatter.ToInList(data));
        }

        [Fact]
        public void InList_wraps_long_lists()
        {
            var rows = Enumerable.Range(1, 25).Select(i => new object[] { i }).ToArray();
            var text = GridSelectionFormatter.ToInList(new GridSelectionData(new[] { new ResultColumn("Id", "int") }, rows));
            Assert.StartsWith("IN (\r\n    1, 2, 3, 4, 5, 6, 7, 8, 9, 10,\r\n", text);
            Assert.EndsWith("    21, 22, 23, 24, 25\r\n)", text);
        }

        [Fact]
        public void Statistics_over_numbers_text_and_nulls()
        {
            var stats = SelectionStatistics.Compute(new (object, string)[]
            {
                (10, "int"), (20.5m, "decimal"), (null, "int"), ("x", "nvarchar"), ("30", "int"), (10, "int"),
            });

            Assert.Equal(6, stats.Cells);
            Assert.Equal(5, stats.NonNull);
            Assert.Equal(4, stats.Distinct);
            Assert.Equal(4, stats.Numeric);
            Assert.Equal(70.5m, stats.Sum);
            Assert.Equal(17.625m, stats.Average);
            Assert.Equal(10m, stats.Min);
            Assert.Equal(30m, stats.Max);
        }

        [Fact]
        public void Statistics_text()
        {
            var stats = SelectionStatistics.Compute(new (object, string)[] { (1, "int"), (2, "int"), (null, "int") }, truncated: true);
            var text = stats.ToStatusText(new Labels(), CultureInfo.InvariantCulture);
            Assert.Equal("Count: 2 / 3   Distinct: 2   Sum: 3   Avg: 1.5   Min: 1   Max: 2   (partial)", text);
        }

        [Fact]
        public void Statistics_without_numbers_only_counts()
        {
            var stats = SelectionStatistics.Compute(new (object, string)[] { ("a", "nvarchar"), ("b", "nvarchar") });
            Assert.Equal("Count: 2   Distinct: 2", stats.ToStatusText(new Labels(), CultureInfo.InvariantCulture));
        }

        private sealed class Labels : IStatisticsLabels
        {
            public string Count => "Count";

            public string Distinct => "Distinct";

            public string Sum => "Sum";

            public string Average => "Avg";

            public string Min => "Min";

            public string Max => "Max";

            public string Truncated => "(partial)";
        }
    }
}
