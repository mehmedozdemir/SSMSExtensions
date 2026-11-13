using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Parsing;
using SsmsToolkit.Data.Sql;

namespace SsmsToolkit.Data.Metadata
{
    /// <summary>
    /// Reads metadata and data with its own short-lived connections (never the SSMS query connection).
    /// Every command has a timeout and honours cancellation.
    /// </summary>
    public sealed class SqlTableMetadataSource : ITableMetadataSource
    {
        private const int MaxCellLength = 4000;

        private readonly ConnectionTarget _target;
        private readonly MetadataOptions _options;

        public SqlTableMetadataSource(ConnectionTarget target, MetadataOptions options = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _options = options ?? new MetadataOptions();
        }

        public string ServerName => _target.ServerName;

        public string Database => _target.Database;

        public async Task<ResolveResult> ResolveAsync(IReadOnlyList<ObjectName> candidates, CancellationToken ct)
        {
            ResolveResult fallback = ResolveResult.NotFound();
            foreach (var candidate in candidates ?? Array.Empty<ObjectName>())
            {
                var result = await ResolveOneAsync(candidate, null, ct).ConfigureAwait(false);
                if (result.Status == ResolveStatus.Found || result.Status == ResolveStatus.NotATableOrView)
                {
                    return result;
                }
                if (result.Status == ResolveStatus.Unsupported)
                {
                    fallback = result;
                }
            }
            return fallback;
        }

