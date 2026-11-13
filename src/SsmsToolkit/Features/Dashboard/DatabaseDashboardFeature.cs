using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsToolkit.Core;
using SsmsToolkit.Data.Dashboard;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;
using SsmsToolkit.Data.Text;
using SsmsToolkit.Features.TableInspector;
using SsmsToolkit.Presentation.Dashboard;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.SsmsInterop;
using SsmsToolkit.UI;

namespace SsmsToolkit.Features.Dashboard
{
    /// <summary>
    /// Database dashboard (read-only): Object Explorer "Database Dashboard" on a database node, or the
    /// Extensions > SsmsToolkit command for the Object Explorer selection / the active query window's database.
    /// One tab per server + database; opening it again brings the tab to the front.
    /// </summary>
    internal sealed class DatabaseDashboardFeature : IFeature
    {
        public const string FeatureId = "database-dashboard";

        private readonly AsyncPackage _package;
        private readonly FeatureRegistry _registry;
        private readonly Func<TableInspectorLauncher> _inspector;
        private readonly Dictionary<string, DashboardToolWindow> _tabs = new Dictionary<string, DashboardToolWindow>(StringComparer.OrdinalIgnoreCase);
        private int _nextId;

        public DatabaseDashboardFeature(AsyncPackage package, FeatureRegistry registry, Func<TableInspectorLauncher> inspector)
        {
            _package = package;
            _registry = registry;
            _inspector = inspector;
        }

        public string Id => FeatureId;

        public string DisplayName => Strings.Get("Feature_Dashboard_Name");

        public string Description => Strings.Get("Feature_Dashboard_Description");

        public bool EnabledByDefault => true;

        public Task ActivateAsync() => Task.CompletedTask;

        public Task DeactivateAsync() => Task.CompletedTask;

        public object CreateSettingsViewModel(ToolkitSettings settings) => null;

        public void ApplySettingsViewModel(object viewModel, ToolkitSettings settings)
        {
        }

        /// <summary>Opens (or brings to front) the dashboard of <paramref name="connection"/>'s database. Never throws.</summary>
        public async Task OpenAsync(EditorConnection connection)
        {
            try
            {
                if (connection == null || string.IsNullOrEmpty(connection.Database))
                {
                    await Notifications.StatusAsync(Strings.Get("Msg_NoDatabase"));
                    return;
                }
                if (!connection.CanOpenOwnConnection)
                {
                    await Notifications.StatusAsync(Strings.Format("Msg_AuthUnsupported", connection.AuthenticationDescription));
                    return;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                var key = connection.ServerName + "|" + connection.Database;
                if (_tabs.TryGetValue(key, out var existing) && existing.Frame is IVsWindowFrame existingFrame)
                {
                    existingFrame.Show();
                    return;
                }

                var settings = SettingsStore.Current;
                var source = new SqlDatabaseDashboardSource(
                    new ConnectionTarget(connection.OwnConnectionString, connection.ServerName, connection.Database),
                    settings.TableInspector.CommandTimeoutSeconds);
                bool isProduction = WildcardMatcher.MatchesAny(connection.ServerName, settings.ProductionServerPatterns);
                var vm = new DatabaseDashboardViewModel(source, new Host(this, connection), isProduction);
                var view = new DatabaseDashboardView { DataContext = vm };
                VsTheme.UseVsScrollBars(view);

                var id = Interlocked.Increment(ref _nextId);
                var pane = (DashboardToolWindow)await _package.FindToolWindowAsync(typeof(DashboardToolWindow), id, create: true, _package.DisposalToken);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                if (!(pane?.Frame is IVsWindowFrame frame))
                {
                    throw new InvalidOperationException("Could not create the dashboard tool window.");
                }

                pane.Initialize(vm, view, () =>
                {
                    _tabs.Remove(key);
                    vm.Dispose();
                });
                _tabs[key] = pane;
                ErrorHandler.ThrowOnFailure(frame.Show());
                Logger.Info("Dashboard opened: " + connection.ServerName + "/" + connection.Database);

                await vm.StartAsync();
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(FeatureId, "Open dashboard", ex);
            }
        }

        private sealed class Host : IDashboardHost
        {
            private readonly DatabaseDashboardFeature _feature;
            private readonly EditorConnection _connection;

            public Host(DatabaseDashboardFeature feature, EditorConnection connection)
            {
                _feature = feature;
                _connection = connection;
            }

            public async Task InspectTableAsync(string schema, string name)
            {
                if (!_feature._registry.IsActive(TableInspectorFeature.FeatureId))
                {
                    return;
                }
                var target = new ObjectName(null, _connection.Database, schema, name);
                await _feature._inspector().InspectAsync(_connection, new[] { target }, target.ToString());
            }

            public void ReportError(string context, Exception exception) =>
                _feature._registry.ReportFailure(FeatureId, context, exception);
        }
    }
}
