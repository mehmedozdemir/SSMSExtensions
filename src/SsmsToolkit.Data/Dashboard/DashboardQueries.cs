namespace SsmsToolkit.Data.Dashboard
{
    /// <summary>
    /// Read-only queries of the database dashboard. All run in the dashboard's database (Initial Catalog).
    /// Catalog views only, except where noted (msdb backup history, sys.dm_db_log_info, sys.dm_db_stats_properties).
    /// </summary>
    internal static class DashboardQueries
    {
        public const string Overview = @"
SELECT d.name, d.state_desc, d.recovery_model_desc, d.compatibility_level, d.collation_name, d.create_date,
       SUSER_SNAME(d.owner_sid), d.is_read_only, d.user_access_desc, d.is_query_store_on, d.is_cdc_enabled,
       d.snapshot_isolation_state_desc, d.is_read_committed_snapshot_on, d.page_verify_option_desc,
       d.is_auto_close_on, d.is_auto_shrink_on, d.is_auto_create_stats_on, d.is_auto_update_stats_on,
       d.log_reuse_wait_desc,
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
       CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
       CAST(SERVERPROPERTY('EngineEdition') AS int)
FROM sys.databases AS d
WHERE d.database_id = DB_ID();";

        // size / max_size / growth are in 8 KB pages; max_size -1 = unlimited, 0 = no growth.
        public const string Files = @"
SELECT f.name, f.type_desc, f.physical_name,
       CAST(f.size AS bigint) * 8 / 1024.0,
       CAST(FILEPROPERTY(f.name, 'SpaceUsed') AS bigint) * 8 / 1024.0,
       CASE WHEN f.max_size = -1 THEN NULL ELSE CAST(f.max_size AS bigint) * 8 / 1024.0 END,
       f.is_percent_growth,
       CASE WHEN f.is_percent_growth = 1 THEN CAST(f.growth AS decimal(19, 2)) ELSE CAST(f.growth AS bigint) * 8 / 1024.0 END
FROM sys.database_files AS f
ORDER BY f.type, f.file_id;";

        public const string ObjectCounts = @"
SELECT o.type_desc, COUNT(*)
FROM sys.objects AS o
WHERE o.is_ms_shipped = 0 AND o.type NOT IN ('PK', 'F', 'C', 'D', 'UQ', 'IT', 'S', 'SQ', 'EC')
GROUP BY o.type_desc
ORDER BY COUNT(*) DESC, o.type_desc;";

        public const string RecentChanges = @"
SELECT TOP (@top) s.name, o.name, o.type_desc, o.create_date, o.modify_date
FROM sys.objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.is_ms_shipped = 0 AND o.parent_object_id = 0 AND o.type NOT IN ('IT', 'S', 'SQ')
ORDER BY o.modify_date DESC;";

        // Same arithmetic as sp_spaceused, from catalog views (no VIEW DATABASE STATE needed).
        public const string TableSizes = @"
WITH alloc AS (
    SELECT p.object_id,
           SUM(a.total_pages) AS reserved,
           SUM(a.used_pages) AS used,
           SUM(CASE WHEN p.index_id IN (0, 1) THEN CASE WHEN a.type = 1 THEN a.data_pages ELSE a.used_pages END ELSE 0 END) AS data
    FROM sys.partitions AS p
    JOIN sys.allocation_units AS a
      ON (a.type IN (1, 3) AND a.container_id = p.hobt_id) OR (a.type = 2 AND a.container_id = p.partition_id)
    GROUP BY p.object_id
), row_counts AS (
    SELECT p.object_id, SUM(p.rows) AS rows
    FROM sys.partitions AS p
    WHERE p.index_id IN (0, 1)
    GROUP BY p.object_id
)
SELECT TOP (@top) s.name, t.name, ISNULL(r.rows, 0),
       ISNULL(a.reserved, 0) * 8 / 1024.0,
       ISNULL(a.data, 0) * 8 / 1024.0,
       (ISNULL(a.used, 0) - ISNULL(a.data, 0)) * 8 / 1024.0,
       (ISNULL(a.reserved, 0) - ISNULL(a.used, 0)) * 8 / 1024.0
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
LEFT JOIN alloc AS a ON a.object_id = t.object_id
LEFT JOIN row_counts AS r ON r.object_id = t.object_id
WHERE t.is_ms_shipped = 0
ORDER BY ISNULL(a.reserved, 0) DESC, s.name, t.name;";

        // msdb: needs SELECT on msdb.dbo.backupset (granted to public by default on SQL Server; absent on Azure SQL DB).
        public const string Backups = @"
SELECT b.type, MAX(b.backup_finish_date)
FROM msdb.dbo.backupset AS b
WHERE b.database_name = DB_NAME() AND b.type IN ('D', 'I', 'L')
GROUP BY b.type;";

        public const string SizeHistory = @"
SELECT CAST(b.backup_finish_date AS date), MAX(b.backup_size) / 1048576.0
FROM msdb.dbo.backupset AS b
WHERE b.database_name = DB_NAME() AND b.type = 'D' AND b.backup_finish_date >= DATEADD(day, -@days, GETDATE())
GROUP BY CAST(b.backup_finish_date AS date)
ORDER BY 1;";

        // SQL Server 2016 SP2+; needs VIEW DATABASE STATE (or VIEW SERVER STATE on older builds).
        public const string VlfCount = @"SELECT COUNT(*) FROM sys.dm_db_log_info(DB_ID());";

        public const string TablesWithoutPrimaryKey = @"
SELECT s.name + N'.' + t.name, COUNT(*) OVER ()
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
  AND NOT EXISTS (SELECT 1 FROM sys.key_constraints AS k WHERE k.parent_object_id = t.object_id AND k.type = 'PK')
ORDER BY s.name, t.name
OFFSET 0 ROWS FETCH NEXT @top ROWS ONLY;";

        public const string Heaps = @"
SELECT s.name + N'.' + t.name, COUNT(*) OVER ()
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
JOIN sys.indexes AS i ON i.object_id = t.object_id AND i.index_id = 0
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name
OFFSET 0 ROWS FETCH NEXT @top ROWS ONLY;";

        public const string UntrustedConstraints = @"
SELECT x.text, COUNT(*) OVER ()
FROM (
    SELECT s.name + N'.' + t.name + N' — ' + fk.name + CASE WHEN fk.is_disabled = 1 THEN N' (FK, disabled)' ELSE N' (FK, not trusted)' END AS text
    FROM sys.foreign_keys AS fk
    JOIN sys.tables AS t ON t.object_id = fk.parent_object_id
    JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE (fk.is_not_trusted = 1 OR fk.is_disabled = 1) AND fk.is_not_for_replication = 0
    UNION ALL
    SELECT s.name + N'.' + t.name + N' — ' + cc.name + CASE WHEN cc.is_disabled = 1 THEN N' (CHECK, disabled)' ELSE N' (CHECK, not trusted)' END
    FROM sys.check_constraints AS cc
    JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
    JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE (cc.is_not_trusted = 1 OR cc.is_disabled = 1) AND cc.is_not_for_replication = 0
) AS x
ORDER BY x.text
OFFSET 0 ROWS FETCH NEXT @top ROWS ONLY;";

        // Statistics on tables with at least 1000 rows whose modifications exceed 20% of the rows.
        public const string StaleStatistics = @"
SELECT x.text, COUNT(*) OVER ()
FROM (
    SELECT s.name + N'.' + t.name + N' — ' + st.name
           + N': ' + CAST(sp.modification_counter AS nvarchar(20)) + N' / ' + CAST(sp.rows AS nvarchar(20))
           + N' rows changed, updated ' + ISNULL(CONVERT(nvarchar(16), sp.last_updated, 120), N'never') AS text,
           CAST(sp.modification_counter AS float) / NULLIF(sp.rows, 0) AS ratio
    FROM sys.stats AS st
    JOIN sys.tables AS t ON t.object_id = st.object_id
    JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    CROSS APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) AS sp
    WHERE t.is_ms_shipped = 0 AND sp.rows >= 1000 AND sp.modification_counter > sp.rows * 0.2
) AS x
ORDER BY x.ratio DESC
OFFSET 0 ROWS FETCH NEXT @top ROWS ONLY;";

        public const string IdentityColumns = @"
SELECT s.name + N'.' + t.name, c.name, ty.name,
       CAST(c.last_value AS decimal(38, 0)),
       CASE ty.name
           WHEN 'tinyint' THEN CAST(255 AS decimal(38, 0))
           WHEN 'smallint' THEN CAST(32767 AS decimal(38, 0))
           WHEN 'int' THEN CAST(2147483647 AS decimal(38, 0))
           WHEN 'bigint' THEN CAST(9223372036854775807 AS decimal(38, 0))
           ELSE CASE WHEN c.precision >= 38 THEN CAST('99999999999999999999999999999999999999' AS decimal(38, 0))
                     ELSE POWER(CAST(10 AS decimal(38, 0)), c.precision) - 1 END
       END
FROM sys.identity_columns AS c
JOIN sys.tables AS t ON t.object_id = c.object_id
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
JOIN sys.types AS ty ON ty.user_type_id = c.system_type_id
WHERE t.is_ms_shipped = 0 AND c.last_value IS NOT NULL;";

        // Only meaningful when server logins are visible; otherwise every user would look orphaned.
        public const string CanSeeLogins = @"
SELECT CASE WHEN IS_SRVROLEMEMBER('sysadmin') = 1 OR HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW ANY DEFINITION') = 1 THEN 1 ELSE 0 END;";

        public const string OrphanedUsers = @"
SELECT dp.name, COUNT(*) OVER ()
FROM sys.database_principals AS dp
LEFT JOIN sys.server_principals AS sp ON sp.sid = dp.sid
WHERE dp.type IN ('S', 'U', 'G') AND dp.authentication_type = 1 AND dp.principal_id > 4 AND sp.sid IS NULL
ORDER BY dp.name
OFFSET 0 ROWS FETCH NEXT @top ROWS ONLY;";
    }
}
