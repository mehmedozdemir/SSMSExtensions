using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Win32;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.ResultGrid;

namespace SsmsToolkit.UI
{
    /// <summary>Modal SSMS dialog hosting <see cref="InsertScriptView"/>.</summary>
    internal sealed class InsertScriptDialog : DialogWindow, IInsertScriptHost
    {
        private readonly Func<string, Task> _openInNewQuery;

        public InsertScriptDialog(Func<string, Task> openInNewQuery)
        {
            _openInNewQuery = openInNewQuery;
            Title = Strings.Get("Insert_Title");
            Width = 980;
            Height = 640;
            MinWidth = 640;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMinimizeButton = false;
            HasMaximizeButton = true;
            ShowInTaskbar = false;
        }

        public void Show(InsertScriptViewModel viewModel)
        {
            var view = new InsertScriptView { DataContext = viewModel };
            VsTheme.UseVsControlStyles(view);
            VsTheme.UseVsScrollBars(view);
            Content = view;
            ShowModal();
        }

        public void CopyToClipboard(string text) =>
            System.Windows.Forms.Clipboard.SetDataObject(new System.Windows.Forms.DataObject(System.Windows.Forms.DataFormats.UnicodeText, text), copy: true);

        public Task OpenInNewQueryAsync(string text) => _openInNewQuery(text);

        public string AskSaveFileName(string suggestedName)
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedName,
                DefaultExt = ".sql",
                Filter = Strings.Get("Insert_FileFilter"),
                OverwritePrompt = true,
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        void IInsertScriptHost.Close() => Close();
    }
}
