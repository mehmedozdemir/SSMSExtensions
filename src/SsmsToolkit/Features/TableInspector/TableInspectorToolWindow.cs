using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Presentation.TableInspector;

namespace SsmsToolkit.Features.TableInspector
{
    /// <summary>Multi-instance tool window docked in the document well; one per inspected table.</summary>
    [Guid("b3f2c0e4-6a5d-4f0b-9d3e-7c1a2b8e5f60")]
    public sealed class TableInspectorToolWindow : ToolWindowPane
    {
        private Action _onClosed;

        public TableInspectorToolWindow() : base(null)
        {
            Caption = "Table Inspector";
        }

        internal void Initialize(TableInspectorViewModel viewModel, TableInspectorView view, Action onClosed)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Caption = viewModel.Title;
            Content = view;
            _onClosed = onClosed;
        }

        protected override void OnClose()
        {
            try
            {
                var onClosed = _onClosed;
                _onClosed = null;
                onClosed?.Invoke();
                Content = null;
            }
            finally
            {
                base.OnClose();
            }
        }
    }
}
