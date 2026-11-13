using System.Collections.Generic;
using System.Text;

namespace SsmsToolkit.SsmsInterop
{
    /// <summary>
    /// SSMS-independent snapshot of the active query editor's connection.
    /// <see cref="OwnConnectionString"/> may contain a password: never log, display or persist it.
    /// </summary>
    internal sealed class ActiveConnectionInfo
    {
        public string Source { get; set; }
        public string EditorCaption { get; set; }
        public string ServerName { get; set; }
        public string Database { get; set; }
        public int? AuthenticationType { get; set; }
        public string AuthenticationDescription { get; set; }
        public string UserName { get; set; }
        public string SsmsConnectionTypeName { get; set; }
        public string SsmsConnectionState { get; set; }
        public List<string> ConnectionStringKeys { get; } = new List<string>();
        public List<string> AdvancedOptionKeys { get; } = new List<string>();
        public List<string> Diagnostics { get; } = new List<string>();

        /// <summary>Connection string for SsmsToolkit's own short-lived connection, or null if unsupported.</summary>
        public string OwnConnectionString { get; set; }

        public bool CanOpenOwnConnection => !string.IsNullOrEmpty(OwnConnectionString);

        /// <summary>Human-readable description. Safe to log: contains no secrets.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Source: " + Source);
            sb.AppendLine("Editor: " + EditorCaption);
            sb.AppendLine("Server: " + ServerName);
            sb.AppendLine("Database: " + Database);
            sb.AppendLine("Auth: " + AuthenticationType + " (" + AuthenticationDescription + ")");
            sb.AppendLine("User: " + UserName);
            sb.AppendLine("SSMS connection: " + SsmsConnectionTypeName + ", state=" + SsmsConnectionState);
            sb.AppendLine("SSMS connection string keys: " + string.Join(", ", ConnectionStringKeys));
            sb.AppendLine("UIConnectionInfo.AdvancedOptions keys: " + string.Join(", ", AdvancedOptionKeys));
            sb.AppendLine("Own connection possible: " + CanOpenOwnConnection);
            foreach (var d in Diagnostics)
            {
                sb.AppendLine("  - " + d);
            }
            return sb.ToString();
        }
    }
}
