using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace SsmsToolkit.UI
{
    /// <summary>
    /// Phase 0: multi-instance tool window opened in the document well (VsDockStyle.MDI).
    /// Shows a plain-text report and uses SSMS theme colors.
    /// </summary>
    [Guid("b3f2c0e4-6a5d-4f0b-9d3e-7c1a2b8e5f60")]
    public sealed class SpikeToolWindow : ToolWindowPane
    {
        private readonly TextBox _text;

        public SpikeToolWindow() : base(null)
        {
            Caption = "SsmsToolkit Spike";

            _text = new TextBox
            {
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "Waiting for report...",
            };
            _text.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _text.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            Content = _text;
        }

        public void SetReport(string title, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Caption = title;
            _text.Text = text;
        }
    }
}
