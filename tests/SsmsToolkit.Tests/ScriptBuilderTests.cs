using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class ScriptBuilderTests
    {
        private static readonly ResolvedObject T = new ResolvedObject(1, "Db", "sales", "Ord]er", InspectableObjectType.Table, null);

        internal static void AssertParses(string sql)
        {
            using (var reader = new StringReader(sql))
            {
                new TSql170Parser(true).Parse(reader, out IList<ParseError> errors);
                Assert.True(errors.Count == 0, sql + "\n→ " + string.Join("; ", errors.Select(e => e.Message)));
            }
        }

        [Fact]
        public void Primary_key()
        {
            var pk = new IndexInfo { Name = "PK_Order", TypeDescription = "CLUSTERED", IsPrimaryKey = true, IsUnique = true };
            pk.KeyColumnDetails.Add(new IndexKeyColumn("Id", false));
            pk.KeyColumnDetails.Add(new IndexKeyColumn("Line", true));

            var sql = ScriptBuilder.Key(T, pk);
            Assert.Equal("ALTER TABLE [sales].[Ord]]er] ADD CONSTRAINT [PK_Order] PRIMARY KEY CLUSTERED ([Id] ASC, [Line] DESC);", sql);
            AssertParses(sql);
        }

        [Fact]
        public void Filtered_index_with_include_and_disabled()
        {
            var ix = new IndexInfo { Name = "IX_A", TypeDescription = "NONCLUSTERED", IsUnique = true, FilterDefinition = "([Total]>(0))", IsDisabled = true };
            ix.KeyColumnDetails.Add(new IndexKeyColumn("CustomerId", true));
            ix.IncludedColumns.Add("Total");

            var sql = ScriptBuilder.Index(T, ix);
            Assert.StartsWith("CREATE UNIQUE NONCLUSTERED INDEX [IX_A] ON [sales].[Ord]]er] ([CustomerId] DESC)", sql);
            Assert.Contains("INCLUDE ([Total])", sql);
            Assert.Contains("WHERE ([Total]>(0));", sql);
            Assert.EndsWith("ALTER INDEX [IX_A] ON [sales].[Ord]]er] DISABLE;", sql);
            AssertParses(sql);
        }

        [Fact]
        public void Columnstore_and_unknown_index_types()
        {
            var cci = new IndexInfo { Name = "CCI", TypeDescription = "CLUSTERED COLUMNSTORE" };
            AssertParses(ScriptBuilder.Index(T, cci));

            var xml = new IndexInfo { Name = "XI", TypeDescription = "XML" };
            Assert.StartsWith("--", ScriptBuilder.Index(T, xml));
        }

        [Fact]
        public void Check_default_and_foreign_key()
        {
            var check = ScriptBuilder.Constraint(T, new ConstraintInfo { Kind = ConstraintKind.Check, Name = "CK", Definition = "([Id]>(0))", IsNotTrusted = true, IsDisabled = true });
            Assert.Contains("WITH NOCHECK ADD CONSTRAINT [CK] CHECK ([Id]>(0));", check);
            Assert.Contains("NOCHECK CONSTRAINT [CK];", check);
            AssertParses(check);

            var def = ScriptBuilder.Constraint(T, new ConstraintInfo { Kind = ConstraintKind.Default, Name = "DF", Column = "Total", Definition = "((0))" });
            Assert.Equal("ALTER TABLE [sales].[Ord]]er] ADD CONSTRAINT [DF] DEFAULT ((0)) FOR [Total];", def);
            AssertParses(def);

            var fk = new ForeignKeyInfo
            {
                Name = "FK",
                ParentTable = new ObjectName(null, "Db", "sales", "Order"),
                ReferencedTable = new ObjectName(null, "Db", "dbo", "Customer"),
                OnDelete = "CASCADE",
                OnUpdate = "SET_NULL",
            };
            fk.ParentColumns.Add("CustomerId");
            fk.ReferencedColumns.Add("Id");
            var fkSql = ScriptBuilder.ForeignKey(fk);
            Assert.Contains("REFERENCES [dbo].[Customer] ([Id])", fkSql);
            Assert.Contains("ON DELETE CASCADE", fkSql);
            Assert.Contains("ON UPDATE SET NULL", fkSql);
            AssertParses(fkSql);
        }

        [Fact]
        public void Columns()
        {
            var id = ScriptBuilder.Column(T, new ColumnInfo { Name = "Id", DataType = "int", IsIdentity = true, IdentityDefinition = "(1, 1)" });
            Assert.Equal("ALTER TABLE [sales].[Ord]]er] ADD [Id] int IDENTITY(1,1) NOT NULL;", id);
            AssertParses(id);

            var computed = ScriptBuilder.Column(T, new ColumnInfo { Name = "L", IsComputed = true, ComputedDefinition = "(len([Name]))" });
            AssertParses(computed);

            var name = ScriptBuilder.Column(T, new ColumnInfo { Name = "Name", DataType = "nvarchar(100)", Collation = "Turkish_CI_AS", IsNullable = true, DefaultDefinition = "(N'x')" });
            Assert.Equal("ALTER TABLE [sales].[Ord]]er] ADD [Name] nvarchar(100) COLLATE Turkish_CI_AS NULL DEFAULT (N'x');", name);
            AssertParses(name);
        }
    }

    public class ScriptBuilderLocalDbTests : IClassFixture<LocalDbFixture>
    {
        private readonly LocalDbFixture _db;

        public ScriptBuilderLocalDbTests(LocalDbFixture db)
        {
            _db = db;
        }

        [LocalDbFact]
        public async Task Every_script_from_real_metadata_parses()
        {
            var source = new SqlTableMetadataSource(_db.Target);
            foreach (var name in new[] { new ObjectName(null, null, "dbo", "Customer"), new ObjectName(null, null, "sales", "Order") })
            {
                var t = (await source.ResolveAsync(new[] { name }, CancellationToken.None)).Object;
                var scripts = new List<string>();
                scripts.AddRange((await source.GetColumnsAsync(t, CancellationToken.None)).Select(c => ScriptBuilder.Column(t, c)));
                foreach (var i in await source.GetIndexesAsync(t, CancellationToken.None))
                {
                    scripts.Add(i.IsConstraint ? ScriptBuilder.Key(t, i) : ScriptBuilder.Index(t, i));
                }
                scripts.AddRange((await source.GetConstraintsAsync(t, CancellationToken.None)).Select(c => ScriptBuilder.Constraint(t, c)));
                scripts.AddRange((await source.GetForeignKeysAsync(t, CancellationToken.None)).Select(ScriptBuilder.ForeignKey));

                Assert.NotEmpty(scripts);
                foreach (var s in scripts)
                {
                    ScriptBuilderTests.AssertParses(s);
                }
            }
        }
    }
}
