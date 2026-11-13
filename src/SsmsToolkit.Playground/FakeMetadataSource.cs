using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;

namespace SsmsToolkit.Playground
{
    /// <summary>In-memory sample data with artificial latency, to see loading states without a server.</summary>
    internal sealed class FakeMetadataSource : ITableMetadataSource
    {
        private readonly int _delayMs;

        public FakeMetadataSource(int delayMs = 400)
        {
            _delayMs = delayMs;
        }

        public string ServerName => "sql-prod-01";

        public string Database => "SampleDb";

        public async Task<ResolveResult> ResolveAsync(IReadOnlyList<ObjectName> candidates, CancellationToken ct)
        {
            await Task.Delay(_delayMs / 4, ct);
            var n = candidates[0];
            if (n.IsTemporary || n.IsTableVariable)
            {
                return ResolveResult.Unsupported("temp-table");
            }
            return ResolveResult.Found(new ResolvedObject(42, n.Database ?? Database, n.Schema ?? "dbo", n.Name, InspectableObjectType.Table, null));
        }

        public async Task<TableSummary> GetSummaryAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return new TableSummary
            {
                Schema = obj.Schema,
                Name = obj.Name,
                TypeDescription = "USER_TABLE",
                Created = new DateTime(2023, 3, 14, 9, 26, 53),
                Modified = new DateTime(2026, 10, 2, 17, 5, 0),
                ApproximateRowCount = 1284551,
                TotalSizeKb = 412_736,
                UsedSizeKb = 398_112,
                ColumnCount = 8,
                IndexCount = 3,
                Description = "Personnel card assignments",
            };
        }