        private async Task<ResolveResult> ResolveOneAsync(ObjectName name, ObjectName viaSynonym, CancellationToken ct)
        {
            if (name.IsTemporary)
            {
                return ResolveResult.Unsupported("temp-table");
            }
            if (name.IsTableVariable)
            {
                return ResolveResult.Unsupported("table-variable");
            }
            if (name.Server != null)
            {
                return ResolveResult.Unsupported("linked-server");
            }

            var database = name.Database ?? (viaSynonym?.Database) ?? _target.Database;
            var target = _target.ForDatabase(database);
            var twoPart = new ObjectName(null, null, name.Schema, name.Name).ToQuotedSchemaName();

            using (var conn = target.CreateConnection())
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using (var cmd = CreateCommand(conn, MetadataQueries.Resolve))
                {
                    cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 776) { Value = twoPart });
                    using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    {
                        if (!await r.ReadAsync(ct).ConfigureAwait(false))
                        {
                            return ResolveResult.NotFound();
                        }

                        int id = r.GetInt32(0);
                        string schema = r.GetString(1);
                        string objName = r.GetString(2);
                        string type = r.GetString(3);
                        string typeDesc = r.GetString(4);
                        string baseName = r.IsDBNull(5) ? null : r.GetString(5);
                        string actualDb = conn.Database;

                        switch (type)
                        {
                            case "U":
                                return ResolveResult.Found(new ResolvedObject(id, actualDb, schema, objName, InspectableObjectType.Table, viaSynonym));
                            case "V":
                                return ResolveResult.Found(new ResolvedObject(id, actualDb, schema, objName, InspectableObjectType.View, viaSynonym));
                            case "SN":
                                if (viaSynonym != null)
                                {
                                    return ResolveResult.Unsupported("synonym-chain");
                                }
                                var target2 = SqlNameResolver.ParseMultipartName(baseName);
                                if (target2 == null)
                                {
                                    return ResolveResult.NotFound(baseName);
                                }
                                var synonym = new ObjectName(null, actualDb, schema, objName);
                                // A synonym's base name is relative to the synonym's database.
                                return await ResolveOneAsync(target2.Database == null ? target2.WithDatabase(actualDb) : target2, synonym, ct).ConfigureAwait(false);
                            default:
                                return ResolveResult.NotATableOrView(typeDesc);
                        }
                    }
                }
            }
        }

        public Task<TableSummary> GetSummaryAsync(ResolvedObject obj, CancellationToken ct)
        {
            return QueryAsync(obj, MetadataQueries.Summary, ct, r =>
            {
                if (!r.Read())
                {
                    throw new InvalidOperationException("Object no longer exists.");
                }

                return new TableSummary
                {
                    Schema = r.GetString(0),
                    Name = r.GetString(1),
                    TypeDescription = r.GetString(2),
                    Created = r.GetDateTime(3),
                    Modified = r.GetDateTime(4),
                    ApproximateRowCount = r.IsDBNull(5) ? (long?)null : Convert.ToInt64(r.GetValue(5), CultureInfo.InvariantCulture),
                    TotalSizeKb = r.IsDBNull(6) ? (long?)null : Convert.ToInt64(r.GetValue(6), CultureInfo.InvariantCulture) * 8,
                    UsedSizeKb = r.IsDBNull(7) ? (long?)null : Convert.ToInt64(r.GetValue(7), CultureInfo.InvariantCulture) * 8,
                    ColumnCount = r.GetInt32(8),
                    IndexCount = r.GetInt32(9),
                    Description = r.IsDBNull(10) ? null : r.GetString(10),
                    SynonymName = obj.Synonym?.ToString(),
                };
            });
        }

        public Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ResolvedObject obj, CancellationToken ct)
        {
            return QueryAsync<IReadOnlyList<ColumnInfo>>(obj, MetadataQueries.Columns, ct, r =>
            {
                var list = new List<ColumnInfo>();
                while (r.Read())
                {
                    var seed = ReadString(r, "identity_seed");
                    var increment = ReadString(r, "identity_increment");
                    list.Add(new ColumnInfo
                    {
                        Ordinal = r.GetInt32(r.GetOrdinal("column_id")),
                        Name = ReadString(r, "name"),
                        DataType = SqlTypeFormatter.Format(
                            ReadString(r, "type_name"),
                            ReadString(r, "system_type_name"),
                            r.GetInt16(r.GetOrdinal("max_length")),
                            r.GetByte(r.GetOrdinal("precision")),
                            r.GetByte(r.GetOrdinal("scale")),
                            r.GetBoolean(r.GetOrdinal("is_user_defined"))),
                        IsNullable = r.GetBoolean(r.GetOrdinal("is_nullable")),
                        IsIdentity = r.GetBoolean(r.GetOrdinal("is_identity")),
                        IsComputed = r.GetBoolean(r.GetOrdinal("is_computed")),
                        Collation = ReadString(r, "collation_name"),
                        DefaultDefinition = ReadString(r, "default_definition"),
                        ComputedDefinition = ReadString(r, "computed_definition"),
                        IdentityDefinition = seed == null ? null : "(" + seed + ", " + increment + ")",
                        Description = ReadString(r, "description"),
                        IsPrimaryKey = r.GetBoolean(r.GetOrdinal("is_primary_key")),
                    });
                }
                return list;
            });
        }

        public Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ResolvedObject obj, CancellationToken ct)
        {
            return QueryAsync<IReadOnlyList<IndexInfo>>(obj, MetadataQueries.Indexes, ct, r =>
            {
                var list = new List<IndexInfo>();
                IndexInfo current = null;
                while (r.Read())
                {
                    int indexId = r.GetInt32(0);
                    if (current == null || current.IndexId != indexId)
                    {
                        current = new IndexInfo
                        {
                            IndexId = indexId,
                            Name = r.IsDBNull(1) ? null : r.GetString(1),
                            TypeDescription = r.GetString(2),
                            IsUnique = r.GetBoolean(3),
                            IsPrimaryKey = r.GetBoolean(4),
                            IsUniqueConstraint = r.GetBoolean(5),
                            IsDisabled = r.GetBoolean(6),
                            FilterDefinition = r.IsDBNull(7) ? null : r.GetString(7),
                        };
                        list.Add(current);
                    }

                    var column = r.GetString(10) + (r.GetBoolean(9) ? " DESC" : string.Empty);
                    if (r.GetBoolean(8))
                    {
                        current.IncludedColumns.Add(r.GetString(10));
                    }
                    else
                    {
                        current.KeyColumns.Add(column);
                    }
                }
                return list;
            });
        }

        public Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(ResolvedObject obj, CancellationToken ct)
        {
            return QueryAsync<IReadOnlyList<ConstraintInfo>>(obj, MetadataQueries.Constraints, ct, r =>
            {
                var list = new List<ConstraintInfo>();
                while (r.Read())
                {
                    list.Add(new ConstraintInfo
                    {
                        Kind = r.GetInt32(0) == 0 ? ConstraintKind.Check : ConstraintKind.Default,
                        Name = r.GetString(1),
                        Column = r.IsDBNull(2) ? null : r.GetString(2),
                        Definition = r.IsDBNull(3) ? null : r.GetString(3),
                        IsDisabled = r.GetBoolean(4),
                        IsNotTrusted = r.GetBoolean(5),
                        IsSystemNamed = r.GetBoolean(6),
                    });
                }
                return list;
            });
        }

        public Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(ResolvedObject obj, CancellationToken ct)
        {
            return QueryAsync<IReadOnlyList<ForeignKeyInfo>>(obj, MetadataQueries.ForeignKeys, ct, r =>
            {
                var byId = new Dictionary<int, ForeignKeyInfo>();
                var list = new List<ForeignKeyInfo>();
                while (r.Read())
                {
                    int fkId = r.GetInt32(0);
                    if (!byId.TryGetValue(fkId, out var fk))
                    {
                        fk = new ForeignKeyInfo
                        {
                            Name = r.GetString(1),
                            IsOutgoing = r.GetInt32(2) == obj.ObjectId,
                            IsIncoming = r.GetInt32(3) == obj.ObjectId,
                            ParentTable = new ObjectName(null, obj.Database, r.GetString(4), r.GetString(5)),
                            ReferencedTable = new ObjectName(null, obj.Database, r.GetString(6), r.GetString(7)),
                            OnDelete = r.GetString(8),
                            OnUpdate = r.GetString(9),
                            IsDisabled = r.GetBoolean(10),
                            IsNotTrusted = r.GetBoolean(11),
                        };
                        byId.Add(fkId, fk);
                        list.Add(fk);
                    }
                    fk.ParentColumns.Add(r.GetString(12));
                    fk.ReferencedColumns.Add(r.GetString(13));
                }
                return list;
            });
        }

        public async Task<DataPage> GetDataAsync(DataQuery query, CancellationToken ct)
        {
            var problem = DataQueryBuilder.Validate(query);
            if (problem != null)
            {
                throw new InvalidOperationException(problem);
            }

            var sw = Stopwatch.StartNew();
            var table = new DataTable { Locale = CultureInfo.InvariantCulture };
            using (var conn = _target.ForDatabase(query.Target.Database).CreateConnection())
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                if (_options.ReadUncommitted)
                {
                    using (var iso = CreateCommand(conn, "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;"))
                    {
                        await iso.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }

                using (var cmd = CreateCommand(conn, DataQueryBuilder.BuildCommandText(query)))
                {
                    cmd.Parameters.Add(new SqlParameter(DataQueryBuilder.TopParameterName, SqlDbType.Int) { Value = query.Top });
                    using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    {
                        // Positional column names keep WPF binding paths valid for any SQL column name.
                        for (int i = 0; i < r.FieldCount; i++)
                        {
                            table.Columns.Add(new DataColumn("c" + i.ToString(CultureInfo.InvariantCulture), typeof(string)) { Caption = r.GetName(i) });
                        }

                        var values = new object[r.FieldCount];
                        while (await r.ReadAsync(ct).ConfigureAwait(false))
                        {
                            for (int i = 0; i < values.Length; i++)
                            {
                                values[i] = FormatCell(r, i);
                            }
                            table.Rows.Add(values);
                        }
                    }
                }
            }

            return new DataPage(table, DataQueryBuilder.BuildDisplayText(query), sw.Elapsed);
        }

        public async Task<string> GetDdlAsync(ResolvedObject obj, CancellationToken ct)
        {
            if (obj.Type == InspectableObjectType.View)
            {
                var definition = await QueryAsync(obj, MetadataQueries.ModuleDefinition, ct, r => r.Read() && !r.IsDBNull(0) ? r.GetString(0) : null)
                    .ConfigureAwait(false);
                return definition ?? "-- The definition is not available (encrypted, or no VIEW DEFINITION permission).";
            }

            var target = _target.ForDatabase(obj.Database);
            return await Task.Run(() => SmoScripter.ScriptTable(target, obj, _options.CommandTimeoutSeconds), ct).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- helpers

        private async Task<T> QueryAsync<T>(ResolvedObject obj, string sql, CancellationToken ct, Func<SqlDataReader, T> read)
        {
            using (var conn = _target.ForDatabase(obj.Database).CreateConnection())
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using (var cmd = CreateCommand(conn, sql))
                {
                    cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = obj.ObjectId });
                    using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    {
                        return read(r);
                    }
                }
            }
        }

        private SqlCommand CreateCommand(SqlConnection conn, string sql)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = _options.CommandTimeoutSeconds;
            return cmd;
        }

        private static string ReadString(SqlDataReader r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? null : r.GetString(i);
        }

        internal static string FormatCell(IDataRecord r, int i)
        {
            object value;
            try
            {
                if (r.IsDBNull(i))
                {
                    return "NULL";
                }
                value = r.GetValue(i);
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is System.IO.FileNotFoundException || ex is InvalidOperationException)
            {
                // CLR UDTs (geography, hierarchyid) need Microsoft.SqlServer.Types; show the type instead of failing the page.
                return "(" + r.GetDataTypeName(i) + ")";
            }

            switch (value)
            {
                case byte[] bytes:
                    return SqlTypeFormatter.FormatBinary(bytes);
                case DateTime dt:
                    var isDateOnly = string.Equals(r.GetDataTypeName(i), "date", StringComparison.OrdinalIgnoreCase);
                    return dt.ToString(isDateOnly ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                case DateTimeOffset dto:
                    return dto.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture);
                case bool b:
                    return b ? "1" : "0";
                case IFormattable f:
                    return f.ToString(null, CultureInfo.InvariantCulture);
                default:
                    var s = value.ToString();
                    return s.Length > MaxCellLength ? s.Substring(0, MaxCellLength) + "…" : s;
            }
        }
    }
}
