namespace SsmsToolkit.Data.Metadata
{
    /// <summary>
    /// Catalog queries. All are parameterized on @id / @name, read only sys.* catalog views that need no
    /// special permission (no DMVs: VIEW DATABASE STATE is often missing), and never scan user tables.
    /// </summary>
    internal static class MetadataQueries
    {
        public const string Resolve = @"
SET NOCOUNT ON;
DECLARE @id int = OBJECT_ID(@name);
SELECT o.object_id, s.name AS schema_name, o.name, RTRIM(o.type) AS type, o.type_desc,
       CASE WHEN o.type = 'SN' THEN (SELECT sy.base_object_name FROM sys.synonyms sy WHERE sy.object_id = o.object_id) END AS base_object_name
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.object_id = @id;";

        public const string Summary = @"
SET NOCOUNT ON;
SELECT s.name AS schema_name, o.name, o.type_desc, o.create_date, o.modify_date,
       (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = o.object_id AND p.index_id IN (0, 1)) AS row_count,
       (SELECT SUM(a.total_pages) FROM sys.partitions p JOIN sys.allocation_units a ON a.container_id = p.partition_id
         WHERE p.object_id = o.object_id) AS total_pages,
       (SELECT SUM(a.used_pages) FROM sys.partitions p JOIN sys.allocation_units a ON a.container_id = p.partition_id
         WHERE p.object_id = o.object_id) AS used_pages,
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = o.object_id) AS column_count,
       (SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id = o.object_id AND i.index_id > 0) AS index_count,
       CAST(ep.value AS nvarchar(4000)) AS description
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.extended_properties ep ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
WHERE o.object_id = @id;";

        public const string Columns = @"
SET NOCOUNT ON;
SELECT c.column_id, c.name,
       TYPE_NAME(c.user_type_id) AS type_name, TYPE_NAME(c.system_type_id) AS system_type_name,
       CAST(CASE WHEN c.user_type_id <> c.system_type_id THEN 1 ELSE 0 END AS bit) AS is_user_defined,
       c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed, c.collation_name,
       dc.definition AS default_definition,
       cc.definition AS computed_definition,
       CAST(ic.seed_value AS nvarchar(64)) AS identity_seed, CAST(ic.increment_value AS nvarchar(64)) AS identity_increment,
       CAST(ep.value AS nvarchar(4000)) AS description,
       CAST(CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS bit) AS is_primary_key
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
LEFT JOIN sys.extended_properties ep ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = N'MS_Description'
LEFT JOIN (SELECT ixc.column_id
           FROM sys.indexes ix
           JOIN sys.index_columns ixc ON ixc.object_id = ix.object_id AND ixc.index_id = ix.index_id
           WHERE ix.object_id = @id AND ix.is_primary_key = 1) pk ON pk.column_id = c.column_id
WHERE c.object_id = @id
ORDER BY c.column_id;";

        public const string Indexes = @"
SET NOCOUNT ON;
SELECT i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.is_disabled,
       i.filter_definition, ic.is_included_column, ic.is_descending_key, c.name AS column_name
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = @id AND i.index_id > 0
ORDER BY i.is_primary_key DESC, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id;";

        public const string Constraints = @"
SET NOCOUNT ON;
SELECT CAST(0 AS int) AS kind, cc.name, c.name AS column_name, cc.definition,
       cc.is_disabled, cc.is_not_trusted, cc.is_system_named
FROM sys.check_constraints cc
LEFT JOIN sys.columns c ON c.object_id = cc.parent_object_id AND c.column_id = cc.parent_column_id
WHERE cc.parent_object_id = @id
UNION ALL
SELECT CAST(1 AS int), dc.name, c.name, dc.definition,
       CAST(0 AS bit), CAST(0 AS bit), dc.is_system_named
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = @id
ORDER BY kind, name;";

        public const string Triggers = @"
SET NOCOUNT ON;
SELECT t.name, t.is_disabled, t.is_instead_of_trigger,
       STUFF((SELECT N', ' + te.type_desc FROM sys.trigger_events te
              WHERE te.object_id = t.object_id ORDER BY te.type FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'') AS events,
       OBJECT_DEFINITION(t.object_id) AS definition
FROM sys.triggers t
WHERE t.parent_id = @id
ORDER BY t.name;";

        /// <summary>
        /// Objects that reference @id ("used by") and objects @id references ("uses").
        /// One-part names inside modules are caller-dependent (referenced_id is NULL), so they are matched by name too.
        /// TOP keeps very popular tables from returning thousands of module definitions.
        /// </summary>
        public const string Dependencies = @"
SET NOCOUNT ON;
DECLARE @schema sysname = OBJECT_SCHEMA_NAME(@id), @name sysname = OBJECT_NAME(@id);
SELECT TOP (500) 0 AS direction, NULL AS db, s.name AS schema_name, o.name, RTRIM(o.type) AS type, o.type_desc,
       OBJECT_DEFINITION(o.object_id) AS definition
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.object_id <> @id
  AND o.object_id IN (
        SELECT d.referencing_id FROM sys.sql_expression_dependencies d
        WHERE d.referencing_class = 1
          AND (d.referenced_id = @id
               OR (d.referenced_id IS NULL AND d.referenced_entity_name = @name
                   AND (d.referenced_schema_name IS NULL OR d.referenced_schema_name = @schema)
                   AND (d.referenced_database_name IS NULL OR d.referenced_database_name = DB_NAME())
                   AND d.referenced_server_name IS NULL)))
UNION ALL
SELECT TOP (500) 1, d.referenced_database_name, COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), d.referenced_schema_name),
       d.referenced_entity_name, RTRIM(o.type), o.type_desc, OBJECT_DEFINITION(d.referenced_id)
FROM (SELECT DISTINCT referenced_id, referenced_database_name, referenced_schema_name, referenced_entity_name
      FROM sys.sql_expression_dependencies
      WHERE referencing_id = @id AND referencing_class = 1 AND referenced_class = 1 AND referenced_server_name IS NULL) d
LEFT JOIN sys.objects o ON o.object_id = d.referenced_id
ORDER BY 1, 3, 4;";

        public const string ForeignKeys = @"
SET NOCOUNT ON;
SELECT fk.object_id AS fk_id, fk.name, fk.parent_object_id, fk.referenced_object_id,
       ps.name AS parent_schema, po.name AS parent_table,
       rs.name AS referenced_schema, ro.name AS referenced_table,
       fk.delete_referential_action_desc, fk.update_referential_action_desc, fk.is_disabled, fk.is_not_trusted,
       pc.name AS parent_column, rc.name AS referenced_column
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.objects po ON po.object_id = fk.parent_object_id
JOIN sys.schemas ps ON ps.schema_id = po.schema_id
JOIN sys.objects ro ON ro.object_id = fk.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE fk.parent_object_id = @id OR fk.referenced_object_id = @id
ORDER BY fk.name, fkc.constraint_column_id;";

        public const string ModuleDefinition = @"
SET NOCOUNT ON;
SELECT OBJECT_DEFINITION(@id);";
    }
}
