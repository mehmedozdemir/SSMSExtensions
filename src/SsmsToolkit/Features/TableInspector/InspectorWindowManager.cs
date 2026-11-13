using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsToolkit.Core;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Presentation.Settings;
using SsmsToolkit.Presentation.TableInspector;
using SsmsToolkit.Presentation.Theme;
using SsmsToolkit.SsmsInterop;

namespace SsmsToolkit.Features.TableInspector
{
    /// <summary>
    /// Opens inspectors as document tabs (multi-instance tool windows) or floating windows.
    /// The same table on the same server is brought to front instead of opened twice.
    /// </summary>
    internal sealed class InspectorWindowManager
    {
        private readonly AsyncPackage _package;
        private readonly FeatureRegistry _registry;
        private readonly Dictionary<string, TableInspectorToolWindow> _tabs = new Dictionary<string, TableInspectorToolWindow>();
        private readonly Dictionary<string, Window> _popups = new Dictionary<string, Window>();
        private int _nextId;

        public InspectorWindowManager(AsyncPackage package, FeatureRegistry registry)
        {
            _package = package;
            _registry = registry;
        }

        public async Task ShowAsync(EditorConnection connection, ITableMetadataSource source, ResolvedObject obj, bool isProduction, TableInspectorLauncher launcher)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            var key = obj.Key(connection.ServerName);
            var settings = SettingsStore.Current.TableInspector;

            if (_tabs.TryGetValue(key, out var existingTab) && existingTab.Frame is IVsWindowFrame existingFrame)
            {
                existingFrame.Show();
                return;
            }
            if (_popups.TryGetValue(key, out var existingPopup))
            {
                existingPopup.Activate();
                return;
            }

            var host = new VsInspectorHost(connection, launcher, _registry);
            var vm = new TableInspectorViewModel(obj, source, host, new InspectorOptions
            {
                RowLimit = settings.RowLimit,
                ReadUncommitted = settings.ReadUncommitted,
                IsProduction = isProduction,
            });
            var view = new TableInspectorView { DataContext = vm };

            if (settings.OpenMode == InspectorOpenMode.Popup)
            {
                ShowPopup(key, vm, view);
            }
            else
            {
                await ShowTabAsync(key, vm, view);
            }

            Logger.Info("Inspector opened: " + obj + " on " + connection.ServerName + "/" + obj.Database);
            _ = SafeStartAsync(vm);
        }

        private async Task ShowTabAsync(string key, TableInspectorViewModel vm, TableInspectorView view)
        {
            var id = Interlocked.Increment(ref _nextId);
            var pane = (TableInspectorToolWindow)await _package.FindToolWindowAsync(typeof(TableInspectorToolWindow), id, create: true, _package.DisposalToken);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            if (pane?.Frame is IVsWindowFrame frame)
            {
                pane.Initialize(vm, view, () =>
                {
                    _tabs.Remove(key);
                    vm.Dispose();
                });
                _tabs[key] = pane;
                ErrorHandler.ThrowOnFailure(frame.Show());
            }
            else
            {
                throw new InvalidOperationException("Could not create the Table Inspector tool window.");
            }
        }

        private void ShowPopup(string key, TableInspectorViewModel vm, TableInspectorView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = new Window
            {
                Title = vm.Title + " — " + vm.ServerName,
                Content = view,
                Width = 1000,
                Height = 640,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            window.SetResourceReference(Window.BackgroundProperty, ThemeKeys.Background);

            if (Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell && shell.GetDialogOwnerHwnd(out var owner) == VSConstants.S_OK)
            {
                new WindowInteropHelper(window) { Owner = owner };
            }

            window.Closed += (s, e) =>
            {
                _popups.Remove(key);
                vm.Dispose();
            };
            _popups[key] = window;
            window.Show();
        }

        private async Task SafeStartAsync(TableInspectorViewModel vm)
        {
            try
            {
                await vm.StartAsync();
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(TableInspectorFeature.FeatureId, "StartAsync", ex);
            }
        }
    }
}
