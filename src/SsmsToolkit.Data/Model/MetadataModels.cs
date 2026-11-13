using System;
using System.Collections.Generic;
using System.Data;

namespace SsmsToolkit.Data.Model
{
    public enum InspectableObjectType
    {
        Table,
        View,
    }

    /// <summary>A table or view that exists in a specific database.</summary>
    public sealed class ResolvedObject
    {
        public ResolvedObject(int objectId, string database, string schema, string name, InspectableObjectType type, ObjectName synonym)
        {
            ObjectId = objectId;
            Database = database;
            Schema = schema;
            Name = name;
            Type = type;
            Synonym = synonym;
        }

        public int ObjectId { get; }
        public string Database { get; }
        public string Schema { get; }
        public string Name { get; }
        public InspectableObjectType Type { get; }

        /// <summary>The synonym the user clicked, if the object was reached through one.</summary>
        public ObjectName Synonym { get; }

        public ObjectName ToObjectName() => new ObjectName(null, Database, Schema, Name);

        /// <summary>Case-insensitive identity used to reuse an already open inspector.</summary>
        public string Key(string server) => (server + "|" + Database + "|" + Schema + "|" + Name).ToLowerInvariant();

        public override string ToString() => Schema + "." + Name;
    }

    public enum ResolveStatus
    {
        Found,
        NotFound,
        NotATableOrView,
        Unsupported,
    }

    public sealed class ResolveResult
    {
        private ResolveResult(ResolveStatus status, ResolvedObject obj, string detail)
        {
            Status = status;
            Object = obj;
            Detail = detail;
        }

        public ResolveStatus Status { get; }
        public ResolvedObject Object { get; }

        /// <summary>Extra information (e.g. the actual object type, or why it is unsupported).</summary>
        public string Detail { get; }

        public static ResolveResult Found(ResolvedObject obj) => new ResolveResult(ResolveStatus.Found, obj, null);
        public static ResolveResult NotFound(string detail = null) => new ResolveResult(ResolveStatus.NotFound, null, detail);
        public static ResolveResult NotATableOrView(string typeDesc) => new ResolveResult(ResolveStatus.NotATableOrView, null, typeDesc);
        public static ResolveResult Unsupported(string reason) => new ResolveResult(ResolveStatus.Unsupported, null, reason);
    }

    public sealed class TableSummary
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        public string TypeDescription { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }

        /// <summary>Approximate row count from sys.partitions (no table scan).</summary>
        public long? ApproximateRowCount { get; set; }

        public long? TotalSizeKb { get; set; }
        public long? UsedSizeKb { get; set; }
        public int ColumnCount { get; set; }
        public int IndexCount { get; set; }
        public string Description { get; set; }
        public string SynonymName { get; set; }
    }

    public sealed class ColumnInfo
    {
        public int Ordinal { get; set; }
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public string IdentityDefinition { get; set; }
        public bool IsComputed { get; set; }
        public string ComputedDefinition { get; set; }
        public string DefaultDefinition { get; set; }
        public string Collation { get; set; }
        public string Description { get; set; }
    }

    public sealed class IndexInfo
    {
        public int IndexId { get; set; }
        public string Name { get; set; }
        public string TypeDescription { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsUniqueConstraint { get; set; }
        public bool IsUnique { get; set; }
        public bool IsDisabled { get; set; }
        public string FilterDefinition { get; set; }
        public List<string> KeyColumns { get; } = new List<string>();
        public List<string> IncludedColumns { get; } = new List<string>();

        public string Kind => IsPrimaryKey ? "PK" : IsUniqueConstraint ? "UQ" : IsUnique ? "UNIQUE" : "INDEX";
    }

    public sealed class ForeignKeyInfo
    {
        public string Name { get; set; }

        /// <summary>True when this table holds the foreign key (references another table).</summary>
        public bool IsOutgoing { get; set; }

        /// <summary>True when another table (or this one, if self-referencing) references this table.</summary>
        public bool IsIncoming { get; set; }

        public ObjectName ParentTable { get; set; }
        public ObjectName ReferencedTable { get; set; }
        public List<string> ParentColumns { get; } = new List<string>();
        public List<string> ReferencedColumns { get; } = new List<string>();
        public string OnDelete { get; set; }
        public string OnUpdate { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsNotTrusted { get; set; }
    }

    /// <summary>A read-only page of rows. Column names are in <see cref="DataColumn.Caption"/>.</summary>
    public sealed class DataPage
    {
        public DataPage(DataTable table, string sqlText, TimeSpan elapsed)
        {
            Table = table;
            SqlText = sqlText;
            Elapsed = elapsed;
        }

        public DataTable Table { get; }

        /// <summary>Executable equivalent of the query (literal TOP), for "open as query".</summary>
        public string SqlText { get; }

        public TimeSpan Elapsed { get; }
    }
}
