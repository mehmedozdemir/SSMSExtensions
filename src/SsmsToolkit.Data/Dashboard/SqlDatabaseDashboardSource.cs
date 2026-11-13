using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SsmsToolkit.Data.Sql;

namespace SsmsToolkit.Data.Dashboard
{
    /// <summary>Everything the database dashboard shows. Implemented with SQL; faked in the Playground and tests.</summary>
    public interface IDatabaseDashboardSource
    {
        string ServerName { get; }

        string Database { get; }

        Task<(DatabaseOverview Overview, IReadOnlyList<DatabaseFileInfo> Files)> GetOverviewAsync(CancellationToken ct);

        Task<IReadOnlyList<TableSizeInfo>> GetTableSizesAsync(int top, CancellationToken ct);

        /// <summary>Full backup sizes per day; throws when msdb is not readable.</summary>
        Task<IReadOnlyList<SizeHistoryPoint>> GetSizeHistoryAsync(int days, CancellationToken ct);

        Task<IReadOnlyList<ObjectCount>> GetObjectCountsAsync(CancellationToken ct);

        Task<IReadOnlyList<RecentObjectChange>> GetRecentChangesAsync(int top, CancellationToken ct);

        /// <summary>All checks; a check that cannot run is returned as <see cref="HealthSeverity.Unavailable"/>, never thrown.</summary>
        Task<IReadOnlyList<HealthFinding>> GetHealthAsync(DatabaseOverview overview, IReadOnlyList<DatabaseFileInfo> files, CancellationToken ct);
    }

    /// <summary>Reads the dashboard with its own short-lived connections; every command has a timeout and honours cancellation.</summary>
    public sealed class SqlDatabaseDashboardSource : IDatabaseDashboardSource
    {
        private readonly ConnectionTarget _target;
        private readonly int _timeoutSeconds;

        public SqlDatabaseDashboardSource(ConnectionTarget target, int commandTimeoutSeconds = 15)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _timeoutSeconds = Math.Max(1, commandTimeoutSeconds);
        }

        public string ServerName => _target.ServerName;

        public string Database => _target.Database;

        public async Task<(DatabaseOverview Overview, IReadOnlyList<DatabaseFileInfo> Files)> GetOverviewAsync(CancellationToken ct)
        {
            DatabaseOverview overview = null;
            await QueryAsync(DashboardQueries.Overview, null, r =>
            {
                overview = new DatabaseOverview
                {
                    Name = r.GetString(0),
                    State = r.GetString(1),
                    RecoveryModel = r.GetString(2),
                    CompatibilityLevel = r.GetByte(3),
                    Collation = Str(r, 4),
                    CreateDate = r.GetDateTime(5),
                    Owner = Str(r, 6),
                    IsReadOnly = r.GetBoolean(7),
                    UserAccess = Str(r, 8),
                    IsQueryStoreOn = !r.IsDBNull(9) && r.GetBoolean(9),
                    IsCdcEnabled = r.GetBoolean(10),
                    SnapshotIsolation = Str(r, 11),
                    IsReadCommittedSnapshotOn = r.GetBoolean(12),
                    PageVerify = Str(r, 13),
                    IsAutoCloseOn = r.GetBoolean(14),
                    IsAutoShrinkOn = r.GetBoolean(15),
                    IsAutoCreateStatsOn = r.GetBoolean(16),
                    IsAutoUpdateStatsOn = r.GetBoolean(17),
                    LogReuseWait = Str(r, 18),
                    ProductVersion = Str(r, 19),
                    Edition = Str(r, 20),
                    EngineEdition = r.IsDBNull(21) ? 0 : r.GetInt32(21),
                };
            }, ct).ConfigureAwait(false);

            if (overview == null)
            {
                throw new InvalidOperationException("Database not found: " + _target.Database);
            }

            var files = new List<DatabaseFileInfo>();
            await QueryAsync(DashboardQueries.Files, null, r => files.Add(new DatabaseFileInfo
            {
                LogicalName = r.GetString(0),
                Type = r.GetString(1),
                PhysicalName = Str(r, 2),
                SizeMb = r.GetDecimal(3),
                UsedMb = r.IsDBNull(4) ? (decimal?)null : r.GetDecimal(4),
                MaxSizeMb = r.IsDBNull(5) ? (decimal?)null : r.GetDecimal(5),
                IsPercentGrowth = r.GetBoolean(6),
                Growth = r.GetDecimal(7),
            }), ct).ConfigureAwait(false);

            return (overview, files);
        }

