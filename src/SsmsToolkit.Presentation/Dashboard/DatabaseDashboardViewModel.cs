using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SsmsToolkit.Data.Dashboard;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Mvvm;

namespace SsmsToolkit.Presentation.Dashboard
{
    /// <summary>What the dashboard asks of its host (SSMS or the Playground).</summary>
    public interface IDashboardHost
    {
        Task InspectTableAsync(string schema, string name);

        void ReportError(string context, Exception exception);
    }

    /// <summary>
    /// Database dashboard: overview, storage, health checks and objects. Every section loads independently, so a
    /// slow or forbidden query only affects its own section.
    /// </summary>
    public sealed class DatabaseDashboardViewModel : ObservableObject, IDisposable
    {
        public const int TopTables = 100;
        public const int RecentCount = 30;
        public const int HistoryDays = 365;

        private readonly IDatabaseDashboardSource _source;
        private readonly IDashboardHost _host;
        private CancellationTokenSource _cts;
        private bool _isBusy;
        private string _error;
        private string _updatedText;
        private int _selectedTab;
        private DatabaseOverview _overview;
        private IReadOnlyList<Card> _cards = Array.Empty<Card>();
        private IReadOnlyList<PropertyRow> _properties = Array.Empty<PropertyRow>();
        private IReadOnlyList<FileRow> _files = Array.Empty<FileRow>();
        private IReadOnlyList<TableRow> _tables = Array.Empty<TableRow>();
        private string _tablesError;
        private IReadOnlyList<HistoryBar> _history = Array.Empty<HistoryBar>();
        private string _historyNote;
        private IReadOnlyList<FindingRow> _findings = Array.Empty<FindingRow>();
        private string _healthError;
        private bool _healthLoading;
        private IReadOnlyList<ObjectCount> _objectCounts = Array.Empty<ObjectCount>();
        private IReadOnlyList<RecentRow> _recent = Array.Empty<RecentRow>();
        private string _objectsError;

        public DatabaseDashboardViewModel(IDatabaseDashboardSource source, IDashboardHost host, bool isProduction)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            IsProduction = isProduction;
            RefreshCommand = new AsyncCommand(_ => RefreshAsync(), ex => _host.ReportError("Dashboard refresh", ex), _ => !IsBusy);
            OpenTableCommand = new AsyncCommand(OpenTableAsync, ex => _host.ReportError("Dashboard open table", ex));
            ShowHealthCommand = new RelayCommand(_ => SelectedTab = 2);
        }

        public string ServerName => _source.ServerName;

        public string Database => _source.Database;

        public string Title => Strings.Format("Dash_Title", _source.Database);

        public string Subtitle => _source.ServerName + "  ·  " + _source.Database;

        public bool IsProduction { get; }

        public string ProductionText => Strings.Format("Header_ProductionWarning", _source.ServerName);

        public ICommand RefreshCommand { get; }

        public ICommand OpenTableCommand { get; }

        public ICommand ShowHealthCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>The overview could not be read (no access, offline...): nothing else is shown.</summary>
        public string Error { get => _error; private set => Set(ref _error, value); }

