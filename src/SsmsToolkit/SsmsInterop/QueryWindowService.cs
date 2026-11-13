using System;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.SqlServer.Management.UI.VSIntegration.Editors;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Core;

namespace SsmsToolkit.SsmsInterop
{
    /// <summary>Opens a new SSMS query window, connected like the source editor, and puts text in it.</summary>
    internal static class QueryWindowService
    {
        public static async Task OpenAsync(EditorConnection connection, string sql, string database)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var factory = ServiceCache.ScriptFactory ?? throw new InvalidOperationException("SSMS script factory is not available.");
            if (connection?.SsmsHandle is UIConnectionInfo ui)
            {
                var copy = ui.Copy();
                if (!string.IsNullOrEmpty(database))
                {
                    copy.AdvancedOptions["DATABASE"] = database;
                }
                factory.CreateNewBlankScript(ScriptType.Sql, copy, null);
            }
            else
            {
                factory.CreateNewBlankScript(ScriptType.Sql);
            }

            var dte = ServiceCache.ExtensibilityModel ?? throw new InvalidOperationException("DTE is not available.");
            if (dte.ActiveDocument?.Object("TextDocument") is EnvDTE.TextDocument doc)
            {
                doc.EndPoint.CreateEditPoint().Insert(sql ?? string.Empty);
            }
            else
            {
                Logger.Warn("New query window opened but its text document was not found.");
            }
        }
    }
}
