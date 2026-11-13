using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using SsmsToolkit.Core;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Presentation.TableInspector;
using SsmsToolkit.SsmsInterop;

namespace SsmsToolkit.Features.TableInspector
{
    /// <summary>IInspectorHost inside SSMS: FK navigation, "open in new query", clipboard, logging.</summary>
    internal sealed class VsInspectorHost : IInspectorHost
    {
        private readonly EditorConnection _connection;
        private readonly TableInspectorLauncher _launcher;
        private readonly FeatureRegistry _registry;

        public VsInspectorHost(EditorConnection connection, TableInspectorLauncher launcher, FeatureRegistry registry)
        {
            _connection = connection;
            _launcher = launcher;
            _registry = registry;
        }

        public async Task OpenTableAsync(ObjectName name)
        {
            try
            {
                await _launcher.InspectAsync(_connection, new[] { name }, name.ToString());
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(TableInspectorFeature.FeatureId, "OpenTable", ex);
            }
        }

        public async Task OpenQueryAsync(string sql, string database)
        {
            try
            {
                await QueryWindowService.OpenAsync(_connection, sql, database);
            }
            catch (Exception ex)
            {
                // SSMS-internal API: log and tell the user, but it does not break the inspector.
                Logger.Warn("Opening a new query window failed", ex);
                await UI.Notifications.StatusAsync(Presentation.Localization.Strings.Format("Msg_Failed", ex.Message));
            }
        }

        public void CopyToClipboard(string text)
        {
            // The clipboard can be locked by another process for a moment.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text ?? string.Empty, copy: true);
                    return;
                }
                catch (COMException) when (attempt < 4)
                {
                    System.Threading.Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Copy to clipboard failed", ex);
                    return;
                }
            }
        }

        public void ReportError(string context, Exception exception)
        {
            // Tab load errors (SQL errors, permissions, timeouts) are shown in the tab; only log them.
            Logger.Warn(context + " failed", exception);
        }
    }
}