        public async Task<IReadOnlyList<TableSizeInfo>> GetTableSizesAsync(int top, CancellationToken ct)
        {
            var list = new List<TableSizeInfo>();
            await QueryAsync(DashboardQueries.TableSizes, cmd => Top(cmd, top), r => list.Add(new TableSizeInfo
            {
                Schema = r.GetString(0),
                Name = r.GetString(1),
                Rows = Convert.ToInt64(r.GetValue(2)),
                ReservedMb = r.GetDecimal(3),
                DataMb = r.GetDecimal(4),
                IndexMb = r.GetDecimal(5),
                UnusedMb = r.GetDecimal(6),
            }), ct).ConfigureAwait(false);
            return list;
        }

        public async Task<IReadOnlyList<SizeHistoryPoint>> GetSizeHistoryAsync(int days, CancellationToken ct)
        {
            var list = new List<SizeHistoryPoint>();
            await QueryAsync(DashboardQueries.SizeHistory,
                cmd => cmd.Parameters.Add(new SqlParameter("@days", SqlDbType.Int) { Value = days }),
                r => list.Add(new SizeHistoryPoint { Date = r.GetDateTime(0), SizeMb = r.GetDecimal(1) }), ct).ConfigureAwait(false);
            return list;
        }

        public async Task<IReadOnlyList<ObjectCount>> GetObjectCountsAsync(CancellationToken ct)
        {
            var list = new List<ObjectCount>();
            await QueryAsync(DashboardQueries.ObjectCounts, null,
                r => list.Add(new ObjectCount { TypeDesc = r.GetString(0), Count = r.GetInt32(1) }), ct).ConfigureAwait(false);
            return list;
        }

        public async Task<IReadOnlyList<RecentObjectChange>> GetRecentChangesAsync(int top, CancellationToken ct)
        {
            var list = new List<RecentObjectChange>();
            await QueryAsync(DashboardQueries.RecentChanges, cmd => Top(cmd, top), r => list.Add(new RecentObjectChange
            {
                Schema = r.GetString(0),
                Name = r.GetString(1),
                TypeDesc = r.GetString(2),
                Created = r.GetDateTime(3),
                Modified = r.GetDateTime(4),
            }), ct).ConfigureAwait(false);
            return list;
        }

