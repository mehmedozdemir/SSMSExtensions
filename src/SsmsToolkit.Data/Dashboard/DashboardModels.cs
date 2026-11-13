using System;
using System.Collections.Generic;

namespace SsmsToolkit.Data.Dashboard
{
    /// <summary>One row of sys.databases plus server version, for the dashboard header and settings checks.</summary>
    public sealed class DatabaseOverview
    {
        public string Name { get; set; }
        public string State { get; set; }
        public string RecoveryModel { get; set; }
        public int CompatibilityLevel { get; set; }
        public string Collation { get; set; }
        public DateTime CreateDate { get; set; }
        public string Owner { get; set; }
        public bool IsReadOnly { get; set; }
        public string UserAccess { get; set; }
        public bool IsQueryStoreOn { get; set; }
        public bool IsCdcEnabled { get; set; }
        public string SnapshotIsolation { get; set; }
        public bool IsReadCommittedSnapshotOn { get; set; }
        public string PageVerify { get; set; }
        public bool IsAutoCloseOn { get; set; }
        public bool IsAutoShrinkOn { get; set; }
        public bool IsAutoCreateStatsOn { get; set; }
        public bool IsAutoUpdateStatsOn { get; set; }
        public string LogReuseWait { get; set; }
        public string ProductVersion { get; set; }
        public string Edition { get; set; }

        /// <summary>5 = Azure SQL Database, 8 = Azure SQL Managed Instance (SERVERPROPERTY('EngineEdition')).</summary>
        public int EngineEdition { get; set; }

        public bool IsAzureSqlDatabase => EngineEdition == 5;

        /// <summary>Highest compatibility level of the server (major version × 10), e.g. 160 for SQL Server 2022.</summary>
        public int ServerCompatibilityLevel
        {
            get
            {
                var major = ProductVersion?.Split('.')[0];
                return int.TryParse(major, out var m) ? m * 10 : 0;
            }
        }

        public bool IsSystemDatabase =>
            string.Equals(Name, "master", StringComparison.OrdinalIgnoreCase) || string.Equals(Name, "model", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Name, "msdb", StringComparison.OrdinalIgnoreCase) || string.Equals(Name, "tempdb", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DatabaseFileInfo
    {
        public string LogicalName { get; set; }

        /// <summary>ROWS, LOG, FILESTREAM, FULLTEXT.</summary>
        public string Type { get; set; }
        public string PhysicalName { get; set; }
        public decimal SizeMb { get; set; }

        /// <summary>Null when FILEPROPERTY is not available (e.g. FILESTREAM).</summary>
        public decimal? UsedMb { get; set; }

        /// <summary>Null = unlimited; 0 = growth disabled.</summary>
        public decimal? MaxSizeMb { get; set; }
        public bool IsPercentGrowth { get; set; }

        /// <summary>Percent, or MB when <see cref="IsPercentGrowth"/> is false. 0 = no growth.</summary>
        public decimal Growth { get; set; }

        public bool IsLog => string.Equals(Type, "LOG", StringComparison.OrdinalIgnoreCase);

        public double? UsedPercent => UsedMb == null || SizeMb <= 0 ? (double?)null : (double)(UsedMb.Value / SizeMb * 100m);
    }

    public sealed class ObjectCount
    {
        public string TypeDesc { get; set; }
        public int Count { get; set; }
    }

    public sealed class RecentObjectChange
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        public string TypeDesc { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }

        public bool IsTableOrView => TypeDesc == "USER_TABLE" || TypeDesc == "VIEW";
    }

    public sealed class TableSizeInfo
    {
        public string Schema { get; set; }
        public string Name { get; set; }

        /// <summary>From sys.partitions (approximate, like sp_spaceused).</summary>
        public long Rows { get; set; }
        public decimal ReservedMb { get; set; }
        public decimal DataMb { get; set; }
        public decimal IndexMb { get; set; }
        public decimal UnusedMb { get; set; }
    }

    public sealed class SizeHistoryPoint
    {
        public DateTime Date { get; set; }

        /// <summary>Full backup size (≈ used data) in MB.</summary>
        public decimal SizeMb { get; set; }
    }

    public sealed class BackupInfo
    {
        public DateTime? LastFull { get; set; }
        public DateTime? LastDifferential { get; set; }
        public DateTime? LastLog { get; set; }
    }

    public sealed class IdentityUsage
    {
        public string Table { get; set; }
        public string Column { get; set; }
        public string Type { get; set; }
        public decimal LastValue { get; set; }
        public decimal MaxValue { get; set; }

        public double Ratio => MaxValue <= 0 ? 0 : (double)(Math.Abs(LastValue) / MaxValue);
    }

    public enum HealthSeverity
    {
        Critical,
        Warning,
        Info,
        Unavailable,
        Ok,
    }

    /// <summary>
    /// Result of one health check. Text is localized by the UI from <see cref="CheckId"/> and <see cref="MessageId"/>
    /// ("Health_{CheckId}" for the title, "Health_{MessageId}" for the message, formatted with <see cref="Args"/>).
    /// </summary>
    public sealed class HealthFinding
    {
        public HealthFinding(string checkId, HealthSeverity severity, string messageId, params object[] args)
        {
            CheckId = checkId;
            Severity = severity;
            MessageId = messageId;
            Args = args ?? Array.Empty<object>();
        }

        public string CheckId { get; }
        public HealthSeverity Severity { get; }
        public string MessageId { get; }
        public object[] Args { get; }

        /// <summary>Affected objects or details (already formatted, e.g. "dbo.Orders").</summary>
        public List<string> Items { get; } = new List<string>();

        /// <summary>Total number of affected objects (Items may be capped).</summary>
        public int ItemCount { get; set; }

        /// <summary>Why the check could not run (permission, version); only for <see cref="HealthSeverity.Unavailable"/>.</summary>
        public string Error { get; set; }

        public static HealthFinding Unavailable(string checkId, string error) =>
            new HealthFinding(checkId, HealthSeverity.Unavailable, "Unavailable") { Error = error };
    }
}
