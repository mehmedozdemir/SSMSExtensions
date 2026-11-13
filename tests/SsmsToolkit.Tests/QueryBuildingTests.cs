using System;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class QueryBuildingTests
    {
        private static readonly ResolvedObject Cards =
            new ResolvedObject(1, "FMS", "dbo", "Cards", InspectableObjectType.Table, null);

        [Theory]
        [InlineData("x", "[x]")]
        [InlineData("a]b", "[a]]b]")]
        [InlineData("with space", "[with space]")]
        [InlineData("", "[]")]
        public void QuoteIdentifier_escapes_closing_bracket(string input, string expected)
        {
            Assert.Equal(expected, ObjectName.QuoteIdentifier(input));
        }

        [Fact]
        public void ObjectName_quoting_keeps_known_parts()
        {
            Assert.Equal("[db].[dbo].[t]", new ObjectName(null, "db", "dbo", "t").ToQuotedString());
            Assert.Equal("[db]..[t]", new ObjectName(null, "db", null, "t").ToQuotedString());
            Assert.Equal("[dbo].[t]", new ObjectName(null, null, "dbo", "t").ToQuotedString());
            Assert.Equal("[t]", new ObjectName(null, null, null, "t").ToQuotedString());
        }

        [Fact]
        public void Data_query_is_parameterized_and_quoted()
        {
            var q = new DataQuery(new ResolvedObject(1, "F]MS", "dbo", "Ca]rds", InspectableObjectType.Table, null), 200, null, "Na]me", true);
            var text = DataQueryBuilder.BuildCommandText(q);
            Assert.StartsWith("SELECT TOP (@top) *", text);
            Assert.Contains("FROM [F]]MS].[dbo].[Ca]]rds]", text);
            Assert.Contains("ORDER BY [Na]]me] DESC", text);
            Assert.Null(DataQueryBuilder.Validate(q));
        }

        [Fact]
        public void Display_text_has_literal_top()
        {
            var q = new DataQuery(Cards, 50, "Id > 5");
            Assert.StartsWith("SELECT TOP (50) *", DataQueryBuilder.BuildDisplayText(q));
        }

        [Theory]
        [InlineData("Id > 5")]
        [InlineData("Name LIKE N'%a%' AND Id IN (SELECT CardId FROM dbo.Other)")]
        [InlineData("Id = 1 -- trailing comment")]
        [InlineData("/* c */ Id = 1")]
        public void Valid_filters(string where)
        {
            Assert.Null(DataQueryBuilder.Validate(new DataQuery(Cards, 200, where)));
        }

        [Theory]
        [InlineData("1=1; DROP TABLE dbo.Cards")]
        [InlineData("1=1) DELETE FROM dbo.Cards WHERE (1=1")]
        [InlineData("1=1)\nGO\nDELETE FROM dbo.Cards --")]
        [InlineData("Id IN (SELECT a FROM OPENQUERY(srv, 'DELETE x'))")]
        [InlineData("Id IN (SELECT a FROM OPENROWSET('SQLNCLI', 'x', 'SELECT 1'))")]
        [InlineData("Id IN (SELECT a FROM OPENDATASOURCE('SQLNCLI', 'x').db.dbo.t)")]
        [InlineData("Id IN (SELECT a FROM srv.db.dbo.t)")]
        [InlineData("Id = ")]
        [InlineData("1=1 /* unclosed")]
        public void Invalid_filters_are_rejected(string where)
        {
            Assert.NotNull(DataQueryBuilder.Validate(new DataQuery(Cards, 200, where)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(DataQuery.MaxRowLimit + 1)]
        public void Row_limit_is_bounded(int top)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DataQuery(Cards, top));
        }

        [Theory]
        [InlineData("nvarchar", "nvarchar", 100, 0, 0, false, "nvarchar(50)")]
        [InlineData("nvarchar", "nvarchar", -1, 0, 0, false, "nvarchar(max)")]
        [InlineData("varchar", "varchar", 20, 0, 0, false, "varchar(20)")]
        [InlineData("varbinary", "varbinary", -1, 0, 0, false, "varbinary(max)")]
        [InlineData("decimal", "decimal", 9, 18, 2, false, "decimal(18,2)")]
        [InlineData("datetime2", "datetime2", 8, 27, 7, false, "datetime2(7)")]
        [InlineData("int", "int", 4, 10, 0, false, "int")]
        [InlineData("float", "float", 8, 53, 0, false, "float")]
        [InlineData("Phone", "nvarchar", 40, 0, 0, true, "Phone")]
        public void Type_formatting(string type, string sys, int len, int prec, int scale, bool udt, string expected)
        {
            Assert.Equal(expected, SqlTypeFormatter.Format(type, sys, len, prec, scale, udt));
        }

        [Fact]
        public void Binary_preview_is_truncated()
        {
            Assert.Equal("0x0102", SqlTypeFormatter.FormatBinary(new byte[] { 1, 2 }));
            Assert.EndsWith("…", SqlTypeFormatter.FormatBinary(new byte[40]));
        }
    }
}