        public async Task<IReadOnlyList<HealthFinding>> GetHealthAsync(DatabaseOverview overview, IReadOnlyList<DatabaseFileInfo> files, CancellationToken ct)
        {
            var findings = new List<HealthFinding>
            {
                await CheckAsync("Backups", async () =>
                {
                    var backups = new BackupInfo();
                    await QueryAsync(DashboardQueries.Backups, null, r =>
                    {
                        var when = r.GetDateTime(1);
                        switch (r.GetString(0))
                        {
                            case "D": backups.LastFull = when; break;
                            case "I": backups.LastDifferential = when; break;
                            case "L": backups.LastLog = when; break;
                        }
                    }, ct).ConfigureAwait(false);
                    return HealthRules.Backups(overview, backups, DateTime.Now);
                }, ct).ConfigureAwait(false),
                HealthRules.Log(overview, files),
                await CheckAsync("Vlf", async () =>
                {
                    int count = 0;
                    await QueryAsync(DashboardQueries.VlfCount, null, r => count = r.GetInt32(0), ct).ConfigureAwait(false);
                    return HealthRules.Vlf(count);
                }, ct).ConfigureAwait(false),
                HealthRules.Settings(overview),
                HealthRules.Files(files),
                await CheckAsync("Identity", async () =>
                {
                    var columns = new List<IdentityUsage>();
                    await QueryAsync(DashboardQueries.IdentityColumns, null, r => columns.Add(new IdentityUsage
                    {
                        Table = r.GetString(0),
                        Column = r.GetString(1),
                        Type = r.GetString(2),
                        LastValue = r.GetDecimal(3),
                        MaxValue = r.GetDecimal(4),
                    }), ct).ConfigureAwait(false);
                    return HealthRules.Identity(columns);
                }, ct).ConfigureAwait(false),
                await ListCheckAsync("NoPrimaryKey", HealthSeverity.Info, DashboardQueries.TablesWithoutPrimaryKey, ct).ConfigureAwait(false),
                await ListCheckAsync("Heaps", HealthSeverity.Info, DashboardQueries.Heaps, ct).ConfigureAwait(false),
                await ListCheckAsync("Untrusted", HealthSeverity.Warning, DashboardQueries.UntrustedConstraints, ct).ConfigureAwait(false),
                await ListCheckAsync("StaleStats", HealthSeverity.Warning, DashboardQueries.StaleStatistics, ct).ConfigureAwait(false),
                await CheckAsync("Orphaned", async () =>
                {
                    int canSee = 0;
                    await QueryAsync(DashboardQueries.CanSeeLogins, null, r => canSee = r.GetInt32(0), ct).ConfigureAwait(false);
                    if (canSee == 0)
                    {
                        return HealthFinding.Unavailable("Orphaned", "VIEW ANY DEFINITION");
                    }
                    return await ListAsync("Orphaned", HealthSeverity.Warning, DashboardQueries.OrphanedUsers, ct).ConfigureAwait(false);
                }, ct).ConfigureAwait(false),
            };
            return HealthRules.Sort(findings);
        }

        private Task<HealthFinding> ListCheckAsync(string id, HealthSeverity severity, string sql, CancellationToken ct) =>
            CheckAsync(id, () => ListAsync(id, severity, sql, ct), ct);

        private async Task<HealthFinding> ListAsync(string id, HealthSeverity severity, string sql, CancellationToken ct)
        {
            var items = new List<string>();
            int total = 0;
            await QueryAsync(sql, cmd => Top(cmd, HealthRules.MaxListedItems), r =>
            {
                items.Add(r.GetString(0));
                total = r.GetInt32(1);
            }, ct).ConfigureAwait(false);
            return HealthRules.ObjectList(id, severity, items, total);
        }

        /// <summary>Runs one check; SQL errors (permission, unsupported version) become "unavailable".</summary>
        private static async Task<HealthFinding> CheckAsync(string id, Func<Task<HealthFinding>> check, CancellationToken ct)
        {
            try
            {
                return await check().ConfigureAwait(false);
            }
            catch (SqlException ex) when (!ct.IsCancellationRequested)
            {
                return HealthFinding.Unavailable(id, ex.Message);
            }
        }

        private async Task QueryAsync(string sql, Action<SqlCommand> parameters, Action<SqlDataReader> read, CancellationToken ct)
        {
            using (var conn = _target.CreateConnection())
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = _timeoutSeconds;
                    parameters?.Invoke(cmd);
                    using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    {
                        while (await r.ReadAsync(ct).ConfigureAwait(false))
                        {
                            read(r);
                        }
                    }
                }
            }
        }

        private static void Top(SqlCommand cmd, int top) =>
            cmd.Parameters.Add(new SqlParameter("@top", SqlDbType.Int) { Value = Math.Max(1, top) });

        private static string Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i).ToString();
    }
}
