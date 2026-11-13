using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using SsmsToolkit.Data.Grid;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.ResultGrid;

namespace SsmsToolkit.Playground
{
    /// <summary>The "INSERT statements" dialog over generated sample rows (--insert [--rows N]).</summary>
    internal static class InsertScriptPlayground
    {
        public static Window CreateWindow(int rowCount)
        {
            var source = new SampleRows(rowCount);
            var selection = new SampleRows(Math.Min(3, rowCount));
            var window = new Window { Title = Strings.Get("Insert_Title") + " — Playground", Width = 980, Height = 640 };
            var host = new Host(window);
            var vm = new InsertScriptViewModel(selection, source, new[] { "sales.Orders", "sales.Customers" }, host);
            window.Content = new InsertScriptView { DataContext = vm };
            return window;
        }

        private sealed class SampleRows : IGridRowSource
        {
            public SampleRows(long rowCount)
            {
                RowCount = rowCount;
            }

            public IReadOnlyList<ResultColumn> Columns { get; } = new[]
            {
                new ResultColumn("OrderId", "int") { IsIdentity = true },
                new ResultColumn("CustomerName", "nvarchar"),
                new ResultColumn("Total", "decimal"),
                new ResultColumn("OrderDate", "datetime2"),
                new ResultColumn("IsPaid", "bit"),
                new ResultColumn("Note", "nvarchar"),
            };

            public long RowCount { get; }

            public object[] ReadRow(long index) => new object[]
            {
                (int)index + 1,
                "Customer " + (index % 97),
                Math.Round(10m + index * 1.25m, 2),
                new DateTime(2026, 1, 1).AddMinutes(index * 37),
                index % 3 == 0,
                index % 5 == 0 ? null : "O'Neil & Co",
            };
        }

        private sealed class Host : IInsertScriptHost
        {
            private readonly Window _window;

            public Host(Window window)
            {
                _window = window;
            }

            public void CopyToClipboard(string text) => Clipboard.SetText(text);

            public Task OpenInNewQueryAsync(string text)
            {
                MessageBox.Show(_window, text.Length + " characters would open in a new query window.", "Playground");
                return Task.CompletedTask;
            }

            public string AskSaveFileName(string suggestedName)
            {
                var dialog = new SaveFileDialog { FileName = suggestedName, Filter = Strings.Get("Insert_FileFilter") };
                return dialog.ShowDialog(_window) == true ? dialog.FileName : null;
            }

            public void Close() => _window.Close();
        }
    }
}
