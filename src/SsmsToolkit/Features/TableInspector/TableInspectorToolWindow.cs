using System;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Presentation.TableInspector;

namespace SsmsToolkit.Features.TableInspector
{
    /// <summary>Multi-instance tool window docked in the document well; one per inspected table.</summary>
    [Guid("b3f2c0e4-6a5d-4f0b-9d3e-7c1a2b8e5f60")]
    public sealed class TableInspectorToolWindow : ToolWindowPane
    {
        // VS builds the window frame from Content while the pane is being created (inside FindToolWindowAsync),
        // before Initialize runs. Content must never be null at that point (E_UNEXPECTED otherwise),
        // so the pane always hosts this container and the real view goes inside it.
        private readonly ContentControl _host = new ContentControl();
        private Action _onClosed;

        public TableInspectorToolWindow() : base(null)
        {
            Caption = "Table Inspector";
            Content = _host;
        }

        internal void Initialize(TableInspectorViewModel viewModel, TableInspectorView view, Action onClosed)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Caption = viewModel.Title;
            _host.Content = view;
            _onClosed = onClosed;
        }

        protected override void OnClose()
        {
            try
            {
                var onClosed = _onClosed;
                _onClosed = null;
                onClosed?.Invoke();
                _host.Content = null;
            }
            finally
            {
                base.OnClose();
            }
        }
    }
}
