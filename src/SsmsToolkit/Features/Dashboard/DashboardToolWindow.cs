using System;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Presentation.Dashboard;

namespace SsmsToolkit.Features.Dashboard
{
    /// <summary>Multi-instance document-well tab; one per server/database dashboard.</summary>
    [Guid("6c1e4d2a-8b3f-4e7a-9c5d-1f2a3b4c5d6e")]
    public sealed class DashboardToolWindow : ToolWindowPane
    {
        // Content must be non-null while VS creates the frame (see TableInspectorToolWindow).
        private readonly ContentControl _host = new ContentControl();
        private Action _onClosed;

        public DashboardToolWindow() : base(null)
        {
            Caption = "Dashboard";
            Content = _host;
        }

        internal void Initialize(DatabaseDashboardViewModel viewModel, DatabaseDashboardView view, Action onClosed)
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