        public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return new[]
            {
                new ColumnInfo { Ordinal = 1, Name = "Id", DataType = "int", IsPrimaryKey = true, IsIdentity = true, IdentityDefinition = "(1, 1)" },
                new ColumnInfo { Ordinal = 2, Name = "PersonelId", DataType = "int", Description = "FK to Personel" },
                new ColumnInfo { Ordinal = 3, Name = "CardId", DataType = "int" },
                new ColumnInfo { Ordinal = 4, Name = "CardNumber", DataType = "varchar(32)", IsNullable = true, Collation = "Turkish_CI_AS" },
                new ColumnInfo { Ordinal = 5, Name = "IsActive", DataType = "bit", DefaultDefinition = "((1))" },
                new ColumnInfo { Ordinal = 6, Name = "ValidFrom", DataType = "datetime2(3)", DefaultDefinition = "(sysutcdatetime())" },
                new ColumnInfo { Ordinal = 7, Name = "ValidTo", DataType = "datetime2(3)", IsNullable = true },
                new ColumnInfo { Ordinal = 8, Name = "IsExpired", DataType = "int", IsNullable = true, IsComputed = true, ComputedDefinition = "(case when [ValidTo]<getdate() then (1) else (0) end)" },
            };
        }

        public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            var pk = new IndexInfo { IndexId = 1, Name = "PK_" + obj.Name, TypeDescription = "CLUSTERED", IsPrimaryKey = true, IsUnique = true };
            pk.KeyColumnDetails.Add(new IndexKeyColumn("Id", false));
            var ix = new IndexInfo { IndexId = 2, Name = "IX_" + obj.Name + "_Personel", TypeDescription = "NONCLUSTERED" };
            ix.KeyColumnDetails.Add(new IndexKeyColumn("PersonelId", false));
            ix.KeyColumnDetails.Add(new IndexKeyColumn("ValidFrom", true));
            ix.IncludedColumns.Add("CardId");
            var uq = new IndexInfo { IndexId = 3, Name = "UQ_CardNumber", TypeDescription = "NONCLUSTERED", IsUnique = true, IsUniqueConstraint = true, FilterDefinition = "([CardNumber] IS NOT NULL)" };
            uq.KeyColumnDetails.Add(new IndexKeyColumn("CardNumber", false));
            return new[] { pk, ix, uq };
        }

        public async Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return new[]
            {
                new ConstraintInfo { Kind = ConstraintKind.Check, Name = "CK_" + obj.Name + "_Validity", Definition = "([ValidTo] IS NULL OR [ValidTo]>[ValidFrom])" },
                new ConstraintInfo { Kind = ConstraintKind.Check, Name = "CK_" + obj.Name + "_CardNumber", Column = "CardNumber", Definition = "(len([CardNumber])>=(8))", IsNotTrusted = true },
                new ConstraintInfo { Kind = ConstraintKind.Default, Name = "DF_" + obj.Name + "_IsActive", Column = "IsActive", Definition = "((1))" },
                new ConstraintInfo { Kind = ConstraintKind.Default, Name = "DF__Personel__Valid__4A8310C6", Column = "ValidFrom", Definition = "(sysutcdatetime())", IsSystemNamed = true },
            };
        }

        public async Task<IReadOnlyList<TriggerInfo>> GetTriggersAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return new[]
            {
                new TriggerInfo
                {
                    Name = "trg_" + obj.Name + "_Audit",
                    Events = "INSERT, UPDATE",
                    Definition = "CREATE TRIGGER [" + obj.Schema + "].[trg_" + obj.Name + "_Audit] ON [" + obj.Schema + "].[" + obj.Name + "]\r\nAFTER INSERT, UPDATE\r\nAS\r\nBEGIN\r\n    SET NOCOUNT ON;\r\n    INSERT INTO log.CardLog (DetailId, ChangedAt)\r\n    SELECT i.Id, SYSUTCDATETIME() FROM inserted i;\r\nEND",
                },
                new TriggerInfo { Name = "trg_" + obj.Name + "_NoDelete", Events = "DELETE", IsInsteadOf = true, IsDisabled = true },
            };
        }

        public async Task<IReadOnlyList<DependencyInfo>> GetDependenciesAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return new[]
            {
                new DependencyInfo { Direction = DependencyDirection.UsedBy, Name = new ObjectName(null, Database, "dbo", "usp_GetActiveCards"), TypeCode = "P", TypeDescription = "SQL_STORED_PROCEDURE", Definition = "CREATE PROCEDURE dbo.usp_GetActiveCards\r\nAS\r\nSELECT * FROM PersonelCardDetails WHERE IsActive = 1;" },
                new DependencyInfo { Direction = DependencyDirection.UsedBy, Name = new ObjectName(null, Database, "rpt", "vCardSummary"), TypeCode = "V", TypeDescription = "VIEW", Definition = "CREATE VIEW rpt.vCardSummary AS SELECT PersonelId, COUNT(*) AS Cards FROM dbo.PersonelCardDetails GROUP BY PersonelId;" },
                new DependencyInfo { Direction = DependencyDirection.UsedBy, Name = new ObjectName(null, Database, "dbo", "fn_CardCount"), TypeCode = "FN", TypeDescription = "SQL_SCALAR_FUNCTION", Definition = "CREATE FUNCTION dbo.fn_CardCount(@p int) RETURNS int AS BEGIN RETURN (SELECT COUNT(*) FROM dbo.PersonelCardDetails WHERE PersonelId = @p) END" },
                new DependencyInfo { Direction = DependencyDirection.Uses, Name = new ObjectName(null, Database, "dbo", "fn_IsValidCard"), TypeCode = "FN", TypeDescription = "SQL_SCALAR_FUNCTION", Definition = "CREATE FUNCTION dbo.fn_IsValidCard(@n varchar(32)) RETURNS bit AS BEGIN RETURN CASE WHEN LEN(@n) >= 8 THEN 1 ELSE 0 END END" },
            };
        }

        public async Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            var me = obj.ToObjectName();
            var a = new ForeignKeyInfo { Name = "FK_" + obj.Name + "_Personel", IsOutgoing = true, ParentTable = me, ReferencedTable = new ObjectName(null, obj.Database, "dbo", "Personel"), OnDelete = "NO_ACTION", OnUpdate = "NO_ACTION" };
            a.ParentColumns.Add("PersonelId");
            a.ReferencedColumns.Add("Id");
            var b = new ForeignKeyInfo { Name = "FK_" + obj.Name + "_Cards", IsOutgoing = true, ParentTable = me, ReferencedTable = new ObjectName(null, obj.Database, "dbo", "Cards"), OnDelete = "CASCADE", OnUpdate = "NO_ACTION", IsNotTrusted = true };
            b.ParentColumns.Add("CardId");
            b.ReferencedColumns.Add("Id");
            var c = new ForeignKeyInfo { Name = "FK_CardLog_" + obj.Name, IsIncoming = true, ParentTable = new ObjectName(null, obj.Database, "log", "CardLog"), ReferencedTable = me, OnDelete = "NO_ACTION", OnUpdate = "NO_ACTION" };
            c.ParentColumns.Add("DetailId");
            c.ReferencedColumns.Add("Id");
            return new[] { a, b, c };
        }

        public async Task<DataPage> GetDataAsync(DataQuery query, CancellationToken ct)
        {
            var problem = DataQueryBuilder.Validate(query);
            if (problem != null)
            {
                throw new InvalidOperationException(problem);
            }

            await Task.Delay(_delayMs, ct);
            var t = new DataTable();
            foreach (var name in new[] { "Id", "PersonelId", "CardId", "CardNumber", "IsActive", "ValidFrom", "ValidTo", "IsExpired" })
            {
                t.Columns.Add(new DataColumn("c" + t.Columns.Count.ToString(CultureInfo.InvariantCulture), typeof(string)) { Caption = name });
            }

            var rnd = new Random(7);
            int n = Math.Min(query.Top, 60);
            for (int i = 1; i <= n; i++)
            {
                t.Rows.Add(i.ToString(), (1000 + rnd.Next(500)).ToString(), (20 + rnd.Next(80)).ToString(),
                    i % 7 == 0 ? "NULL" : "TR" + rnd.Next(10000000, 99999999), i % 5 == 0 ? "0" : "1",
                    "2025-0" + (1 + i % 9) + "-1" + (i % 9) + " 08:00:00.000", i % 3 == 0 ? "NULL" : "2027-12-31 23:59:59.000", "0");
            }
            return new DataPage(t, DataQueryBuilder.BuildDisplayText(query), TimeSpan.FromMilliseconds(_delayMs));
        }

        public async Task<string> GetDdlAsync(ResolvedObject obj, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return "CREATE TABLE [" + obj.Schema + "].[" + obj.Name + "](\r\n\t[Id] [int] IDENTITY(1,1) NOT NULL,\r\n\t[PersonelId] [int] NOT NULL,\r\n" +
                   "\t[CardId] [int] NOT NULL,\r\n\t[CardNumber] [varchar](32) COLLATE Turkish_CI_AS NULL,\r\n" +
                   " CONSTRAINT [PK_" + obj.Name + "] PRIMARY KEY CLUSTERED ([Id] ASC)\r\n) ON [PRIMARY]\r\nGO\r\n";
        }
    }
}
