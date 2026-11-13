using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using SsmsToolkit.Data.Grid;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Mvvm;

namespace SsmsToolkit.Presentation.ResultGrid
{
    /// <summary>Rows of a results grid, read on the UI thread one at a time.</summary>
    public interface IGridRowSource
    {
        IReadOnlyList<ResultColumn> Columns { get; }

        long RowCount { get; }

        /// <summary>One value per column of <see cref="Columns"/>. <paramref name="index"/> is 0..RowCount-1.</summary>
        object[] ReadRow(long index);
    }

    /// <summary>Where the generated script goes. Implemented by SSMS (VSIX) and the Playground.</summary>
    public interface IInsertScriptHost
    {
        void CopyToClipboard(string text);

        Task OpenInNewQueryAsync(string text);

        /// <summary>Asks for a file name; null when cancelled.</summary>
        string AskSaveFileName(string suggestedName);

        void Close();
    }

    /// <summary>
    /// "INSERT statements" dialog: target table, options, live preview of the first rows, and generation of the full
    /// script with progress and cancel, to the clipboard, a new query window or a file.
    /// </summary>
    public sealed class InsertScriptViewModel : ObservableObject
    {
        public const int PreviewRows = 100;

        /// <summary>Above this many cells the script would be hundreds of MB; refuse.</summary>
        public const long MaxCells = 5_000_000;

        /// <summary>Larger scripts are only offered as a file (the editor becomes unusable).</summary>
        public const int MaxQueryWindowChars = 30_000_000;

        private const int RowsPerProgressStep = 250;

        private readonly IGridRowSource _selection;
        private readonly IGridRowSource _all;
        private readonly IInsertScriptHost _host;
        private CancellationTokenSource _cts;
        private string _tableName;
        private bool _useAllRows;
        private int _rowsPerStatement = 1;
        private bool _excludeGenerated = true;
        private bool _identityInsert;
        private bool _quoteIdentifiers = true;
        private string _preview;
        private string _previewNote;
        private bool _isBusy;
        private double _progress;
        private string _status;

