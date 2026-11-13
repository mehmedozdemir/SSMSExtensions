using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SsmsToolkit.Data.Dashboard
{
    /// <summary>
    /// Pure health-check rules over data already read from the server. Thresholds are deliberately conservative:
    /// the dashboard points at things worth a look, it does not grade the database.
    /// </summary>
    public static class HealthRules
    {
        public const int MaxListedItems = 50;

        public static readonly TimeSpan FullBackupWarningAge = TimeSpan.FromDays(7);
        public static readonly TimeSpan LogBackupWarningAge = TimeSpan.FromHours(24);
        public const int VlfInfo = 300;
        public const int VlfWarning = 1000;
        public const double LogUsedWarningPercent = 80;
        public const double FileNearMaxPercent = 90;
        public const double IdentityWarning = 0.80;
        public const double IdentityCritical = 0.95;

        public static HealthFinding Backups(DatabaseOverview db, BackupInfo backups, DateTime now)
        {
            const string id = "Backups";
            if (string.Equals(db.Name, "tempdb", StringComparison.OrdinalIgnoreCase))
            {
                return new HealthFinding(id, HealthSeverity.Ok, "Backups_NotApplicable");
            }

            var lastData = Max(backups.LastFull, backups.LastDifferential);
            HealthFinding finding;
            if (backups.LastFull == null)
            {
                finding = new HealthFinding(id, HealthSeverity.Critical, "Backups_NoFull");
            }
            else if (now - lastData.Value > FullBackupWarningAge)
            {
                finding = new HealthFinding(id, HealthSeverity.Warning, "Backups_Old", now - lastData.Value);
            }
            else
            {
                finding = new HealthFinding(id, HealthSeverity.Ok, "Backups_Ok", now - lastData.Value);
            }

            bool needsLogBackups = !string.Equals(db.RecoveryModel, "SIMPLE", StringComparison.OrdinalIgnoreCase);
            if (needsLogBackups && finding.Severity != HealthSeverity.Critical
                && (backups.LastLog == null || now - backups.LastLog.Value > LogBackupWarningAge))
            {
                finding = new HealthFinding(id, HealthSeverity.Warning, backups.LastLog == null ? "Backups_NoLog" : "Backups_OldLog",
                    db.RecoveryModel, backups.LastLog == null ? (object)null : now - backups.LastLog.Value);
            }

            Add(finding, "FULL", backups.LastFull);
            Add(finding, "DIFF", backups.LastDifferential);
            Add(finding, "LOG", backups.LastLog);
            return finding;
        }

        public static HealthFinding Log(DatabaseOverview db, IReadOnlyList<DatabaseFileInfo> files)
        {
            const string id = "Log";
            var logs = files.Where(f => f.IsLog && f.UsedMb != null).ToList();
            decimal size = logs.Sum(f => f.SizeMb);
            decimal used = logs.Sum(f => f.UsedMb.Value);
            double percent = size > 0 ? (double)(used / size * 100m) : 0;
            var wait = db.LogReuseWait ?? "NOTHING";
            bool waiting = wait != "NOTHING" && wait != "CHECKPOINT";

            if (percent >= LogUsedWarningPercent && waiting)
            {
                return new HealthFinding(id, HealthSeverity.Warning, "Log_FullAndWaiting", Math.Round(percent), wait);
            }
            if (waiting)
            {
                return new HealthFinding(id, HealthSeverity.Info, "Log_Waiting", wait, Math.Round(percent));
            }
            return new HealthFinding(id, HealthSeverity.Ok, "Log_Ok", Math.Round(percent));
        }

        public static HealthFinding Vlf(int count)
        {
            const string id = "Vlf";
            if (count > VlfWarning)
            {
                return new HealthFinding(id, HealthSeverity.Warning, "Vlf_High", count);
            }
            if (count > VlfInfo)
            {
                return new HealthFinding(id, HealthSeverity.Info, "Vlf_Elevated", count);
            }
            return new HealthFinding(id, HealthSeverity.Ok, "Vlf_Ok", count);
        }

        public static HealthFinding Settings(DatabaseOverview db)
        {
            const string id = "Settings";
            var issues = new List<(HealthSeverity Severity, string Text)>();
            if (db.IsAutoCloseOn)
            {
                issues.Add((HealthSeverity.Warning, "AUTO_CLOSE = ON"));
            }
            if (db.IsAutoShrinkOn)
            {
                issues.Add((HealthSeverity.Warning, "AUTO_SHRINK = ON"));
            }
            if (!string.Equals(db.PageVerify, "CHECKSUM", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add((HealthSeverity.Warning, "PAGE_VERIFY = " + (db.PageVerify ?? "?")));
            }
            if (!db.IsAutoCreateStatsOn)
            {
                issues.Add((HealthSeverity.Info, "AUTO_CREATE_STATISTICS = OFF"));
            }
            if (!db.IsAutoUpdateStatsOn)
            {
                issues.Add((HealthSeverity.Info, "AUTO_UPDATE_STATISTICS = OFF"));
            }
            int serverLevel = db.ServerCompatibilityLevel;
            if (serverLevel > 0 && db.CompatibilityLevel < serverLevel && !db.IsSystemDatabase)
            {
                issues.Add((HealthSeverity.Info, "COMPATIBILITY_LEVEL = " + db.CompatibilityLevel + " (server: " + serverLevel + ")"));
            }

            if (issues.Count == 0)
            {
                return new HealthFinding(id, HealthSeverity.Ok, "Settings_Ok");
            }

            var finding = new HealthFinding(id, issues.Min(i => i.Severity), "Settings_Issues", issues.Count);
            finding.Items.AddRange(issues.Select(i => i.Text));
            finding.ItemCount = issues.Count;
            return finding;
        }

        public static HealthFinding Files(IReadOnlyList<DatabaseFileInfo> files)
        {
            const string id = "Files";
            var issues = new List<(HealthSeverity Severity, string Text)>();
            foreach (var f in files)
            {
                if (f.MaxSizeMb > 0 && f.UsedMb != null && (double)(f.UsedMb.Value / f.MaxSizeMb.Value * 100m) >= FileNearMaxPercent)
                {
                    issues.Add((HealthSeverity.Warning, f.LogicalName + ": " + Mb(f.UsedMb.Value) + " / max " + Mb(f.MaxSizeMb.Value)));
                }
                if (f.IsPercentGrowth && f.Growth > 0)
                {
                    issues.Add((HealthSeverity.Info, f.LogicalName + ": growth " + f.Growth.ToString("0", CultureInfo.InvariantCulture) + "%"));
                }
                else if (!f.IsPercentGrowth && f.Growth > 0 && f.Growth < 64 && f.SizeMb >= 1024)
                {
                    issues.Add((HealthSeverity.Info, f.LogicalName + ": growth " + Mb(f.Growth) + " (size " + Mb(f.SizeMb) + ")"));
                }
            }

            if (issues.Count == 0)
            {
                return new HealthFinding(id, HealthSeverity.Ok, "Files_Ok");
            }

            var finding = new HealthFinding(id, issues.Min(i => i.Severity), "Files_Issues", issues.Count);
            finding.Items.AddRange(issues.Select(i => i.Text));
            finding.ItemCount = issues.Count;
            return finding;
        }

        public static HealthFinding Identity(IReadOnlyList<IdentityUsage> columns)
        {
            const string id = "Identity";
            var risky = columns.Where(c => c.Ratio >= IdentityWarning).OrderByDescending(c => c.Ratio).ToList();
            if (risky.Count == 0)
            {
                return new HealthFinding(id, HealthSeverity.Ok, "Identity_Ok");
            }

            var severity = risky[0].Ratio >= IdentityCritical ? HealthSeverity.Critical : HealthSeverity.Warning;
            var finding = new HealthFinding(id, severity, "Identity_Near", risky.Count) { ItemCount = risky.Count };
            finding.Items.AddRange(risky.Take(MaxListedItems).Select(c =>
                c.Table + "." + c.Column + " (" + c.Type + "): " + (c.Ratio * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"));
            return finding;
        }

        /// <summary>A check that lists objects: Ok when empty, otherwise <paramref name="severity"/>.</summary>
        public static HealthFinding ObjectList(string id, HealthSeverity severity, IReadOnlyList<string> objects, int totalCount)
        {
            if (totalCount == 0)
            {
                return new HealthFinding(id, HealthSeverity.Ok, id + "_Ok");
            }

            var finding = new HealthFinding(id, severity, id + "_Found", totalCount) { ItemCount = totalCount };
            finding.Items.AddRange(objects.Take(MaxListedItems));
            return finding;
        }

        /// <summary>Critical first, then warnings, info, unavailable, ok; stable within a severity.</summary>
        public static IReadOnlyList<HealthFinding> Sort(IEnumerable<HealthFinding> findings) =>
            findings.Select((f, i) => (f, i)).OrderBy(x => x.f.Severity).ThenBy(x => x.i).Select(x => x.f).ToList();

        private static string Mb(decimal mb) =>
            mb >= 1024 ? (mb / 1024m).ToString("0.#", CultureInfo.InvariantCulture) + " GB" : mb.ToString("0", CultureInfo.InvariantCulture) + " MB";

        private static void Add(HealthFinding finding, string type, DateTime? value) =>
            finding.Items.Add(type + ": " + (value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—"));

        private static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a > b ? a : b);
    }
}
