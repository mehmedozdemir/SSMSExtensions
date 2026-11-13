using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SsmsToolkit.Data.Dashboard;

namespace SsmsToolkit.Playground
{
    /// <summary>Sample dashboard data (--dashboard without --cs).</summary>
    internal sealed class FakeDashboardSource : IDatabaseDashboardSource
    {
        public string ServerName => "sql-prod-01";

        public string Database => "SampleDb";

        public async Task<(DatabaseOverview Overview, IReadOnlyList<DatabaseFileInfo> Files)> GetOverviewAsync(CancellationToken ct)
        {
            await Task.Delay(150, ct);
            var overview = new DatabaseOverview
            {
                Name = Database,
                State = "ONLINE",
                RecoveryModel = "FULL",
                CompatibilityLevel = 150,
                Collation = "Turkish_CI_AS",
                CreateDate = new DateTime(2019, 4, 2, 10, 15, 0),
                Owner = "sa",
                UserAccess = "MULTI_USER",
                IsQueryStoreOn = true,
                SnapshotIsolation = "OFF",
                IsReadCommittedSnapshotOn = true,
                PageVerify = "CHECKSUM",
                IsAutoCreateStatsOn = true,
                IsAutoUpdateStatsOn = true,
                LogReuseWait = "LOG_BACKUP",
                ProductVersion = "16.0.4135.4",
                Edition = "Enterprise Edition (64-bit)",
                EngineEdition = 3,
            };
            var files = new List<DatabaseFileInfo>
            {
                new DatabaseFileInfo { LogicalName = "SampleDb", Type = "ROWS", PhysicalName = @"D:\Data\SampleDb.mdf", SizeMb = 51200, UsedMb = 43800, Growth = 1024 },
                new DatabaseFileInfo { LogicalName = "SampleDb_2", Type = "ROWS", PhysicalName = @"D:\Data\SampleDb_2.ndf", SizeMb = 20480, UsedMb = 19900, MaxSizeMb = 20480, Growth = 10, IsPercentGrowth = true },
                new DatabaseFileInfo { LogicalName = "SampleDb_log", Type = "LOG", PhysicalName = @"L:\Log\SampleDb_log.ldf", SizeMb = 16384, UsedMb = 14200, Growth = 512 },
            };
            return (overview, files);
        }

        public async Task<IReadOnlyList<TableSizeInfo>> GetTableSizesAsync(int top, CancellationToken ct)
        {
            await Task.Delay(300, ct);
            var names = new[] { "Transactions", "TransactionLines", "AuditLog", "Cards", "Customers", "Products", "Orders", "OrderLines", "Payments", "Sessions", "Devices", "Stations" };
            return names.Select((n, i) => new TableSizeInfo
            {
                Schema = i % 4 == 3 ? "sales" : "dbo",
                Name = n,
                Rows = (long)(80_000_000 / Math.Pow(1.9, i)),
                ReservedMb = (decimal)(22_000 / Math.Pow(1.7, i)),
                DataMb = (decimal)(15_000 / Math.Pow(1.7, i)),
                IndexMb = (decimal)(6_500 / Math.Pow(1.7, i)),
                UnusedMb = (decimal)(500 / Math.Pow(1.7, i)),
            }).ToList();
        }

        public async Task<IReadOnlyList<SizeHistoryPoint>> GetSizeHistoryAsync(int days, CancellationToken ct)
        {
            await Task.Delay(200, ct);
            var start = DateTime.Today.AddDays(-days);
            return Enumerable.Range(0, 52).Select(w => new SizeHistoryPoint
            {
                Date = start.AddDays(w * 7),
                SizeMb = 30_000 + w * 260 + (w % 5) * 120,
            }).ToList();
        }

        public Task<IReadOnlyList<ObjectCount>> GetObjectCountsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ObjectCount>>(new[]
            {
                new ObjectCount { TypeDesc = "SQL_STORED_PROCEDURE", Count = 412 },
                new ObjectCount { TypeDesc = "USER_TABLE", Count = 238 },
                new ObjectCount { TypeDesc = "VIEW", Count = 96 },
                new ObjectCount { TypeDesc = "SQL_SCALAR_FUNCTION", Count = 41 },
                new ObjectCount { TypeDesc = "SQL_TRIGGER", Count = 17 },
                new ObjectCount { TypeDesc = "SYNONYM", Count = 6 },
            });

        public Task<IReadOnlyList<RecentObjectChange>> GetRecentChangesAsync(int top, CancellationToken ct)
        {
            var now = DateTime.Now;
            return Task.FromResult<IReadOnlyList<RecentObjectChange>>(new[]
            {
                new RecentObjectChange { Schema = "dbo", Name = "usp_GetCardBalance", TypeDesc = "SQL_STORED_PROCEDURE", Created = now.AddDays(-400), Modified = now.AddHours(-3) },
                new RecentObjectChange { Schema = "dbo", Name = "Cards", TypeDesc = "USER_TABLE", Created = now.AddDays(-900), Modified = now.AddDays(-1) },
                new RecentObjectChange { Schema = "sales", Name = "vw_DailyTotals", TypeDesc = "VIEW", Created = now.AddDays(-30), Modified = now.AddDays(-2) },
                new RecentObjectChange { Schema = "dbo", Name = "fn_Tariff", TypeDesc = "SQL_SCALAR_FUNCTION", Created = now.AddDays(-60), Modified = now.AddDays(-6) },
            });
        }

        public async Task<IReadOnlyList<HealthFinding>> GetHealthAsync(DatabaseOverview overview, IReadOnlyList<DatabaseFileInfo> files, CancellationToken ct)
        {
            await Task.Delay(600, ct);
            var now = DateTime.Now;
            var findings = new List<HealthFinding>
            {
                HealthRules.Backups(overview, new BackupInfo { LastFull = now.AddDays(-2), LastLog = now.AddDays(-3) }, now),
                HealthRules.Log(overview, files),
                HealthRules.Vlf(420),
                HealthRules.Settings(overview),
                HealthRules.Files(files),
                HealthRules.Identity(new[] { new IdentityUsage { Table = "dbo.AuditLog", Column = "Id", Type = "int", LastValue = 1_900_000_000, MaxValue = 2_147_483_647 } }),
                HealthRules.ObjectList("NoPrimaryKey", HealthSeverity.Info, new[] { "dbo.ImportStaging", "dbo.TmpCards" }, 2),
                HealthRules.ObjectList("Heaps", HealthSeverity.Info, new[] { "dbo.ImportStaging" }, 1),
                HealthRules.ObjectList("Untrusted", HealthSeverity.Warning, Array.Empty<string>(), 0),
                HealthRules.ObjectList("StaleStats", HealthSeverity.Warning, new[] { "dbo.Transactions — IX_Date: 3200000 / 9100000 rows changed, updated 2026-10-02 03:00" }, 1),
                HealthFinding.Unavailable("Orphaned", "VIEW ANY DEFINITION"),
            };
            return HealthRules.Sort(findings);
        }
    }
}
