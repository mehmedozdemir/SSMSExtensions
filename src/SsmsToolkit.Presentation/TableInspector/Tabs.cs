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

    /// <summary>A grid row that has a SQL script for the bottom script panel.</summary>
    public interface IScriptRow
    {
        string Script { get; }
    }

    /// <summary>A list tab whose selected row's script is shown in the script panel, with copy / open in new query.</summary>
    public abstract class ScriptListTab : InspectorTab
    {
        private IScriptRow _selected;

        protected ScriptListTab(string header, ResolvedObject obj, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(header, lifetime, onError)
        {
            Object = obj;
            CopyScriptCommand = new RelayCommand(_ => host.CopyToClipboard(Selected?.Script), _ => Selected != null);
            OpenScriptCommand = new AsyncCommand(_ => host.OpenQueryAsync(Selected?.Script, obj.Database), ex => onError?.Invoke("Open script", ex),
                _ => Selected != null);
        }

        protected ResolvedObject Object { get; }

        public IScriptRow Selected
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                {
                    OnPropertyChanged(nameof(SelectedScript));
                }
            }
        }

        public string SelectedScript => Selected?.Script;

        /// <summary>Note above the script; scripts generated from metadata say what they omit. Null hides it.</summary>
        public virtual string ScriptNote => Strings.Get("Script_Note");

        public ICommand CopyScriptCommand { get; }

        public ICommand OpenScriptCommand { get; }
    }

    public sealed class ColumnRow : IScriptRow
    {
        public ColumnRow(ResolvedObject table, ColumnInfo info)
        {
            Info = info;
            Script = ScriptBuilder.Column(table, info);
        }

        public ColumnInfo Info { get; }

        public string Script { get; }
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

    public sealed class ColumnsTab : ScriptListTab
    {
        private readonly ITableMetadataSource _source;

        public ColumnsTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Columns"), obj, host, lifetime, onError)
        {
            _source = source;
        }

        public ObservableCollection<ColumnRow> Columns { get; } = new ObservableCollection<ColumnRow>();

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var cols = await _source.GetColumnsAsync(Object, ct);
            Selected = null;
            Columns.Clear();
            foreach (var c in cols)
            {
                Columns.Add(new ColumnRow(Object, c));
            }
        }
    }

    public sealed class IndexRow : IScriptRow
    {
        public IndexRow(ResolvedObject table, IndexInfo i)
        {
            Script = i.IsConstraint ? ScriptBuilder.Key(table, i) : ScriptBuilder.Index(table, i);
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
        public string Script { get; }
    }

    /// <summary>
    /// Indexes from sys.indexes, split in two tabs: <see cref="KeysTab"/> (PRIMARY KEY / UNIQUE constraints)
    /// and <see cref="IndexesTab"/> (all other indexes), so a constraint's backing index is not listed twice.
    /// </summary>
    public abstract class IndexListTab : ScriptListTab
    {
        private readonly ITableMetadataSource _source;
        private readonly bool _constraints;

        protected IndexListTab(string header, bool constraints, ResolvedObject obj, ITableMetadataSource source, IInspectorHost host,
                               CancellationToken lifetime, Action<string, Exception> onError)
            : base(header, obj, host, lifetime, onError)
        {
            _source = source;
            _constraints = constraints;
        }

        public ObservableCollection<IndexRow> Items { get; } = new ObservableCollection<IndexRow>();

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var list = await _source.GetIndexesAsync(Object, ct);
            Selected = null;
            Items.Clear();
            foreach (var i in list.Where(i => i.IsConstraint == _constraints))
            {
                Items.Add(new IndexRow(Object, i));
            }
        }
    }

    public sealed class KeysTab : IndexListTab
    {
        public KeysTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Keys"), true, obj, source, host, lifetime, onError)
        {
        }
    }

    public sealed class IndexesTab : IndexListTab
    {
        public IndexesTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Indexes"), false, obj, source, host, lifetime, onError)
        {
        }
    }

    public sealed class ConstraintRow : IScriptRow
    {
        public ConstraintRow(ResolvedObject table, ConstraintInfo c)
        {
            Script = ScriptBuilder.Constraint(table, c);
            Name = c.Name;
            Kind = c.Kind == ConstraintKind.Check ? "CHECK" : "DEFAULT";
            Column = c.Column ?? string.Empty;
            Definition = c.Definition;

            var states = new List<string>();
            if (c.IsDisabled)
            {
                states.Add(Strings.Get("Fk_Disabled"));
            }
            if (c.IsNotTrusted)
            {
                states.Add(Strings.Get("Fk_NotTrusted"));
            }
            if (c.IsSystemNamed)
            {
                states.Add(Strings.Get("Constraint_SystemNamed"));
            }
            State = string.Join(", ", states);
        }

        public string Name { get; }
        public string Kind { get; }
        public string Column { get; }
        public string Definition { get; }
        public string State { get; }
        public string Script { get; }
    }

    public sealed class ConstraintsTab : ScriptListTab
    {
        private readonly ITableMetadataSource _source;

        public ConstraintsTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Constraints"), obj, host, lifetime, onError)
        {
            _source = source;
        }

        public ObservableCollection<ConstraintRow> Items { get; } = new ObservableCollection<ConstraintRow>();

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var list = await _source.GetConstraintsAsync(Object, ct);
            Selected = null;
            Items.Clear();
            foreach (var c in list)
            {
                Items.Add(new ConstraintRow(Object, c));
            }
        }
    }

    public sealed class ForeignKeyRow : IScriptRow
    {
        public ForeignKeyRow(ForeignKeyInfo fk, bool outgoing)
        {
            Script = ScriptBuilder.ForeignKey(fk);
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
        public string Script { get; }
    }

    public sealed class ForeignKeysTab : ScriptListTab
    {
        private readonly ITableMetadataSource _source;
        private int _count;
        private ForeignKeyRow _selectedOutgoing;
        private ForeignKeyRow _selectedIncoming;

        public ForeignKeysTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_ForeignKeys"), obj, host, lifetime, onError)
        {
            _source = source;
            OpenTableCommand = new AsyncCommand(
                p => p is ForeignKeyRow row ? host.OpenTableAsync(row.OtherTable) : Task.CompletedTask,
                ex => onError?.Invoke("Open table", ex));
        }

        public ObservableCollection<ForeignKeyRow> Outgoing { get; } = new ObservableCollection<ForeignKeyRow>();

        public ObservableCollection<ForeignKeyRow> Incoming { get; } = new ObservableCollection<ForeignKeyRow>();

        public ICommand OpenTableCommand { get; }

        /// <summary>Selection in the outgoing grid; selecting here clears the incoming grid's selection.</summary>
        public ForeignKeyRow SelectedOutgoing
        {
            get => _selectedOutgoing;
            set
            {
                if (Set(ref _selectedOutgoing, value) && value != null)
                {
                    Selected = value;
                    SelectedIncoming = null;
                }
            }
        }

        public ForeignKeyRow SelectedIncoming
        {
            get => _selectedIncoming;
            set
            {
                if (Set(ref _selectedIncoming, value) && value != null)
                {
                    Selected = value;
                    SelectedOutgoing = null;
                }
            }
        }

        public int Count
        {
            get => _count;
            private set => Set(ref _count, value);
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var list = await _source.GetForeignKeysAsync(Object, ct);
            Selected = null;
            SelectedOutgoing = null;
            SelectedIncoming = null;
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

    public sealed class TriggerRow : IScriptRow
    {
        public TriggerRow(ResolvedObject table, TriggerInfo t)
        {
            Name = t.Name;
            Timing = t.IsInsteadOf ? "INSTEAD OF" : "AFTER";
            Events = t.Events;
            IsDisabled = t.IsDisabled;
            Script = ScriptBuilder.Trigger(table, t);
        }

        public string Name { get; }
        public string Timing { get; }
        public string Events { get; }
        public bool IsDisabled { get; }
        public string Script { get; }
    }

    public sealed class TriggersTab : ScriptListTab
    {
        private readonly ITableMetadataSource _source;

        /// <summary>Scripts here are the server-side definitions, not generated.</summary>
        public override string ScriptNote => null;

        public TriggersTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Triggers"), obj, host, lifetime, onError)
        {
            _source = source;
        }

        public ObservableCollection<TriggerRow> Items { get; } = new ObservableCollection<TriggerRow>();

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var list = await _source.GetTriggersAsync(Object, ct);
            Selected = null;
            Items.Clear();
            foreach (var t in list)
            {
                Items.Add(new TriggerRow(Object, t));
            }
        }
    }

    public sealed class DependencyRow : IScriptRow
    {
        public DependencyRow(DependencyInfo d, string currentDatabase)
        {
            Target = d.Name;
            IsTableOrView = d.IsTableOrView;
            bool otherDb = d.Name.Database != null && !string.Equals(d.Name.Database, currentDatabase, StringComparison.OrdinalIgnoreCase);
            Display = (otherDb ? d.Name.Database + "." : string.Empty) + (d.Name.Schema == null ? string.Empty : d.Name.Schema + ".") + d.Name.Name;
            Type = d.TypeDescription ?? Strings.Get("Dep_Missing");
            Script = string.IsNullOrEmpty(d.Definition)
                ? "-- " + Display + " (" + Type + "): " + Strings.Get("Dep_NoDefinition")
                : d.Definition.Trim();
        }

        public ObjectName Target { get; }

        public string Display { get; }

        public string Type { get; }

        /// <summary>Tables and views open in a new inspector when clicked.</summary>
        public bool IsTableOrView { get; }

        public string Script { get; }
    }

    public sealed class DependenciesTab : ScriptListTab
    {
        private readonly ITableMetadataSource _source;
        private DependencyRow _selectedUsedBy;
        private DependencyRow _selectedUses;

        /// <summary>Scripts here are the server-side definitions, not generated.</summary>
        public override string ScriptNote => null;

        public DependenciesTab(ResolvedObject obj, ITableMetadataSource source, IInspectorHost host, CancellationToken lifetime, Action<string, Exception> onError)
            : base(Strings.Get("Tab_Dependencies"), obj, host, lifetime, onError)
        {
            _source = source;
            OpenTableCommand = new AsyncCommand(
                p => p is DependencyRow row && row.IsTableOrView ? host.OpenTableAsync(row.Target) : Task.CompletedTask,
                ex => onError?.Invoke("Open table", ex));
        }

        public ObservableCollection<DependencyRow> UsedBy { get; } = new ObservableCollection<DependencyRow>();

        public ObservableCollection<DependencyRow> Uses { get; } = new ObservableCollection<DependencyRow>();

        public ICommand OpenTableCommand { get; }

        public DependencyRow SelectedUsedBy
        {
            get => _selectedUsedBy;
            set
            {
                if (Set(ref _selectedUsedBy, value) && value != null)
                {
                    Selected = value;
                    SelectedUses = null;
                }
            }
        }

        public DependencyRow SelectedUses
        {
            get => _selectedUses;
            set
            {
                if (Set(ref _selectedUses, value) && value != null)
                {
                    Selected = value;
                    SelectedUsedBy = null;
                }
            }
        }

        protected override async Task LoadCoreAsync(CancellationToken ct)
        {
            var list = await _source.GetDependenciesAsync(Object, ct);
            Selected = null;
            SelectedUsedBy = null;
            SelectedUses = null;
            UsedBy.Clear();
            Uses.Clear();
            foreach (var d in list)
            {
                (d.Direction == DependencyDirection.UsedBy ? UsedBy : Uses).Add(new DependencyRow(d, Object.Database));
            }
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
