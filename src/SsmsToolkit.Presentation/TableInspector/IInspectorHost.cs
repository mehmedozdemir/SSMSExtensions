using System;
using System.Threading.Tasks;
using SsmsToolkit.Data.Model;

namespace SsmsToolkit.Presentation.TableInspector
{
    /// <summary>What the inspector needs from its host (SSMS or the Playground).</summary>
    public interface IInspectorHost
    {
        /// <summary>Opens another table on the same connection (e.g. from a foreign key).</summary>
        Task OpenTableAsync(ObjectName name);

        /// <summary>Opens a new query window with the given text, connected like the source editor.</summary>
        Task OpenQueryAsync(string sql, string database);

        void CopyToClipboard(string text);

        /// <summary>Logs an unexpected exception. Must not throw.</summary>
        void ReportError(string context, Exception exception);
    }

    public sealed class InspectorOptions
    {
        public int RowLimit { get; set; } = 200;

        public bool ReadUncommitted { get; set; }

        /// <summary>Set when the server name matches a configured production pattern.</summary>
        public bool IsProduction { get; set; }
    }
}