        public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }

        public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }

        public DatabaseOverview Overview { get => _overview; private set => Set(ref _overview, value); }

        public IReadOnlyList<Card> Cards { get => _cards; private set => Set(ref _cards, value); }

        public IReadOnlyList<PropertyRow> Properties { get => _properties; private set => Set(ref _properties, value); }

        public IReadOnlyList<FileRow> Files { get => _files; private set => Set(ref _files, value); }

        public IReadOnlyList<TableRow> Tables { get => _tables; private set => Set(ref _tables, value); }

        public string TablesError { get => _tablesError; private set => Set(ref _tablesError, value); }

        public string TablesNote => Strings.Format("Dash_TablesNote", TopTables);

        public IReadOnlyList<HistoryBar> History { get => _history; private set => Set(ref _history, value); }

        public string HistoryNote { get => _historyNote; private set => Set(ref _historyNote, value); }

        public IReadOnlyList<FindingRow> Findings { get => _findings; private set => Set(ref _findings, value); }

        public string HealthError { get => _healthError; private set => Set(ref _healthError, value); }

        public bool HealthLoading { get => _healthLoading; private set => Set(ref _healthLoading, value); }

        public IReadOnlyList<ObjectCount> ObjectCounts { get => _objectCounts; private set => Set(ref _objectCounts, value); }

        public IReadOnlyList<RecentRow> Recent { get => _recent; private set => Set(ref _recent, value); }

        public string ObjectsError { get => _objectsError; private set => Set(ref _objectsError, value); }

        public Task StartAsync() => RefreshAsync();

        public async Task RefreshAsync()
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            var ct = cts.Token;
            IsBusy = true;
            Error = null;
            try
            {
                IReadOnlyList<DatabaseFileInfo> files;
                try
                {
                    var result = await _source.GetOverviewAsync(ct);
                    Overview = result.Overview;
                    files = result.Files;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Error = Strings.Format("Dash_Failed", ex.Message);
                    return;
                }

                Files = files.Select(f => new FileRow(f)).ToList();
                Properties = BuildProperties(Overview);
                Cards = BuildCards(Overview, files, null);

                // Sections are independent; run them together.
                await Task.WhenAll(LoadTablesAsync(ct), LoadHistoryAsync(ct), LoadObjectsAsync(ct), LoadHealthAsync(files, ct));
                UpdatedText = Strings.Format("Dash_Updated", DateTime.Now.ToString("T", CultureInfo.CurrentCulture));
            }
            catch (OperationCanceledException)
            {
                // A newer refresh or the window closing cancelled this one.
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                {
                    IsBusy = false;
                }
            }
        }

        private async Task LoadTablesAsync(CancellationToken ct)
        {
            try
            {
                var tables = await _source.GetTableSizesAsync(TopTables, ct);
                decimal max = tables.Count == 0 ? 0 : tables.Max(t => t.ReservedMb);
                Tables = tables.Select(t => new TableRow(t, max)).ToList();
                TablesError = null;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                TablesError = Strings.Format("Dash_Failed", ex.Message);
            }
        }

        private async Task LoadHistoryAsync(CancellationToken ct)
        {
            try
            {
                var points = await _source.GetSizeHistoryAsync(HistoryDays, ct);
                decimal max = points.Count == 0 ? 0 : points.Max(p => p.SizeMb);
                History = points.Select(p => new HistoryBar(p, max)).ToList();
                HistoryNote = points.Count == 0
                    ? Strings.Get("Dash_HistoryEmpty")
                    : Strings.Format("Dash_HistoryRange", FormatMb(points.First().SizeMb), FormatMb(points.Last().SizeMb), points.Count);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                History = Array.Empty<HistoryBar>();
                HistoryNote = Strings.Format("Dash_HistoryUnavailable", ex.Message);
            }
        }

        private async Task LoadObjectsAsync(CancellationToken ct)
        {
            try
            {
                ObjectCounts = await _source.GetObjectCountsAsync(ct);
                Recent = (await _source.GetRecentChangesAsync(RecentCount, ct)).Select(r => new RecentRow(r)).ToList();
                ObjectsError = null;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                ObjectsError = Strings.Format("Dash_Failed", ex.Message);
            }
        }

        private async Task LoadHealthAsync(IReadOnlyList<DatabaseFileInfo> files, CancellationToken ct)
        {
            HealthLoading = true;
            try
            {
                var findings = await _source.GetHealthAsync(Overview, files, ct);
                Findings = findings.Select(f => new FindingRow(f)).ToList();
                HealthError = null;
                Cards = BuildCards(Overview, files, findings);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                HealthError = Strings.Format("Dash_Failed", ex.Message);
            }
            finally
            {
                HealthLoading = false;
            }
        }

        private async Task OpenTableAsync(object parameter)
        {
            switch (parameter)
            {
                case TableRow t:
                    await _host.InspectTableAsync(t.Schema, t.Name);
                    break;
                case RecentRow r when r.IsTableOrView:
                    await _host.InspectTableAsync(r.Schema, r.Name);
                    break;
            }
        }

        private static IReadOnlyList<Card> BuildCards(DatabaseOverview db, IReadOnlyList<DatabaseFileInfo> files, IReadOnlyList<HealthFinding> findings)
        {
            var data = files.Where(f => !f.IsLog).ToList();
            var log = files.Where(f => f.IsLog).ToList();
            var cards = new List<Card>
            {
                new Card(Strings.Get("Dash_Card_Size"), FormatMb(files.Sum(f => f.SizeMb)), Strings.Format("Dash_Card_Files", files.Count)),
                UsageCard(Strings.Get("Dash_Card_Data"), data),
                UsageCard(Strings.Get("Dash_Card_Log"), log),
                new Card(Strings.Get("Dash_Card_Recovery"), db.RecoveryModel, db.State),
                new Card(Strings.Get("Dash_Card_Compat"), db.CompatibilityLevel.ToString(CultureInfo.InvariantCulture),
                    db.ServerCompatibilityLevel > 0 ? Strings.Format("Dash_Card_ServerCompat", db.ServerCompatibilityLevel) : null),
            };

            if (findings == null)
            {
                cards.Add(new Card(Strings.Get("Dash_Card_Health"), "…", Strings.Get("Common_Loading")) { IsHealth = true });
            }
            else
            {
                int critical = findings.Count(f => f.Severity == HealthSeverity.Critical);
                int warnings = findings.Count(f => f.Severity == HealthSeverity.Warning);
                var severity = critical > 0 ? HealthSeverity.Critical : warnings > 0 ? HealthSeverity.Warning : HealthSeverity.Ok;
                cards.Add(new Card(Strings.Get("Dash_Card_Health"),
                    critical + warnings == 0 ? Strings.Get("Dash_Health_AllGood") : Strings.Format("Dash_Health_Summary", critical, warnings),
                    Strings.Get("Dash_Health_Open")) { IsHealth = true, Severity = severity });
            }
            return cards;
        }

        private static Card UsageCard(string title, IReadOnlyList<DatabaseFileInfo> files)
        {
            decimal size = files.Sum(f => f.SizeMb);
            var used = files.All(f => f.UsedMb != null) ? files.Sum(f => f.UsedMb.Value) : (decimal?)null;
            if (used == null || size <= 0)
            {
                return new Card(title, FormatMb(size), null);
            }
            double percent = (double)(used.Value / size * 100m);
            return new Card(title, percent.ToString("0", CultureInfo.CurrentCulture) + " %", Strings.Format("Dash_Card_UsedOf", FormatMb(used.Value), FormatMb(size)))
            {
                Percent = percent,
            };
        }

        private static IReadOnlyList<PropertyRow> BuildProperties(DatabaseOverview db)
        {
            string YesNo(bool b) => Strings.Get(b ? "Common_Yes" : "Common_No");
            return new[]
            {
                new PropertyRow(Strings.Get("Dash_Prop_State"), db.State),
                new PropertyRow(Strings.Get("Dash_Prop_Owner"), db.Owner),
                new PropertyRow(Strings.Get("Dash_Prop_Created"), db.CreateDate.ToString("g", CultureInfo.CurrentCulture)),
                new PropertyRow(Strings.Get("Col_Collation"), db.Collation),
                new PropertyRow(Strings.Get("Dash_Prop_UserAccess"), db.UserAccess),
                new PropertyRow(Strings.Get("Dash_Prop_ReadOnly"), YesNo(db.IsReadOnly)),
                new PropertyRow("Snapshot isolation", db.SnapshotIsolation),
                new PropertyRow("Read committed snapshot", YesNo(db.IsReadCommittedSnapshotOn)),
                new PropertyRow("Query Store", YesNo(db.IsQueryStoreOn)),
                new PropertyRow("Change data capture", YesNo(db.IsCdcEnabled)),
                new PropertyRow("Page verify", db.PageVerify),
                new PropertyRow("Auto close / Auto shrink", YesNo(db.IsAutoCloseOn) + " / " + YesNo(db.IsAutoShrinkOn)),
                new PropertyRow(Strings.Get("Dash_Prop_AutoStats"), YesNo(db.IsAutoCreateStatsOn) + " / " + YesNo(db.IsAutoUpdateStatsOn)),
                new PropertyRow("Log reuse wait", db.LogReuseWait),
                new PropertyRow("SQL Server", db.ProductVersion + " — " + db.Edition),
            };
        }

        /// <summary>"512 MB", "1,5 GB", "2,1 TB" in the current culture.</summary>
        public static string FormatMb(decimal mb)
        {
            var culture = CultureInfo.CurrentCulture;
            if (mb >= 1024m * 1024m)
            {
                return (mb / 1024m / 1024m).ToString("0.##", culture) + " TB";
            }
            if (mb >= 1024m)
            {
                return (mb / 1024m).ToString("0.#", culture) + " GB";
            }
            return mb.ToString(mb < 10 ? "0.##" : "0", culture) + " MB";
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    public sealed class Card
    {
        public Card(string title, string value, string detail)
        {
            Title = title;
            Value = value;
            Detail = detail;
        }

        public string Title { get; }
        public string Value { get; }
        public string Detail { get; }

        /// <summary>0..100 for a usage bar, or null.</summary>
        public double? Percent { get; set; }

        public bool HasPercent => Percent != null;

        public bool IsHealth { get; set; }

        public HealthSeverity Severity { get; set; } = HealthSeverity.Info;
    }

    public sealed class PropertyRow
    {
        public PropertyRow(string property, string value)
        {
            Property = property;
            Value = value;
        }

        public string Property { get; }
        public string Value { get; }
    }

    public sealed class FileRow
    {
        public FileRow(DatabaseFileInfo f)
        {
            Name = f.LogicalName;
            Type = f.Type;
            Path = f.PhysicalName;
            SizeMb = f.SizeMb;
            UsedPercent = f.UsedPercent;
            Size = DatabaseDashboardViewModel.FormatMb(f.SizeMb);
            Used = f.UsedMb == null ? "—" : DatabaseDashboardViewModel.FormatMb(f.UsedMb.Value) + " (" + f.UsedPercent.Value.ToString("0", CultureInfo.CurrentCulture) + " %)";
            Growth = f.Growth == 0 ? Strings.Get("Dash_NoGrowth")
                : f.IsPercentGrowth ? f.Growth.ToString("0", CultureInfo.CurrentCulture) + " %" : DatabaseDashboardViewModel.FormatMb(f.Growth);
            Max = f.MaxSizeMb == null ? Strings.Get("Dash_Unlimited") : DatabaseDashboardViewModel.FormatMb(f.MaxSizeMb.Value);
        }

        public string Name { get; }
        public string Type { get; }
        public string Path { get; }
        public decimal SizeMb { get; }
        public double? UsedPercent { get; }
        public string Size { get; }
        public string Used { get; }
        public string Growth { get; }
        public string Max { get; }
    }

    public sealed class TableRow
    {
        public TableRow(TableSizeInfo t, decimal maxReserved)
        {
            Schema = t.Schema;
            Name = t.Name;
            Rows = t.Rows;
            ReservedMb = t.ReservedMb;
            DataMb = t.DataMb;
            IndexMb = t.IndexMb;
            UnusedMb = t.UnusedMb;
            Share = maxReserved <= 0 ? 0 : (double)(t.ReservedMb / maxReserved);
        }

        public string Schema { get; }
        public string Name { get; }
        public string FullName => Schema + "." + Name;
        public long Rows { get; }
        public decimal ReservedMb { get; }
        public decimal DataMb { get; }
        public decimal IndexMb { get; }
        public decimal UnusedMb { get; }

        /// <summary>0..1 of the largest table, for the inline bar.</summary>
        public double Share { get; }
    }

    public sealed class HistoryBar
    {
        public HistoryBar(SizeHistoryPoint p, decimal max)
        {
            Date = p.Date;
            SizeMb = p.SizeMb;
            Height = max <= 0 ? 0 : Math.Max(2, (double)(p.SizeMb / max) * 120);
            ToolTip = p.Date.ToString("d", CultureInfo.CurrentCulture) + ": " + DatabaseDashboardViewModel.FormatMb(p.SizeMb);
        }

        public DateTime Date { get; }
        public decimal SizeMb { get; }

        /// <summary>Bar height in pixels (chart is 120 px tall).</summary>
        public double Height { get; }
        public string ToolTip { get; }
    }

    public sealed class FindingRow
    {
        public FindingRow(HealthFinding f)
        {
            Severity = f.Severity;
            Title = Strings.Get("Health_" + f.CheckId);
            Message = f.Severity == HealthSeverity.Unavailable
                ? Strings.Format("Health_Unavailable", f.Error)
                : Strings.Format("Health_" + f.MessageId, f.Args.Select(FormatArg).ToArray());
            Items = f.Items;
            More = f.ItemCount > f.Items.Count ? Strings.Format("Health_More", f.ItemCount - f.Items.Count) : null;
        }

        /// <summary>"3 days", "5 hours", "12 minutes" (localized).</summary>
        public static string FormatAge(TimeSpan age)
        {
            if (age.TotalDays >= 1)
            {
                return Strings.Format("Dash_Age_Days", (int)age.TotalDays);
            }
            if (age.TotalHours >= 1)
            {
                return Strings.Format("Dash_Age_Hours", (int)age.TotalHours);
            }
            return Strings.Format("Dash_Age_Minutes", Math.Max(0, (int)age.TotalMinutes));
        }

        private static object FormatArg(object arg)
        {
            switch (arg)
            {
                case null: return "—";
                case TimeSpan age: return FormatAge(age);
                default: return arg;
            }
        }

        public HealthSeverity Severity { get; }
        public string Title { get; }
        public string Message { get; }
        public IReadOnlyList<string> Items { get; }
        public bool HasItems => Items.Count > 0;
        public string More { get; }
    }

    public sealed class RecentRow
    {
        public RecentRow(RecentObjectChange r)
        {
            Schema = r.Schema;
            Name = r.Name;
            TypeDesc = r.TypeDesc;
            Created = r.Created;
            Modified = r.Modified;
            IsTableOrView = r.IsTableOrView;
        }

        public string Schema { get; }
        public string Name { get; }
        public string FullName => Schema + "." + Name;
        public string TypeDesc { get; }
        public DateTime Created { get; }
        public DateTime Modified { get; }
        public bool IsTableOrView { get; }
    }
}
