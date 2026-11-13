using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;

namespace SsmsToolkit.Data.Metadata
{
    /// <summary>Everything the Table Inspector reads. Implementations must be read-only.</summary>
    public interface ITableMetadataSource
    {
        string ServerName { get; }

        string Database { get; }

        /// <summary>Tries the candidates in order and returns the first table/view that exists.</summary>
        Task<ResolveResult> ResolveAsync(IReadOnlyList<ObjectName> candidates, CancellationToken ct);

        Task<TableSummary> GetSummaryAsync(ResolvedObject obj, CancellationToken ct);

        Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ResolvedObject obj, CancellationToken ct);

        Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ResolvedObject obj, CancellationToken ct);

        /// <summary>CHECK and DEFAULT constraints.</summary>
        Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(ResolvedObject obj, CancellationToken ct);

        Task<IReadOnlyList<TriggerInfo>> GetTriggersAsync(ResolvedObject obj, CancellationToken ct);

        /// <summary>Objects that use this one and objects this one uses (sys.sql_expression_dependencies).</summary>
        Task<IReadOnlyList<DependencyInfo>> GetDependenciesAsync(ResolvedObject obj, CancellationToken ct);

        Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(ResolvedObject obj, CancellationToken ct);

        Task<DataPage> GetDataAsync(DataQuery query, CancellationToken ct);

        Task<string> GetDdlAsync(ResolvedObject obj, CancellationToken ct);
    }

    public sealed class MetadataOptions
    {
        public int CommandTimeoutSeconds { get; set; } = 15;

        /// <summary>Data tab only. Off by default.</summary>
        public bool ReadUncommitted { get; set; }
    }
}
