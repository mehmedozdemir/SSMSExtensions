using System.Linq;
using SsmsToolkit.Data.Grid;
using SsmsToolkit.Data.Parsing;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class InsertScriptBuilderTests
    {
        private static readonly ResultColumn[] Columns =
        {
            new ResultColumn("Id", "int") { IsIdentity = true },
            new ResultColumn("Name", "nvarchar"),
            new ResultColumn("Total", "decimal") { IsComputed = true },
            new ResultColumn("RV", "timestamp"),
        };

        private static readonly object[][] Rows =
        {
            new object[] { 1, "a", 1m, new byte[] { 1 } },
            new object[] { 2, null, 2m, new byte[] { 2 } },
            new object[] { 3, "c", 3m, new byte[] { 3 } },
        };

        [Fact]
        public void One_statement_per_row_without_generated_columns_by_default()
        {
            var sql = InsertScriptBuilder.Build(Columns, Rows, new InsertScriptOptions { TableName = "dbo.T" });
            Assert.Equal(
                "INSERT INTO [dbo].[T] ([Name]) VALUES (N'a');\r\n" +
                "INSERT INTO [dbo].[T] ([Name]) VALUES (NULL);\r\n" +
                "INSERT INTO [dbo].[T] ([Name]) VALUES (N'c');\r\n",
                sql);
        }

        [Fact]
        public void Multi_row_statements_are_split()
        {
            var sql = InsertScriptBuilder.Build(Columns, Rows, new InsertScriptOptions { TableName = "T", RowsPerStatement = 2 });
            Assert.Equal(
                "INSERT INTO [T] ([Name])\r\nVALUES\r\n    (N'a'),\r\n    (NULL);\r\n" +
                "INSERT INTO [T] ([Name])\r\nVALUES\r\n    (N'c');\r\n",
                sql);
        }

        [Fact]
        public void Identity_insert_wraps_the_script_when_identity_is_included()
        {
            var options = new InsertScriptOptions { TableName = "T", ExcludeGeneratedColumns = false, IdentityInsert = true };
            var sql = InsertScriptBuilder.Build(Columns, Rows.Take(1), options);
            Assert.Equal(
                "SET IDENTITY_INSERT [T] ON;\r\n" +
                "INSERT INTO [T] ([Id], [Name], [Total]) VALUES (1, N'a', 1);\r\n" +
                "SET IDENTITY_INSERT [T] OFF;\r\n",
                sql);
        }

        [Fact]
        public void Identity_insert_is_not_written_when_identity_is_excluded()
        {
            var sql = InsertScriptBuilder.Build(Columns, Rows.Take(1), new InsertScriptOptions { TableName = "T", IdentityInsert = true });
            Assert.DoesNotContain("IDENTITY_INSERT", sql);
        }

        [Fact]
        public void Unquoted_names()
        {
            var sql = InsertScriptBuilder.Build(Columns, Rows.Take(1), new InsertScriptOptions { TableName = "[dbo].[T]", QuoteIdentifiers = false });
            Assert.Equal("INSERT INTO dbo.T (Name) VALUES (N'a');\r\n", sql);
        }

        [Theory]
        [InlineData("dbo.Orders", "[dbo].[Orders]")]
        [InlineData("[sales].[Order Lines]", "[sales].[Order Lines]")]
        [InlineData("Db..T", "Db..T")]
        [InlineData("  T  ", "[T]")]
        [InlineData("dbo.Order", "[dbo].[Order]")]
        [InlineData("", "[TableName]")]
        [InlineData("not a name!", "not a name!")]
        public void Table_name_formatting(string text, string expected) =>
            Assert.Equal(expected, InsertScriptBuilder.FormatTableName(text));

        [Fact]
        public void Default_table_from_schema()
        {
            Assert.Equal("[sales].[Order]", InsertScriptBuilder.DefaultTableName(new[] { new ResultColumn("A", "int", null, "sales", "Order") }));
            Assert.Null(InsertScriptBuilder.DefaultTableName(new[]
            {
                new ResultColumn("A", "int", null, "dbo", "X"), new ResultColumn("B", "int", null, "dbo", "Y"),
            }));
        }

        [Fact]
        public void Select_sources_in_order_without_ctes_and_temp_tables()
        {
            var script = "WITH c AS (SELECT * FROM dbo.A) SELECT * FROM c JOIN B b ON 1 = 1;\r\nGO\r\n"
                       + "UPDATE dbo.Z SET x = 1;\r\nSELECT * FROM #tmp; SELECT * FROM [sales].[Order] o;";
            var names = SqlNameResolver.FindSelectSources(script).Select(n => n.ToString()).ToList();
            Assert.Equal(new[] { "dbo.A", "B", "sales.Order" }, names);
        }

        [Fact]
        public void Select_sources_of_broken_script_is_empty() =>
            Assert.Empty(SqlNameResolver.FindSelectSources("SELECT * FROM"));
    }
}
