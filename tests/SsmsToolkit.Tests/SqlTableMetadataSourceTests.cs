using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class SqlTableMetadataSourceTests : IClassFixture<LocalDbFixture>
    {
        private readonly LocalDbFixture _db;

        public SqlTableMetadataSourceTests(LocalDbFixture db)
        {
            _db = db;
        }

        private SqlTableMetadataSource Source(bool readUncommitted = false) =>
            new SqlTableMetadataSource(_db.Target, new MetadataOptions { CommandTimeoutSeconds = 15, ReadUncommitted = readUncommitted });

        private static ObjectName N(string schema, string name, string db = null) => new ObjectName(null, db, schema, name);

        private async Task<ResolvedObject> Resolve(string schema, string name)
        {
            var r = await Source().ResolveAsync(new[] { N(schema, name) }, CancellationToken.None);
            Assert.Equal(ResolveStatus.Found, r.Status);
            return r.Object;
        }

        [LocalDbFact]
        public async Task Resolves_table_with_default_schema()
        {
            var r = await Source().ResolveAsync(new[] { N(null, "Customer") }, CancellationToken.None);
            Assert.Equal(ResolveStatus.Found, r.Status);
            Assert.Equal("dbo", r.Object.Schema);
            Assert.Equal(InspectableObjectType.Table, r.Object.Type);
            Assert.Equal(_db.Database, r.Object.Database);
        }

        [LocalDbFact]
        public async Task Tries_candidates_in_order()
        {
            var r = await Source().ResolveAsync(new[] { N(null, "p"), N("sales", "Order") }, CancellationToken.None);
            Assert.Equal("Order", r.Object.Name);
        }

        [LocalDbFact]
        public async Task Resolves_three_part_name_via_its_database()
        {
            var r = await Source().ResolveAsync(new[] { N("sales", "Order", _db.Database) }, CancellationToken.None);
            Assert.Equal(ResolveStatus.Found, r.Status);
        }

        [LocalDbFact]
        public async Task Resolves_view_and_synonym()
        {
            var view = await Source().ResolveAsync(new[] { N("dbo", "vCustomer") }, CancellationToken.None);
            Assert.Equal(InspectableObjectType.View, view.Object.Type);

            var syn = await Source().ResolveAsync(new[] { N("dbo", "Cust") }, CancellationToken.None);
            Assert.Equal(ResolveStatus.Found, syn.Status);
            Assert.Equal("Customer", syn.Object.Name);
            Assert.Equal("Cust", syn.Object.Synonym.Name);
        }

        [LocalDbFact]
        public async Task Reports_non_table_objects_and_unsupported_names()
        {
            var proc = await Source().ResolveAsync(new[] { N("dbo", "DoNothing") }, CancellationToken.None);
            Assert.Equal(ResolveStatus.NotATableOrView, proc.Status);
            Assert.Equal("SQL_STORED_PROCEDURE", proc.Detail);

            var temp = await Source().ResolveAsync(new[] { N(null, "#tmp") }, CancellationToken.None);
            Assert.Equal(ResolveStatus.Unsupported, temp.Status);

            var missing = await Source().ResolveAsync(new[] { N("dbo", "Nope") }, CancellationToken.None);
            Assert.Equal(ResolveStatus.NotFound, missing.Status);
        }

        [LocalDbFact]
        public async Task Summary()
        {
            var s = await Source().GetSummaryAsync(await Resolve("dbo", "Customer"), CancellationToken.None);
            Assert.Equal(3, s.ApproximateRowCount);
            Assert.Equal(7, s.ColumnCount);
            Assert.Equal(2, s.IndexCount);
            Assert.Equal("Customers table", s.Description);
            Assert.Equal("USER_TABLE", s.TypeDescription);
        }

        [LocalDbFact]
        public async Task Columns()
        {
            var cols = await Source().GetColumnsAsync(await Resolve("dbo", "Customer"), CancellationToken.None);
            Assert.Equal(new[] { "Id", "Name", "Email", "Balance", "CreatedAt", "NameLength", "Photo" }, cols.Select(c => c.Name));

            var id = cols[0];
            Assert.True(id.IsPrimaryKey);
            Assert.True(id.IsIdentity);
            Assert.Equal("(1, 1)", id.IdentityDefinition);

            Assert.Equal("nvarchar(100)", cols[1].DataType);
            Assert.Equal("Display name", cols[1].Description);
            Assert.Equal("decimal(18,2)", cols[3].DataType);
            Assert.Equal("((0))", cols[3].DefaultDefinition);
            Assert.Equal("datetime2(3)", cols[4].DataType);
            Assert.True(cols[5].IsComputed);
            Assert.Equal("(len([Name]))", cols[5].ComputedDefinition);
            Assert.Equal("varbinary(max)", cols[6].DataType);
        }

        [LocalDbFact]
        public async Task Indexes()
        {
            var ix = await Source().GetIndexesAsync(await Resolve("sales", "Order"), CancellationToken.None);
            Assert.Equal("PK_Order", ix[0].Name);
            Assert.True(ix[0].IsPrimaryKey);

            var filtered = ix.Single(i => i.Name == "IX_Order_Customer");
            Assert.Equal(new[] { "CustomerId DESC" }, filtered.KeyColumns);
            Assert.Equal(new[] { "Total" }, filtered.IncludedColumns);
            Assert.Equal("([Total]>(0))", filtered.FilterDefinition);
        }

        [LocalDbFact]
        public async Task Indexes_are_marked_as_constraints_or_plain()
        {
            var ix = await Source().GetIndexesAsync(await Resolve("dbo", "Customer"), CancellationToken.None);
            Assert.Equal(new[] { "PK_Customer", "UQ_Customer_Email" }, ix.Where(i => i.IsConstraint).Select(i => i.Name));
            Assert.Equal("UQ", ix.Single(i => i.Name == "UQ_Customer_Email").Kind);
        }

        [LocalDbFact]
        public async Task Check_and_default_constraints()
        {
            var cs = await Source().GetConstraintsAsync(await Resolve("dbo", "Customer"), CancellationToken.None);

            var check = cs.Single(c => c.Kind == ConstraintKind.Check);
            Assert.Equal("CK_Customer_Balance", check.Name);
            Assert.Equal("Balance", check.Column);
            Assert.Equal("([Balance]>=(0))", check.Definition);
            Assert.False(check.IsNotTrusted);

            var defaults = cs.Where(c => c.Kind == ConstraintKind.Default).ToList();
            Assert.Equal(new[] { "DF_Customer_Balance", "DF_Customer_CreatedAt" }, defaults.Select(d => d.Name));
            Assert.Equal("((0))", defaults[0].Definition);

            var tableLevel = (await Source().GetConstraintsAsync(await Resolve("sales", "Order"), CancellationToken.None)).Single();
            Assert.Null(tableLevel.Column);
            Assert.True(tableLevel.IsNotTrusted);
        }

        [LocalDbFact]
        public async Task Foreign_keys_outgoing_incoming_and_self()
        {
            var fks = await Source().GetForeignKeysAsync(await Resolve("sales", "Order"), CancellationToken.None);

            var toCustomer = fks.Single(f => f.Name == "FK_Order_Customer");
            Assert.True(toCustomer.IsOutgoing);
            Assert.False(toCustomer.IsIncoming);
            Assert.Equal("Customer", toCustomer.ReferencedTable.Name);
            Assert.Equal("CASCADE", toCustomer.OnDelete);

            var self = fks.Single(f => f.Name == "FK_Order_Parent");
            Assert.True(self.IsOutgoing && self.IsIncoming);

            var incoming = await Source().GetForeignKeysAsync(await Resolve("dbo", "Customer"), CancellationToken.None);
            Assert.True(incoming.Single().IsIncoming);
        }

        [LocalDbFact]
        public async Task Data_with_limit_filter_and_order()
        {
            var obj = await Resolve("dbo", "Customer");
            var page = await Source().GetDataAsync(new DataQuery(obj, 2, "Balance > 0", "Name", descending: false), CancellationToken.None);

            Assert.Equal(2, page.Table.Rows.Count);
            Assert.Equal("Name", page.Table.Columns[1].Caption);
            Assert.Equal("Ada", page.Table.Rows[0][1]);
            Assert.Equal("Grace", page.Table.Rows[1][1]);
            Assert.Equal("0x0102", page.Table.Rows[0][6]);
            Assert.Equal("NULL", page.Table.Rows[1][6]);
            Assert.StartsWith("SELECT TOP (2) *", page.SqlText);
        }

        [LocalDbFact]
        public async Task Data_read_uncommitted_works()
        {
            var obj = await Resolve("dbo", "Customer");
            var page = await Source(readUncommitted: true).GetDataAsync(new DataQuery(obj, 200), CancellationToken.None);
            Assert.Equal(3, page.Table.Rows.Count);
        }

        [LocalDbFact]
        public async Task Data_rejects_unsafe_filter_before_touching_the_server()
        {
            var obj = await Resolve("dbo", "Customer");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Source().GetDataAsync(new DataQuery(obj, 10, "1=1; DELETE FROM dbo.Customer"), CancellationToken.None));

            var page = await Source().GetDataAsync(new DataQuery(obj, 200), CancellationToken.None);
            Assert.Equal(3, page.Table.Rows.Count);
        }

        [LocalDbFact]
        public async Task Table_ddl_via_smo()
        {
            if (!System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Microsoft.SqlServer.Smo.dll")))
            {
                return; // SMO not available on this machine (no SSMS): nothing to verify.
            }

            var ddl = await Source().GetDdlAsync(await Resolve("sales", "Order"), CancellationToken.None);
            Assert.Contains("CREATE TABLE [sales].[Order]", ddl);
            Assert.Contains("FK_Order_Customer", ddl);
            Assert.Contains("IX_Order_Customer", ddl);
            ScriptBuilderTests.AssertParses(ddl);
        }

        [LocalDbFact]
        public async Task View_ddl_is_the_module_definition()
        {
            var ddl = await Source().GetDdlAsync(await Resolve("dbo", "vCustomer"), CancellationToken.None);
            Assert.Contains("CREATE VIEW dbo.vCustomer", ddl);
        }

        [LocalDbFact]
        public async Task Cancellation_is_honoured()
        {
            var obj = await Resolve("dbo", "Customer");
            var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source().GetColumnsAsync(obj, cts.Token));
        }
    }
}
