using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Mvvm;

namespace SsmsToolkit.Presentation.TableInspector
{
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

    public sealed class SummaryTab : InspectorTab
    {
        private readonly ResolvedObject _obj;
        private readonly ITableMetadataSource _source;

        public SummaryTab(ResolvedObject obj, ITableMetadataSource source, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Summary"), lifetime, onError)
        {
            _obj = obj;
            _source = source;
        }

        public ObservableCollection<PropertyRow> Rows { get; } = new ObservableCollection<PropertyRow>();

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var s = await _source.GetSummaryAsync(_obj, ct);
            var c = Strings.IsTurkish ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.GetCultureInfo("en-US");

            Rows.Clear();
            Rows.Add(new PropertyRow(Strings.Get("Prop_Server"), _source.ServerName));
            Rows.Add(new PropertyRow(Strings.Get("Prop_Database"), _obj.Database));
            Rows.Add(new PropertyRow(Strings.Get("Prop_Schema"), s.Schema));
            Rows.Add(new PropertyRow(Strings.Get("Prop_Name"), s.Name));
            Rows.Add(new PropertyRow(Strings.Get("Prop_Type"), s.TypeDescription));
            if (s.SynonymName != null)
            {
                Rows.Add(new PropertyRow(Strings.Get("Prop_Synonym"), s.SynonymName));
            }
            Rows.Add(new PropertyRow(Strings.Get("Prop_Rows"), s.ApproximateRowCount?.ToString("N0", c) ?? "-"));
            Rows.Add(new PropertyRow(Strings.Get("Prop_TotalSize"), FormatKb(s.TotalSizeKb, c)));
            Rows.Add(new PropertyRow(Strings.Get("Prop_UsedSize"), FormatKb(s.UsedSizeKb, c)));
            Rows.Add(new PropertyRow(Strings.Get("Prop_ColumnCount"), s.ColumnCount.ToString(c)));
            Rows.Add(new PropertyRow(Strings.Get("Prop_IndexCount"), s.IndexCount.ToString(c)));
            Rows.Add(new PropertyRow(Strings.Get("Prop_Created"), s.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            Rows.Add(new PropertyRow(Strings.Get("Prop_Modified"), s.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            Rows.Add(new PropertyRow(Strings.Get("Prop_Description"), s.Description ?? string.Empty));
        }

        internal static string FormatKb(long? kb, CultureInfo c)
        {
            if (kb == null)
            {
                return "-";
            }
            if (kb < 1024)
            {
                return kb.Value.ToString("N0", c) + " KB";
            }
            if (kb < 1024 * 1024)
            {
                return (kb.Value / 1024d).ToString("N1", c) + " MB";
            }
            return (kb.Value / 1024d / 1024d).ToString("N2", c) + " GB";
        }
    }

    public sealed class ColumnsTab : InspectorTab
    {
        private readonly ResolvedObject _obj;
        private readonly ITableMetadataSource _source;

        public ColumnsTab(ResolvedObject obj, ITableMetadataSource source, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Columns"), lifetime, onError)
        {
            _obj = obj;
            _source = source;
        }

        public ObservableCollection<ColumnInfo> Columns { get; } = new ObservableCollection<ColumnInfo>();

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var cols = await _source.GetColumnsAsync(_obj, ct);
            Columns.Clear();
            foreach (var c in cols)
            {
                Columns.Add(c);
            }
        }
    }

    public sealed class IndexRow
    {
        public IndexRow(IndexInfo i)
        {
            Name = i.Name;
            Kind = i.Kind;
            Type = i.TypeDescription;
            KeyColumns = string.Join(", ", i.KeyColumns);
            IncludedColumns = string.Join(", ", i.IncludedColumns);
            Filter = i.FilterDefinition;
            IsDisabled = i.IsDisabled;
        }

        public string Name { get; }
        public string Kind { get; }
        public string Type { get; }
        public string KeyColumns { get; }
        public string IncludedColumns { get; }
        public string Filter { get; }
        public bool IsDisabled { get; }
    }

    public sealed class IndexesTab : InspectorTab
    {
        private readonly ResolvedObject _obj;
        private readonly ITableMetadataSource _source;

        public IndexesTab(ResolvedObject obj, ITableMetadataSource source, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Indexes"), lifetime, onError)
        {
            _obj = obj;
            _source = source;
        }

        public ObservableCollection<IndexRow> Indexes { get; } = new ObservableCollection<IndexRow>();

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var list = await _source.GetIndexesAsync(_obj, ct);
            Indexes.Clear();
            foreach (var i in list)
            {
                Indexes.Add(new IndexRow(i));
            }
        }
    }

    public sealed class ForeignKeyRow
    {
        public ForeignKeyRow(ForeignKeyInfo fk, bool outgoing)
        {
            Name = fk.Name;
            OtherTable = outgoing ? fk.ReferencedTable : fk.ParentTable;
            OtherTableDisplay = OtherTable.Schema + "." + OtherTable.Name;
            Columns = string.Join(", ", outgoing ? fk.ParentColumns : fk.ReferencedColumns);
            OtherColumns = string.Join(", ", outgoing ? fk.ReferencedColumns : fk.ParentColumns);
            OnDelete = fk.OnDelete;
            OnUpdate = fk.OnUpdate;

            var states = new List<string>();
            if (fk.IsDisabled)
            {
                states.Add(Strings.Get("Fk_Disabled"));
            }
            if (fk.IsNotTrusted)
            {
                states.Add(Strings.Get("Fk_NotTrusted"));
            }
            State = string.Join(", ", states);
        }

        public string Name { get; }

        /// <summary>The referenced table (outgoing) or the referencing table (incoming).</summary>
        public ObjectName OtherTable { get; }

        public string OtherTableDisplay { get; }

        /// <summary>This table's columns.</summary>
        public string Columns { get; }

        /// <summary>The other table's columns.</summary>
        public string OtherColumns { get; }

        public string OnDelete { get; }
        public string OnUpdate { get; }
        public string State { get; }
    }

    public sealed class ForeignKeysTab : InspectorTab
    {
        private readonly ResolvedObject _obj;
        private readonly ITableMetadataSource _source;
        private int _count;

        public ForeignKeysTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_ForeignKeys"), lifetime, onError)
        {
            _obj = obj;
            _source = source;
            OpenTableCommand = new AsyncCommand(
                p => p is ForeignKeyRow row ? host.OpenTableAsync(row.OtherTable) : Task.CompletedTask,
                ex => onError?.Invoke("Open table", ex));
        }

        public ObservableCollection<ForeignKeyRow> Outgoing { get; } = new ObservableCollection<ForeignKeyRow>();

        public ObservableCollection<ForeignKeyRow> Incoming { get; } = new ObservableCollection<ForeignKeyRow>();

        public ICommand OpenTableCommand { get; }

        public int Count
        {
            get => _count;
            private set => Set(ref _count, value);
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var list = await _source.GetForeignKeysAsync(_obj, ct);
            Outgoing.Clear();
            Incoming.Clear();
            foreach (var fk in list)
            {
                if (fk.IsOutgoing)
                {
                    Outgoing.Add(new ForeignKeyRow(fk, outgoing: true));
                }
                if (fk.IsIncoming)
                {
                    Incoming.Add(new ForeignKeyRow(fk, outgoing: false));
                }
            }
            Count = list.Count;
        }
    }

    public sealed class DataTab : InspectorTab
    {
        private readonly ResolvedObject _obj;
        private readonly ITableMetadataSource _source;
        private readonly IInspectorHost _host;
        private int _top;
        private string _where;
        private string _sortColumn;
        private bool _sortDescending;
        private DataView _rows;
        private string _info;
        private string _lastSql;

        public DataTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, InspectorOptions options,
                       CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Data"), lifetime, onError)
        {
            _obj = obj;
            _source = source;
            _host = host;
            _top = Math.Max(1, Math.Min(DataQuery.MaxRowLimit, options.RowLimit));
            ReadUncommitted = options.ReadUncommitted;

            RunCommand = new AsyncCommand(_ => ReloadAsync(), ex => onError?.Invoke("Data", ex));
            OpenAsQueryCommand = new AsyncCommand(
                _ => _host.OpenQueryAsync(_lastSql ?? DataQueryBuilder.BuildDisplayText(BuildQuery()), _obj.Database),
                ex => onError?.Invoke("Open query", ex));
        }

        public int Top
        {
            get => _top;
            set => Set(ref _top, value);
        }

        public string Where
        {
            get => _where;
            set => Set(ref _where, value);
        }

        public bool ReadUncommitted { get; }

        public string SortColumn
        {
            get => _sortColumn;
            private set => Set(ref _sortColumn, value);
        }

        public bool SortDescending
        {
            get => _sortDescending;
            private set => Set(ref _sortDescending, value);
        }

        public DataView Rows
        {
            get => _rows;
            private set => Set(ref _rows, value);
        }

        public string Info
        {
            get => _info;
            private set => Set(ref _info, value);
        }

        public ICommand RunCommand { get; }

        public ICommand OpenAsQueryCommand { get; }

        /// <summary>Header click: ascending first, then toggles. Sorting happens on the server.</summary>
        public Task SortByAsync(string column)
        {
            if (string.Equals(SortColumn, column, StringComparison.Ordinal))
            {
                SortDescending = !SortDescending;
            }
            else
            {
                SortColumn = column;
                SortDescending = false;
            }
            return ReloadAsync();
        }

        private DataQuery BuildQuery()
        {
            if (Top < 1 || Top > DataQuery.MaxRowLimit)
            {
                throw new InvalidOperationException(Strings.Format("Data_InvalidTop", DataQuery.MaxRowLimit));
            }
            return new DataQuery(_obj, Top, Where, SortColumn, SortDescending);
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            Info = null;
            var query = BuildQuery();
            var page = await _source.GetDataAsync(query, ct);
            _lastSql = page.SqlText;
            Rows = page.Table.DefaultView;

            var key = page.Table.Rows.Count >= query.Top ? "Data_LimitReached" : "Data_Info";
            Info = Strings.Format(key, page.Table.Rows.Count, (long)page.Elapsed.TotalMilliseconds);
        }
    }

    public sealed class DdlTab : InspectorTab
    {
        private readonly ResolvedObject _obj;
        private readonly ITableMetadataSource _source;
        private string _text;

        public DdlTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Ddl"), lifetime, onError)
        {
            _obj = obj;
            _source = source;
            _text = Strings.Get("Ddl_NotLoaded");
            CopyCommand = new RelayCommand(_ => host.CopyToClipboard(Text), _ => State == TabState.Loaded);
            OpenInQueryCommand = new AsyncCommand(_ => host.OpenQueryAsync(Text, _obj.Database), ex => onError?.Invoke("Open query", ex),
                _ => State == TabState.Loaded);
        }

        public string Text
        {
            get => _text;
            private set => Set(ref _text, value);
        }

        public ICommand CopyCommand { get; }

        public ICommand OpenInQueryCommand { get; }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            Text = await _source.GetDdlAsync(_obj, ct);
        }
    }
}