        /// <param name="selection">The selected cells (rows × selected columns).</param>
        /// <param name="all">The whole result set, or null to hide the "all rows" choice.</param>
        /// <param name="tableCandidates">Suggestions for the target table (schema, query text).</param>
        public InsertScriptViewModel(IGridRowSource selection, IGridRowSource all, IReadOnlyList<string> tableCandidates, IInsertScriptHost host)
        {
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _all = all;
            _host = host ?? throw new ArgumentNullException(nameof(host));
            TableCandidates = (tableCandidates ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _tableName = TableCandidates.FirstOrDefault() ?? string.Empty;

            CopyCommand = new AsyncCommand(_ => RunAsync(Output.Clipboard), OnError, _ => !IsBusy);
            OpenInQueryCommand = new AsyncCommand(_ => RunAsync(Output.QueryWindow), OnError, _ => !IsBusy);
            SaveCommand = new AsyncCommand(_ => RunAsync(Output.File), OnError, _ => !IsBusy);
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
            CloseCommand = new RelayCommand(_ =>
            {
                _cts?.Cancel();
                _host.Close();
            });

            RefreshPreview();
        }

        private enum Output
        {
            Clipboard,
            QueryWindow,
            File,
        }

        public IReadOnlyList<string> TableCandidates { get; }

        public IReadOnlyList<int> RowsPerStatementChoices { get; } = new[] { 1, 10, 100, 1000 };

        public string TableName
        {
            get => _tableName;
            set
            {
                if (Set(ref _tableName, value))
                {
                    RefreshPreview();
                }
            }
        }

        public bool HasAllRowsChoice => _all != null;

        public string SelectionRowsText => Strings.Format("Insert_SelectedRows", _selection.RowCount.ToString("N0", CultureInfo.CurrentCulture), _selection.Columns.Count);

        public string AllRowsText => _all == null ? null : Strings.Format("Insert_AllRows", _all.RowCount.ToString("N0", CultureInfo.CurrentCulture), _all.Columns.Count);

        public bool UseAllRows
        {
            get => _useAllRows;
            set
            {
                if (Set(ref _useAllRows, value && _all != null))
                {
                    OnPropertyChanged(nameof(UseSelectedRows));
                    OnPropertyChanged(nameof(HasIdentityColumn));
                    OnPropertyChanged(nameof(CanUseIdentityInsert));
                    RefreshPreview();
                }
            }
        }

        public bool UseSelectedRows
        {
            get => !UseAllRows;
            set => UseAllRows = !value;
        }

        public int RowsPerStatement
        {
            get => _rowsPerStatement;
            set
            {
                if (Set(ref _rowsPerStatement, Math.Max(1, Math.Min(InsertScriptOptions.MaxRowsPerStatement, value))))
                {
                    RefreshPreview();
                }
            }
        }

        public bool ExcludeGeneratedColumns
        {
            get => _excludeGenerated;
            set
            {
                if (Set(ref _excludeGenerated, value))
                {
                    OnPropertyChanged(nameof(CanUseIdentityInsert));
                    RefreshPreview();
                }
            }
        }

        /// <summary>The current source has an IDENTITY column (reported by the result schema).</summary>
        public bool HasIdentityColumn => Source.Columns.Any(c => c.IsIdentity);

        public bool CanUseIdentityInsert => HasIdentityColumn && !ExcludeGeneratedColumns;

        public bool IdentityInsert
        {
            get => _identityInsert;
            set
            {
                if (Set(ref _identityInsert, value))
                {
                    RefreshPreview();
                }
            }
        }

        public bool QuoteIdentifiers
        {
            get => _quoteIdentifiers;
            set
            {
                if (Set(ref _quoteIdentifiers, value))
                {
                    RefreshPreview();
                }
            }
        }

        public string Preview
        {
            get => _preview;
            private set => Set(ref _preview, value);
        }

        public string PreviewNote
        {
            get => _previewNote;
            private set => Set(ref _previewNote, value);
        }

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

        /// <summary>0..100 while generating.</summary>
        public double Progress
        {
            get => _progress;
            private set => Set(ref _progress, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        public ICommand CopyCommand { get; }

        public ICommand OpenInQueryCommand { get; }

        public ICommand SaveCommand { get; }

        public ICommand CancelCommand { get; }

        public ICommand CloseCommand { get; }

        private IGridRowSource Source => _useAllRows && _all != null ? _all : _selection;

        private InsertScriptOptions Options => new InsertScriptOptions
        {
            TableName = TableName,
            RowsPerStatement = RowsPerStatement,
            ExcludeGeneratedColumns = ExcludeGeneratedColumns,
            IdentityInsert = IdentityInsert && CanUseIdentityInsert,
            QuoteIdentifiers = QuoteIdentifiers,
        };

        /// <summary>Generates the whole script (used by the commands; public for tests and hosts).</summary>
        public async Task<string> GenerateAsync(CancellationToken cancellationToken, Func<Task> yieldToUi = null)
        {
            yieldToUi = yieldToUi ?? (async () => await Dispatcher.Yield(DispatcherPriority.Background));
            var source = Source;
            var builder = new InsertScriptBuilder(source.Columns, Options);
            var sb = new StringBuilder();
            builder.Begin(sb);
            long total = source.RowCount;
            for (long i = 0; i < total; i++)
            {
                builder.AppendRow(sb, source.ReadRow(i));
                if ((i + 1) % RowsPerProgressStep == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Progress = 100.0 * (i + 1) / total;
                    Status = Strings.Format("Insert_Progress", (i + 1).ToString("N0", CultureInfo.CurrentCulture), total.ToString("N0", CultureInfo.CurrentCulture));
                    await yieldToUi();
                }
            }
            builder.End(sb);
            return sb.ToString();
        }

        private async Task RunAsync(Output output)
        {
            var source = Source;
            if (source.RowCount * Math.Max(1, source.Columns.Count) > MaxCells)
            {
                Status = Strings.Format("Insert_TooLarge", MaxCells.ToString("N0", CultureInfo.CurrentCulture));
                return;
            }

            string fileName = null;
            if (output == Output.File)
            {
                fileName = _host.AskSaveFileName(SuggestedFileName());
                if (fileName == null)
                {
                    return;
                }
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            try
            {
                var text = await GenerateAsync(_cts.Token);
                Progress = 100;
                switch (output)
                {
                    case Output.Clipboard:
                        _host.CopyToClipboard(text);
                        Status = Strings.Format("Insert_Copied", source.RowCount.ToString("N0", CultureInfo.CurrentCulture));
                        break;
                    case Output.QueryWindow:
                        if (text.Length > MaxQueryWindowChars)
                        {
                            Status = Strings.Get("Insert_TooLargeForEditor");
                            return;
                        }
                        await _host.OpenInNewQueryAsync(text);
                        Status = Strings.Get("Insert_Opened");
                        break;
                    case Output.File:
                        await Task.Run(() => System.IO.File.WriteAllText(fileName, text, new UTF8Encoding(true)));
                        Status = Strings.Format("Insert_Saved", fileName);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                Status = Strings.Get("Insert_Cancelled");
                Progress = 0;
            }
            finally
            {
                IsBusy = false;
                _cts.Dispose();
                _cts = null;
            }
        }

        private string SuggestedFileName()
        {
            var name = InsertScriptBuilder.FormatTableName(TableName, quote: false);
            var safe = new string(name.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) || ch == '[' || ch == ']' ? '_' : ch).ToArray()).Trim('_', '.');
            return (string.IsNullOrEmpty(safe) ? "insert" : safe) + ".sql";
        }

        private void RefreshPreview()
        {
            try
            {
                var source = Source;
                long rows = Math.Min(PreviewRows, source.RowCount);
                var sample = new List<object[]>();
                for (long i = 0; i < rows; i++)
                {
                    sample.Add(source.ReadRow(i));
                }

                Preview = InsertScriptBuilder.Build(source.Columns, sample, Options);
                PreviewNote = source.RowCount > rows
                    ? Strings.Format("Insert_PreviewNote", rows.ToString("N0", CultureInfo.CurrentCulture), source.RowCount.ToString("N0", CultureInfo.CurrentCulture))
                    : null;
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private void OnError(Exception ex) => Status = Strings.Format("Insert_Failed", ex.Message);
    }
}
