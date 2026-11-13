using System.Collections.Generic;

namespace SsmsToolkit.SsmsInterop
{
    /// <summary>
    /// SSMS-independent view of a query editor's connection. <see cref="OwnConnectionString"/> may contain a
    /// password: never log, display or persist it.
    /// </summary>
    internal sealed class EditorConnection
    {
        public string Source { get; set; }
        public string ServerName { get; set; }
        public string Database { get; set; }
        public int? AuthenticationType { get; set; }
        public string AuthenticationDescription { get; set; }

        /// <summary>Connection string for SsmsToolkit's own short-lived connections, or null if unsupported.</summary>
        public string OwnConnectionString { get; set; }

        public bool CanOpenOwnConnection => !string.IsNullOrEmpty(OwnConnectionString);

        /// <summary>Opaque SSMS object (UIConnectionInfo) used only inside SsmsInterop to open query windows.</summary>
        internal object SsmsHandle { get; set; }

        public List<string> Diagnostics { get; } = new List<string>();

        /// <summary>Safe to log: no secrets.</summary>
        public override string ToString() =>
            "source=" + Source + ", server=" + ServerName + ", db=" + Database + ", auth=" + AuthenticationType + " (" + AuthenticationDescription + ")"
            + (Diagnostics.Count > 0 ? ", " + string.Join("; ", Diagnostics) : string.Empty);
    }
}
