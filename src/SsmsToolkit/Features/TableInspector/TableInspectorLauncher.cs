using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using SsmsToolkit.Core;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Parsing;
using SsmsToolkit.Data.Sql;
using SsmsToolkit.Data.Text;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.SsmsInterop;
using SsmsToolkit.UI;

namespace SsmsToolkit.Features.TableInspector
{
    /// <summary>
    /// Editor text + caret → table name → own connection → resolve → open the inspector window.
    /// Expected problems (no name, not found, SQL errors) go to the status bar; unexpected ones count as feature failures.
    /// </summary>
    internal sealed class TableInspectorLauncher
    {
        private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(30);

        private readonly SsmsConnectionProvider _connections;
        private readonly InspectorWindowManager _windows;
        private readonly FeatureRegistry _registry;

        public TableInspectorLauncher(SsmsConnectionProvider connections, InspectorWindowManager windows, FeatureRegistry registry)
        {
            _connections = connections;
            _windows = windows;
            _registry = registry;
        }

        /// <param name="scriptText">Full editor text (captured on the UI thread).</param>
        /// <param name="caretOffset">Character offset of the click or caret.</param>
        /// <param name="documentMoniker">Editor document path, used to find that editor's connection.</param>
        public async Task InspectAtAsync(string scriptText, int caretOffset, string documentMoniker)
        {
            try
            {
                var connection = await _connections.GetAsync(documentMoniker);
                if (connection == null)
                {
                    await Notifications.StatusAsync(Strings.Get("Msg_NoConnection"));
                    return;
                }

                await TaskScheduler.Default;
                var resolution = SqlNameResolver.Resolve(scriptText, caretOffset);
                Logger.Debug("Resolved '" + resolution.ClickedText + "' → [" + string.Join(", ", resolution.Candidates) + "] parser=" + resolution.UsedParser);
                if (resolution.IsEmpty)
                {
                    await Notifications.StatusAsync(Strings.Get("Msg_NoName"));
                    return;
                }

                await InspectAsync(connection, resolution.Candidates, resolution.ClickedText);
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(TableInspectorFeature.FeatureId, "InspectAt", ex);
            }
        }

        /// <summary>Object Explorer: context menu item or Inspect Table command while Object Explorer has focus.</summary>
        public async Task InspectObjectExplorerAsync(ObjectExplorerTarget target)
        {
            try
            {
                if (target?.Connection == null)
                {
                    await Notifications.StatusAsync(Strings.Get("Msg_NoName"));
                    return;
                }

                await InspectAsync(target.Connection, new[] { target.Name }, target.Name.ToString());
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(TableInspectorFeature.FeatureId, "InspectObjectExplorer", ex);
            }
        }

        /// <summary>Resolves the candidates on the editor's server and opens the first table/view found.</summary>
        public async Task InspectAsync(EditorConnection connection, IReadOnlyList<ObjectName> candidates, string displayName)
        {
            if (!connection.CanOpenOwnConnection)
            {
                await Notifications.StatusAsync(Strings.Format("Msg_AuthUnsupported", connection.AuthenticationDescription));
                return;
            }

            await Notifications.StatusAsync(Strings.Format("Msg_Inspecting", displayName));
            await TaskScheduler.Default;

            var settings = SettingsStore.Current.TableInspector;
            var source = new SqlTableMetadataSource(
                new ConnectionTarget(connection.OwnConnectionString, connection.ServerName, connection.Database),
                new MetadataOptions { CommandTimeoutSeconds = settings.CommandTimeoutSeconds, ReadUncommitted = settings.ReadUncommitted });

            ResolveResult result;
            try
            {
                using (var cts = new CancellationTokenSource(ResolveTimeout))
                {
                    result = await source.ResolveAsync(candidates, cts.Token);
                }
            }
            catch (Exception ex) when (ex is SqlException || ex is OperationCanceledException || ex is InvalidOperationException)
            {
                // Login failed, database offline, timeout...: expected, not a feature failure.
                Logger.Warn("Resolving '" + displayName + "' failed on " + connection.ServerName, ex);
                await Notifications.StatusAsync(Strings.Format("Msg_Failed", ex.Message));
                return;
            }

            switch (result.Status)
            {
                case ResolveStatus.Found:
                    await Notifications.StatusAsync(string.Empty);
                    bool isProduction = WildcardMatcher.MatchesAny(connection.ServerName, SettingsStore.Current.ProductionServerPatterns);
                    await _windows.ShowAsync(connection, source, result.Object, isProduction, this);
                    break;
                case ResolveStatus.NotATableOrView:
                    await Notifications.StatusAsync(Strings.Format("Msg_NotTable", displayName, result.Detail));
                    break;
                case ResolveStatus.Unsupported:
                    await Notifications.StatusAsync(UnsupportedMessage(result.Detail));
                    break;
                default:
                    await Notifications.StatusAsync(Strings.Format("Msg_NotFound", displayName));
                    break;
            }
        }

        private static string UnsupportedMessage(string reason)
        {
            switch (reason)
            {
                case "temp-table":
                case "table-variable":
                    return Strings.Get("Msg_TempUnsupported");
                case "linked-server":
                    return Strings.Get("Msg_LinkedServerUnsupported");
                default:
                    return Strings.Format("Msg_Unsupported", reason);
            }
        }
    }
}
