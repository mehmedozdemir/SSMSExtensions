using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SsmsToolkit.Features.TableInspector
{
    /// <summary>Draws a link-style underline under one span and shows a hand cursor (Ctrl-hover).</summary>
    internal sealed class LinkHoverAdornment
    {
        public const string LayerName = "SsmsToolkit.LinkHover";

        private readonly IWpfTextView _view;
        private IAdornmentLayer _layer;
        private SnapshotSpan? _current;
        private Cursor _previousCursor;

        public LinkHoverAdornment(IWpfTextView view)
        {
            _view = view;
            _view.LayoutChanged += (s, e) =>
            {
                // Scrolling / editing invalidates the drawn position.
                if (_current != null)
                {
                    Clear();
                }
            };
        }

        public void Show(SnapshotSpan span)
        {
            if (_current.HasValue && _current.Value == span)
            {
                return;
            }

            Clear();
            var geometry = _view.TextViewLines?.GetMarkerGeometry(span);
            if (geometry == null)
            {
                return;
            }

            var bounds = geometry.Bounds;
            var brush = Application.Current?.TryFindResource(EnvironmentColors.ControlLinkTextBrushKey) as Brush ?? Brushes.DodgerBlue;
            var line = new Rectangle { Width = bounds.Width, Height = 1, Fill = brush, IsHitTestVisible = false };
            Canvas.SetLeft(line, bounds.Left);
            Canvas.SetTop(line, bounds.Bottom - 2);

            _layer = _layer ?? _view.GetAdornmentLayer(LayerName);
            _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, line, null);
            _current = span;

            _previousCursor = _view.VisualElement.Cursor;
            _view.VisualElement.Cursor = Cursors.Hand;
        }

        public void Clear()
        {
            if (_current == null)
            {
                return;
            }

            _current = null;
            _layer?.RemoveAllAdornments();
            _view.VisualElement.Cursor = _previousCursor;
            _previousCursor = null;
        }
    }
}
