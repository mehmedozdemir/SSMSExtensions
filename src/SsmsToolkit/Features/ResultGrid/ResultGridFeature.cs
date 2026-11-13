using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.SqlServer.Management.UI.Grid;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using SsmsToolkit.Core;
using SsmsToolkit.Data.Grid;
using SsmsToolkit.Data.Parsing;
using SsmsToolkit.Package;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.ResultGrid;
using SsmsToolkit.SsmsInterop;
using SsmsToolkit.UI;

namespace SsmsToolkit.Features.ResultGrid
{
    /// <summary>
    /// SSMS results grid helpers: "Copy As" (CSV / JSON / Markdown / INSERT / IN list) in the grid context menu, and
    /// count / distinct / sum / average / min / max of the selection in the status bar.
    /// Off by default (ANALIZ §7): it relies on the SSMS grid control and its undocumented context menu id.
    /// </summary>
    internal sealed class ResultGridFeature : IFeature
    {
        public const string FeatureId = "result-grid";

        /// <summary>Copying more than this many cells is refused (clipboard text would be hundreds of MB).</summary>
        public const int MaxCopyCells = 500_000;

        /// <summary>Statistics stop after this many cells so a "select all" on a big result does not freeze the UI.</summary>
        public const int MaxStatisticsCells = 100_000;

        private readonly FeatureRegistry _registry;
        private DispatcherTimer _focusTimer;
        private DispatcherTimer _debounceTimer;
        private GridControl _grid;

        public ResultGridFeature(FeatureRegistry registry)
        {
            _registry = registry;
        }

        public string Id => FeatureId;

        public string DisplayName => Strings.Get("Feature_ResultGrid_Name");

        public string Description => Strings.Get("Feature_ResultGrid_Description");

        public bool EnabledByDefault => false;

        public async Task ActivateAsync()
        {
            // DispatcherTimers tick on the thread that creates them: the UI thread, where the grid lives.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // Grids are created per query execution; poll the focused control (cheap) to find the current one.
            _focusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
            _focusTimer.Tick += OnFocusTick;
            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
            _debounceTimer.Tick += OnDebounceTick;
            _focusTimer.Start();
        }

        public async Task DeactivateAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _focusTimer?.Stop();
            _debounceTimer?.Stop();
            _focusTimer = null;
            _debounceTimer = null;
            Track(null);
        }

        public object CreateSettingsViewModel(ToolkitSettings settings) => null;

        public void ApplySettingsViewModel(object viewModel, ToolkitSettings settings)
        {
        }

        /// <summary>Copies the focused grid's selection in <paramref name="format"/>. Never throws.</summary>
        public async Task CopyAsAsync(GridCopyFormat format)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var grid = ResultGridInterop.GetFocusedResultsGrid() ?? (ResultGridInterop.IsResultsGrid(_grid) ? _grid : null);
                long cells = grid == null ? 0 : ResultGridInterop.CountSelectedCells(grid);
                if (cells == 0)
                {
                    await Notifications.StatusAsync(Strings.Get("Msg_GridNoSelection"));
                    return;
                }
                if (cells > MaxCopyCells)
                {
                    await Notifications.StatusAsync(Strings.Format("Msg_GridTooLarge", cells.ToString("N0", CultureInfo.CurrentCulture), MaxCopyCells.ToString("N0", CultureInfo.CurrentCulture)));
                    return;
                }

