using System;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SsmsToolkit.Core
{
    /// <summary>
    /// Phase 0: records the host and the versions of shared assemblies SSMS actually loaded,
    /// so we know what we can depend on (CLAUDE.md rule 9).
    /// </summary>
    internal static class EnvironmentReport
    {
        private static readonly string[] InterestingAssemblies =
        {
            "Microsoft.VisualStudio.Shell.Framework",
            "Microsoft.VisualStudio.Shell.15.0",
            "Microsoft.VisualStudio.Text.UI.Wpf",
            "SqlWorkbench.Interfaces",
            "SqlPackageBase",
            "SQLEditors",
            "Microsoft.SqlServer.RegSvrEnum",
            "Microsoft.SqlServer.ConnectionInfo",
            "Microsoft.SqlServer.Smo",
            "Microsoft.SqlServer.TransactSql.ScriptDom",
            "Microsoft.SqlServer.GridControl",
            "Microsoft.Data.SqlClient",
            "System.Data.SqlClient",
            "Newtonsoft.Json",
            "System.Text.Json",
        };

        public static string Build()
        {
            var sb = new StringBuilder();
            try
            {
                var main = Process.GetCurrentProcess().MainModule;
                sb.AppendLine("Host: " + main?.FileName);
                sb.AppendLine("Host version: " + main?.FileVersionInfo.ProductVersion);
            }
            catch (Exception ex)
            {
                sb.AppendLine("Host: <unavailable> " + ex.Message);
            }

            sb.AppendLine("CLR: " + Environment.Version + ", 64-bit process: " + Environment.Is64BitProcess);
            sb.AppendLine("SsmsToolkit: " + typeof(EnvironmentReport).Assembly.Location);
            sb.AppendLine("Loaded assemblies of interest:");

            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var name in InterestingAssemblies)
            {
                var matches = loaded.Where(a => string.Equals(SafeName(a), name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    sb.AppendLine("  " + name + ": (not loaded yet)");
                    continue;
                }

                foreach (var a in matches)
                {
                    string location;
                    try { location = a.IsDynamic ? "<dynamic>" : a.Location; }
                    catch { location = "<unknown>"; }
                    sb.AppendLine("  " + name + ": " + a.GetName().Version + "  " + location);
                }
            }

            return sb.ToString();
        }

        private static string SafeName(System.Reflection.Assembly a)
        {
            try { return a.GetName().Name; }
            catch { return null; }
        }
    }
}
