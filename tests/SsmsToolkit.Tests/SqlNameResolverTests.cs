using System.Linq;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Parsing;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class SqlNameResolverTests
    {
        /// <summary>Resolves at the position marked with '|' (the marker is removed; the caret is on the next char).</summary>
        private static NameResolution At(string sqlWithCaret)
        {
            int offset = sqlWithCaret.IndexOf('|');
            Assert.True(offset >= 0, "caret marker missing");
            return SqlNameResolver.Resolve(sqlWithCaret.Remove(offset, 1), offset);
        }

        private static string First(NameResolution r) => r.Candidates.FirstOrDefault()?.ToString();

        [Theory]
        [InlineData("SELECT * FROM dbo.|Cards", "dbo.Cards")]
        [InlineData("SELECT * FROM |dbo.Cards", "dbo.Cards")]
        [InlineData("SELECT * FROM |Cards", "Cards")]
        [InlineData("SELECT * FROM [dbo].[My |Table] t", "dbo.My Table")]
        [InlineData("SELECT * FROM FMS.dbo.|Cards", "FMS.dbo.Cards")]
        [InlineData("SELECT * FROM FMS..|Cards", "FMS..Cards")]
        [InlineData("INSERT INTO dbo.|Cards (Id) VALUES (1)", "dbo.Cards")]
        [InlineData("SELECT * FROM a JOIN dbo.|B ON 1 = 1", "dbo.B")]
        [InlineData("EXEC dbo.|MyProc", "dbo.MyProc")]
        public void Plain_names(string sql, string expected)
        {
            var r = At(sql);
            Assert.True(r.UsedParser);
            Assert.Equal(expected, First(r));
        }

        [Theory]
        [InlineData("SELECT |p.Id FROM PersonelCardDetails p", "PersonelCardDetails")]
        [InlineData("SELECT p.|Id FROM PersonelCardDetails p", "PersonelCardDetails")]
        [InlineData("SELECT p.Id FROM PersonelCardDetails |p", "PersonelCardDetails")]
        [InlineData("SELECT p.Id FROM dbo.PersonelCardDetails AS |p", "dbo.PersonelCardDetails")]
        [InlineData("SELECT a.X, |b.Y FROM dbo.A a JOIN dbo.B b ON a.Id = b.AId", "dbo.B")]
        [InlineData("SELECT |Cards.Id FROM dbo.Cards", "dbo.Cards")]
        [InlineData("SELECT * FROM dbo.Cards c WHERE |c.Id = 1", "dbo.Cards")]
        public void Aliases(string sql, string expected)
        {
            Assert.Equal(expected, First(At(sql)));
        }

        [Fact]
        public void Alias_prefers_innermost_scope()
        {
            const string inner = "SELECT * FROM dbo.A x WHERE EXISTS (SELECT 1 FROM dbo.B x WHERE |x.Id = 1)";
            const string outer = "SELECT |x.Id FROM dbo.A x WHERE EXISTS (SELECT 1 FROM dbo.B x WHERE x.Id = 1)";
            Assert.Equal("dbo.B", First(At(inner)));
            Assert.Equal("dbo.A", First(At(outer)));
        }

        [Fact]
        public void Update_target_alias_resolves_to_table()
        {
            var r = At("UPDATE |p SET p.X = 1 FROM dbo.Cards p");
            Assert.Equal("dbo.Cards", First(r));
        }

        [Fact]
        public void Cte_name_is_not_a_table()
        {
            Assert.True(At("WITH c AS (SELECT 1 AS a) SELECT * FROM |c").IsEmpty);
        }

        [Fact]
        public void Derived_table_alias_is_not_a_table()
        {
            Assert.True(At("SELECT |d.a FROM (SELECT 1 AS a) d").IsEmpty);
        }

        [Theory]
        [InlineData("SELECT * FROM t WHERE x = '|Cards'")]
        [InlineData("-- |Cards\nSELECT 1")]
        [InlineData("SELECT * |FROM t")]
        [InlineData("SELECT 1 |+ 2")]
        public void Non_identifiers_resolve_to_nothing(string sql)
        {
            Assert.True(At(sql).IsEmpty);
        }

        [Fact]
        public void Broken_query_falls_back_to_tokens_with_alias()
        {
            var r = At("SELECT |p. FROM dbo.Cards p WHERE");
            Assert.False(r.UsedParser);
            Assert.Equal("dbo.Cards", First(r));
        }

        [Fact]
        public void Broken_query_plain_name()
        {
            var r = At("SELECT * FROM dbo.|Cards c JOIN");
            Assert.False(r.UsedParser);
            Assert.Equal("dbo.Cards", First(r));
        }

        [Fact]
        public void Broken_query_qualified_column_also_offers_table()
        {
            var r = At("SELECT dbo.Cards.|Id FROM");
            Assert.Contains(r.Candidates, c => c.ToString() == "dbo.Cards");
        }

        [Fact]
        public void Only_the_batch_with_the_caret_is_parsed()
        {
            var r = At("SELEC broken here\nGO\nSELECT * FROM dbo.|Cards\nGO\nalso broken (");
            Assert.True(r.UsedParser);
            Assert.Equal("dbo.Cards", First(r));
        }

        [Fact]
        public void Temp_table_is_reported_as_temporary()
        {
            var r = At("SELECT * FROM |#tmp");
            Assert.True(r.Candidates.Single().IsTemporary);
        }

        [Fact]
        public void Table_variable_is_reported()
        {
            var r = At("DECLARE @t TABLE (a int); SELECT * FROM |@t");
            Assert.True(r.Candidates.Single().IsTableVariable);
        }

        [Fact]
        public void Caret_at_end_or_empty_script_is_safe()
        {
            Assert.True(SqlNameResolver.Resolve("SELECT 1", 8).IsEmpty);
            Assert.True(SqlNameResolver.Resolve(string.Empty, 0).IsEmpty);
            Assert.True(SqlNameResolver.Resolve(null, 0).IsEmpty);
        }

        [Theory]
        [InlineData("[dbo].[Cards]", null, "dbo", "Cards")]
        [InlineData("[Other].[dbo].[T]", "Other", "dbo", "T")]
        [InlineData("dbo.T", null, "dbo", "T")]
        [InlineData("[a]]b].[c d]", null, "a]b", "c d")]
        public void ParseMultipartName_valid(string text, string db, string schema, string name)
        {
            var n = SqlNameResolver.ParseMultipartName(text);
            Assert.Equal(new ObjectName(null, db, schema, name), n);
        }

        [Theory]
        [InlineData("")]
        [InlineData("dbo.")]
        [InlineData("a b")]
        [InlineData("a.b.c.d.e")]
        public void ParseMultipartName_invalid(string text)
        {
            Assert.Null(SqlNameResolver.ParseMultipartName(text));
        }
    }
}