                var data = ResultGridInterop.ReadSelection(grid);
                var text = GridSelectionFormatter.Format(data, format);
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), copy: true);
                Logger.Debug("Copied " + data.Rows.Count + " row(s) x " + data.Columns.Count + " column(s) as " + format);
                await Notifications.StatusAsync(Strings.Format("Msg_GridCopied", data.Rows.Count.ToString("N0", CultureInfo.CurrentCulture), FormatName(format)));
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(FeatureId, "Copy as " + format, ex);
                await Notifications.StatusAsync(Strings.Get("Msg_GridCopyFailed"));
            }
        }

        /// <summary>
        /// Opens the "INSERT statements" dialog for the focused grid: target table (suggested from the result schema and
        /// the query text), options, preview, and output to the clipboard, a new query window or a file. Never throws.
        /// </summary>
        public async Task ShowInsertDialogAsync(ToolkitPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var grid = ResultGridInterop.GetFocusedResultsGrid() ?? (ResultGridInterop.IsResultsGrid(_grid) ? _grid : null);
                var all = grid == null ? null : ResultGridInterop.CreateAllRowsSource(grid);
                if (all == null || all.RowCount == 0)
                {
                    await Notifications.StatusAsync(Strings.Get("Msg_GridNoSelection"));
                    return;
                }
                var selection = ResultGridInterop.CreateSelectionSource(grid);

                // The query editor that owns the grid: its text suggests table names, its moniker gives the connection.
                string moniker = null;
                var candidates = new List<string>();
                var fromSchema = InsertScriptBuilder.DefaultTableName(all.Columns);
                if (fromSchema != null)
                {
                    candidates.Add(fromSchema);
                }
                try
                {
                    var view = await ActiveEditor.GetActiveSqlViewAsync(package);
                    if (view != null)
                    {
                        var documents = (await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel)?.GetService<ITextDocumentFactoryService>();
                        moniker = ActiveEditor.GetDocumentMoniker(view, documents);
                        var script = view.TextSnapshot.GetText();
                        candidates.AddRange(SqlNameResolver.FindSelectSources(script).Select(InsertScriptBuilder.DisplayName));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("Table name suggestions unavailable: " + ex.Message);
                }

                var dialog = new InsertScriptDialog(async text =>
                {
                    var connection = await package.Connections.GetAsync(moniker);
                    await QueryWindowService.OpenAsync(connection, text, connection?.Database);
                });
                var vm = new InsertScriptViewModel(selection ?? all, selection == null ? null : all, candidates, dialog);
                dialog.Show(vm);
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(FeatureId, "INSERT dialog", ex);
                await Notifications.StatusAsync(Strings.Get("Msg_GridCopyFailed"));
            }
        }

        private static string FormatName(GridCopyFormat format)
        {
            switch (format)
            {
                case GridCopyFormat.Insert: return "INSERT";
                case GridCopyFormat.InList: return "IN (...)";
                case GridCopyFormat.Csv: return "CSV";
                case GridCopyFormat.Json: return "JSON";
                default: return format.ToString();
            }
        }

        private void OnFocusTick(object sender, EventArgs e)
        {
            try
            {
                var focused = ResultGridInterop.GetFocusedResultsGrid();
                if (focused != null && !ReferenceEquals(focused, _grid))
                {
                    Track(focused);
                    _debounceTimer?.Stop();
                    _debounceTimer?.Start();
                }
                else if (_grid != null && _grid.IsDisposed)
                {
                    Track(null);
                }
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(FeatureId, "grid focus tracking", ex);
            }
        }

        private void Track(GridControl grid)
        {
            if (_grid != null)
            {
                _grid.SelectionChanged -= OnSelectionChanged;
            }
            _grid = grid;
            if (_grid != null)
            {
                _grid.SelectionChanged += OnSelectionChanged;
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Restart the debounce: a mouse drag raises this for every cell.
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            _debounceTimer?.Stop();
            try
            {
                if (!ResultGridInterop.IsResultsGrid(_grid))
                {
                    return;
                }

                var stats = ResultGridInterop.ReadStatistics(_grid, MaxStatisticsCells);
                // A single cell needs no statistics; leave SSMS's own status text alone.
                if (stats == null || stats.Cells < 2)
                {
                    return;
                }

#pragma warning disable VSSDK007 // Status bar update on the UI thread; never throws.
                _ = Notifications.StatusAsync(stats.ToStatusText(StatisticsLabels.Instance, CultureInfo.CurrentCulture));
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                _registry.ReportFailure(FeatureId, "selection statistics", ex);
            }
        }

        private sealed class StatisticsLabels : IStatisticsLabels
        {
            public static readonly StatisticsLabels Instance = new StatisticsLabels();

            public string Count => Strings.Get("Stats_Count");

            public string Distinct => Strings.Get("Stats_Distinct");

            public string Sum => Strings.Get("Stats_Sum");

            public string Average => Strings.Get("Stats_Average");

            public string Min => Strings.Get("Stats_Min");

            public string Max => Strings.Get("Stats_Max");

            public string Truncated => Strings.Format("Stats_Truncated", MaxStatisticsCells.ToString("N0", CultureInfo.CurrentCulture));
        }
    }
}
