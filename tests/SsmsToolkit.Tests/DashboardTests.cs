using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SsmsToolkit.Data.Dashboard;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class HealthRulesTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 13, 12, 0, 0);

        private static DatabaseOverview Db(string recovery = "FULL") => new DatabaseOverview
        {
            Name = "Sales",
            RecoveryModel = recovery,
            CompatibilityLevel = 160,
            ProductVersion = "16.0.4135.4",
            PageVerify = "CHECKSUM",
            IsAutoCreateStatsOn = true,
            IsAutoUpdateStatsOn = true,
            LogReuseWait = "NOTHING",
        };

        [Fact]
        public void Backups_missing_full_is_critical()
        {
            var f = HealthRules.Backups(Db(), new BackupInfo(), Now);
            Assert.Equal(HealthSeverity.Critical, f.Severity);
            Assert.Equal("Backups_NoFull", f.MessageId);
        }

        [Fact]
        public void Backups_old_full_is_warning_but_recent_diff_counts()
        {
            Assert.Equal(HealthSeverity.Warning, HealthRules.Backups(Db("SIMPLE"), new BackupInfo { LastFull = Now.AddDays(-10) }, Now).Severity);
            var withDiff = HealthRules.Backups(Db("SIMPLE"), new BackupInfo { LastFull = Now.AddDays(-10), LastDifferential = Now.AddHours(-3) }, Now);
            Assert.Equal(HealthSeverity.Ok, withDiff.Severity);
        }

        [Fact]
        public void Backups_full_recovery_needs_log_backups()
        {
            var f = HealthRules.Backups(Db("FULL"), new BackupInfo { LastFull = Now.AddHours(-2) }, Now);
            Assert.Equal(HealthSeverity.Warning, f.Severity);
            Assert.Equal("Backups_NoLog", f.MessageId);

            var ok = HealthRules.Backups(Db("FULL"), new BackupInfo { LastFull = Now.AddHours(-2), LastLog = Now.AddMinutes(-15) }, Now);
            Assert.Equal(HealthSeverity.Ok, ok.Severity);
            Assert.Contains(ok.Items, i => i.StartsWith("LOG: 2026-11-13 11:45"));
        }

        [Fact]
        public void Log_full_and_waiting_is_warning()
        {
            var db = Db();
            db.LogReuseWait = "LOG_BACKUP";
            var files = new[] { new DatabaseFileInfo { Type = "LOG", SizeMb = 100, UsedMb = 90 } };
            var f = HealthRules.Log(db, files);
            Assert.Equal(HealthSeverity.Warning, f.Severity);
            Assert.Equal(new object[] { 90.0, "LOG_BACKUP" }, f.Args);
        }

        [Theory]
        [InlineData(50, HealthSeverity.Ok)]
        [InlineData(400, HealthSeverity.Info)]
        [InlineData(5000, HealthSeverity.Warning)]
        public void Vlf_thresholds(int count, HealthSeverity expected) => Assert.Equal(expected, HealthRules.Vlf(count).Severity);

        [Fact]
        public void Settings_flags_auto_shrink_and_old_compatibility()
        {
            var db = Db();
            db.IsAutoShrinkOn = true;
            db.CompatibilityLevel = 130;
            var f = HealthRules.Settings(db);
            Assert.Equal(HealthSeverity.Warning, f.Severity);
            Assert.Contains("AUTO_SHRINK = ON", f.Items);
            Assert.Contains("COMPATIBILITY_LEVEL = 130 (server: 160)", f.Items);
            Assert.Equal(HealthSeverity.Ok, HealthRules.Settings(Db()).Severity);
        }

        [Fact]
        public void Files_percent_growth_and_near_max()
        {
            var files = new[]
            {
                new DatabaseFileInfo { LogicalName = "data", Type = "ROWS", SizeMb = 2048, UsedMb = 1900, MaxSizeMb = 2000, Growth = 10, IsPercentGrowth = true },
                new DatabaseFileInfo { LogicalName = "log", Type = "LOG", SizeMb = 4096, UsedMb = 10, Growth = 1 },
            };
            var f = HealthRules.Files(files);
            Assert.Equal(HealthSeverity.Warning, f.Severity);
            Assert.Equal(new[] { "data: 1.9 GB / max 2 GB", "data: growth 10%", "log: growth 1 MB (size 4 GB)" }, f.Items);
        }

        [Fact]
        public void Identity_near_max()
        {
            var cols = new[]
            {
                new IdentityUsage { Table = "dbo.A", Column = "Id", Type = "int", LastValue = 2_100_000_000, MaxValue = 2_147_483_647 },
                new IdentityUsage { Table = "dbo.B", Column = "Id", Type = "smallint", LastValue = 27000, MaxValue = 32767 },
                new IdentityUsage { Table = "dbo.C", Column = "Id", Type = "bigint", LastValue = 5, MaxValue = 9_223_372_036_854_775_807 },
            };
            var f = HealthRules.Identity(cols);
            Assert.Equal(HealthSeverity.Critical, f.Severity);
            Assert.Equal(2, f.ItemCount);
            Assert.StartsWith("dbo.A.Id (int): 97.8%", f.Items[0]);
        }

        [Fact]
        public void Sort_puts_critical_first()
        {
            var sorted = HealthRules.Sort(new[]
            {
                new HealthFinding("a", HealthSeverity.Ok, "x"),
                new HealthFinding("b", HealthSeverity.Unavailable, "x"),
                new HealthFinding("c", HealthSeverity.Critical, "x"),
                new HealthFinding("d", HealthSeverity.Info, "x"),
            });
            Assert.Equal(new[] { "c", "d", "b", "a" }, sorted.Select(f => f.CheckId));
        }
    }

    public class SqlDatabaseDashboardSourceTests : IClassFixture<LocalDbFixture>
    {
        private readonly LocalDbFixture _db;

        public SqlDatabaseDashboardSourceTests(LocalDbFixture db)
        {
            _db = db;
        }

        private SqlDatabaseDashboardSource Source() => new SqlDatabaseDashboardSource(_db.Target, 60);

        [LocalDbFact]
        public async Task Overview_and_files()
        {
            var (overview, files) = await Source().GetOverviewAsync(CancellationToken.None);
            Assert.Equal(_db.Database, overview.Name);
            Assert.Equal("ONLINE", overview.State);
            Assert.True(overview.ServerCompatibilityLevel >= 130);
            Assert.Contains(files, f => f.Type == "ROWS" && f.SizeMb > 0 && f.UsedMb > 0);
            Assert.Contains(files, f => f.IsLog);
        }

        [LocalDbFact]
        public async Task Table_sizes_include_fixture_tables()
        {
            var tables = await Source().GetTableSizesAsync(100, CancellationToken.None);
            var customer = tables.Single(t => t.Schema == "dbo" && t.Name == "Customer");
            Assert.True(customer.Rows > 0);
            Assert.True(customer.ReservedMb > 0);
            // Each part is rounded to 6 decimals by SQL Server.
            Assert.InRange(customer.ReservedMb - (customer.DataMb + customer.IndexMb + customer.UnusedMb), -0.00001m, 0.00001m);
        }

        [LocalDbFact]
        public async Task Objects_and_recent_changes()
        {
            var counts = await Source().GetObjectCountsAsync(CancellationToken.None);
            Assert.Contains(counts, c => c.TypeDesc == "USER_TABLE" && c.Count >= 2);
            var recent = await Source().GetRecentChangesAsync(10, CancellationToken.None);
            Assert.NotEmpty(recent);
            Assert.True(recent.Zip(recent.Skip(1), (a, b) => a.Modified >= b.Modified).All(x => x));
        }

        [LocalDbFact]
        public async Task Health_runs_every_check_without_throwing()
        {
            var source = Source();
            var (overview, files) = await source.GetOverviewAsync(CancellationToken.None);
            var findings = await source.GetHealthAsync(overview, files, CancellationToken.None);

            var ids = findings.Select(f => f.CheckId).ToList();
            foreach (var id in new[] { "Backups", "Log", "Vlf", "Settings", "Files", "Identity", "NoPrimaryKey", "Heaps", "Untrusted", "StaleStats", "Orphaned" })
            {
                Assert.Contains(id, ids);
            }
            // The throwaway test database has never been backed up.
            Assert.Equal(HealthSeverity.Critical, findings.Single(f => f.CheckId == "Backups").Severity);
            Assert.All(findings.Where(f => f.Severity == HealthSeverity.Unavailable), f => Assert.False(string.IsNullOrEmpty(f.Error)));
        }

        [LocalDbFact]
        public async Task Size_history_is_empty_without_backups()
        {
            var history = await Source().GetSizeHistoryAsync(365, CancellationToken.None);
            Assert.Empty(history);
        }
    }
}
