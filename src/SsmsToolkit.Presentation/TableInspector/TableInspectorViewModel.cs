using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Mvvm;

namespace SsmsToolkit.Presentation.TableInspector
{
    /// <summary>One inspected table or view. Dispose cancels everything still running.</summary>
    public sealed class TableInspectorViewModel : ObservableObject, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IInspectorHost _host;
        private InspectorTab _selectedTab;

        public TableInspectorViewModel(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, InspectorOptions options)
        {
            Object = obj ?? throw new ArgumentNullException(nameof(obj));
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _host = host ?? throw new ArgumentNullException(nameof(host));
            options = options ?? new InspectorOptions();

            ServerName = source.ServerName;
            IsProduction = options.IsProduction;

            Action<string, Exception> onError = (ctx, ex) => _host.ReportError("Table Inspector/" + ctx, ex);
            var token = _lifetime.Token;

            Summary = new SummaryTab(obj, source, token, onError);
            Columns = new ColumnsTab(obj, source, host, token, onError);
            Keys = new KeysTab(obj, source, host, token, onError);
            Indexes = new IndexesTab(obj, source, host, token, onError);
            Constraints = new ConstraintsTab(obj, source, host, token, onError);
            ForeignKeys = new ForeignKeysTab(obj, source, host, token, onError);
            Data = new DataTab(obj, source, host, options, token, onError);
            Ddl = new DdlTab(obj, source, host, token, onError);
            Tabs = new InspectorTab[] { Summary, Columns, Keys, Indexes, Constraints, ForeignKeys, Data, Ddl };

            foreach (var t in Tabs)
            {
                t.StateChanged += (s, e) => OnPropertyChanged(nameof(IsBusy));
            }

            CancelCommand = new RelayCommand(_ => CancelAll(), _ => IsBusy);
            RefreshCommand = new AsyncCommand(_ => RefreshAsync(), ex => onError("Refresh", ex), _ => !IsBusy);
            _selectedTab = Columns;
        }

        public ResolvedObject Object { get; }

        public string ServerName { get; }

        public string Title => Object.Schema + "." + Object.Name;

        public string Subtitle =>
            (Object.Type == InspectableObjectType.View ? Strings.Get("Header_View") : Strings.Get("Header_Table"))
            + " · " + ServerName + " · " + Object.Database;

        public bool IsProduction { get; }

        public string ProductionText => Strings.Format("Header_ProductionWarning", ServerName);

        public SummaryTab Summary { get; }
        public ColumnsTab Columns { get; }
        public KeysTab Keys { get; }
        public IndexesTab Indexes { get; }
        public ConstraintsTab Constraints { get; }
        public ForeignKeysTab ForeignKeys { get; }
        public DataTab Data { get; }
        public DdlTab Ddl { get; }

        public IReadOnlyList<InspectorTab> Tabs { get; }

        public InspectorTab SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (Set(ref _selectedTab, value) && value != null)
                {
                    _ = SafeAsync(value.EnsureLoadedAsync());
                }
            }
        }

        public bool IsBusy => Tabs.Any(t => t.IsLoading);

        public ICommand CancelCommand { get; }

        public ICommand RefreshCommand { get; }

        /// <summary>Loads the cheap metadata tabs in parallel; Data and DDL load when first selected.</summary>
        public Task StartAsync()
        {
            return Task.WhenAll(
                SafeAsync(Summary.EnsureLoadedAsync()),
                SafeAsync(Columns.EnsureLoadedAsync()),
                SafeAsync(Keys.EnsureLoadedAsync()),
                SafeAsync(Indexes.EnsureLoadedAsync()),
                SafeAsync(Constraints.EnsureLoadedAsync()),
                SafeAsync(ForeignKeys.EnsureLoadedAsync()));
        }

        public Task RefreshAsync()
        {
            var loaded = Tabs.Where(t => t.State != TabState.NotLoaded || t == SelectedTab).Select(t => SafeAsync(t.ReloadAsync()));
            return Task.WhenAll(loaded);
        }

        public void CancelAll()
        {
            foreach (var t in Tabs)
            {
                t.Cancel();
            }
        }

        public void Dispose()
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _lifetime.Cancel();
            }
            _lifetime.Dispose();
        }

        private async Task SafeAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                _host.ReportError("Table Inspector", ex);
            }
        }
    }
}
