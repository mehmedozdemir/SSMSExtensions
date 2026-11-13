using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsToolkit.Core;
using SsmsToolkit.Package;
using SsmsToolkit.Presentation.Settings;
using SsmsToolkit.SsmsInterop;

namespace SsmsToolkit.Features.TableInspector
{
    [Export(typeof(IMouseProcessorProvider))]
    [Name("SsmsToolkit.TableInspectorMouseProcessor")]
    [ContentType(ActiveEditor.SqlContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class TableInspectorMouseProcessorProvider : IMouseProcessorProvider
    {
        /// <summary>Adornment layer for the Ctrl-hover underline.</summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LinkHoverAdornment.LayerName)]
        [Order(After = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition LinkLayer = null;

        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; }

        public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView) =>
            new TableInspectorMouseProcessor(wpfTextView, TextDocumentFactory);
    }

    /// <summary>
    /// Ctrl+Click (configurable) on an identifier in a SQL query editor opens the Table Inspector.
    /// While the trigger keys are held, the identifier under the mouse is underlined and the cursor is a hand.
    /// </summary>
    internal sealed class TableInspectorMouseProcessor : MouseProcessorBase
    {
        private readonly IWpfTextView _view;
        private readonly ITextDocumentFactoryService _documents;
        private readonly LinkHoverAdornment _hover;

        public TableInspectorMouseProcessor(IWpfTextView view, ITextDocumentFactoryService documents)
        {
            _view = view;
            _documents = documents;
            _hover = new LinkHoverAdornment(view);

            // Pressing / releasing the trigger keys without moving the mouse must also update the underline.
            _view.VisualElement.PreviewKeyDown += OnKeyChanged;
            _view.VisualElement.PreviewKeyUp += OnKeyChanged;
            _view.LostAggregateFocus += OnLostFocus;
            _view.Closed += OnClosed;
        }

        public override void PreprocessMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            try
            {
                if (!IsTriggerPressed() || !IsFeatureActive())
                {
                    return;
                }

                var position = GetPosition(e.GetPosition(_view.VisualElement));
                if (position == null || !IsIdentifierChar(position.Value.GetChar()))
                {
                    return;
                }

                // The click is ours: suppress SSMS's default Ctrl+Click (word selection).
                e.Handled = true;
                _hover.Clear();

                var text = _view.TextSnapshot.GetText();
                var offset = position.Value.Position;
                var moniker = ActiveEditor.GetDocumentMoniker(_view, _documents);

#pragma warning disable VSSDK007 // Fire-and-forget by design; InspectAtAsync catches and reports everything.
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    var p = await ToolkitPackage.GetOrLoadAsync();
                    if (p != null && p.Features.IsActive(TableInspectorFeature.FeatureId))
                    {
                        await p.Launcher.InspectAtAsync(text, offset, moniker);
                    }
                });
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Report("mouse", ex);
            }
        }

        public override void PreprocessMouseMove(MouseEventArgs e)
        {
            UpdateHover(e.GetPosition(_view.VisualElement));
        }

        public override void PostprocessMouseLeave(MouseEventArgs e)
        {
            _hover.Clear();
        }

        private void OnKeyChanged(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftAlt || e.Key == Key.RightAlt
                || e.Key == Key.System || e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                if (_view.VisualElement.IsMouseOver)
                {
                    UpdateHover(Mouse.GetPosition(_view.VisualElement));
                }
                else
                {
                    _hover.Clear();
                }
            }
        }

        private void UpdateHover(Point point)
        {
            try
            {
                if (!IsTriggerPressed() || !IsFeatureActive())
                {
                    _hover.Clear();
                    return;
                }

                var position = GetPosition(point);
                var span = position == null ? null : IdentifierSpanAt(position.Value);
                if (span == null)
                {
                    _hover.Clear();
                }
                else
                {
                    _hover.Show(span.Value);
                }
            }
            catch (Exception ex)
            {
                _hover.Clear();
                Report("hover", ex);
            }
        }

        private void OnLostFocus(object sender, EventArgs e) => _hover.Clear();

        private void OnClosed(object sender, EventArgs e)
        {
            _view.VisualElement.PreviewKeyDown -= OnKeyChanged;
            _view.VisualElement.PreviewKeyUp -= OnKeyChanged;
            _view.LostAggregateFocus -= OnLostFocus;
            _view.Closed -= OnClosed;
        }

        private static bool IsFeatureActive()
        {
            var package = ToolkitPackage.Instance;
            return package != null
                ? package.Features.IsActive(TableInspectorFeature.FeatureId)
                : !SettingsStore.Current.Features.TryGetValue(TableInspectorFeature.FeatureId, out var enabled) || enabled;
        }

        private static bool IsTriggerPressed()
        {
            var modifiers = Keyboard.Modifiers;
            switch (SettingsStore.Current.TableInspector.Trigger)
            {
                case ClickTrigger.CtrlClick:
                    return modifiers == ModifierKeys.Control;
                case ClickTrigger.CtrlAltClick:
                    return modifiers == (ModifierKeys.Control | ModifierKeys.Alt);
                default:
                    return false;
            }
        }

        private SnapshotPoint? GetPosition(Point pt)
        {
            var line = _view.TextViewLines?.GetTextViewLineContainingYCoordinate(pt.Y + _view.ViewportTop);
            if (line == null)
            {
                return null;
            }

            // Only when the mouse is really over text (not in the empty area right of the line end).
            var x = pt.X + _view.ViewportLeft;
            return x <= line.TextRight ? line.GetBufferPositionFromXCoordinate(x) : null;
        }

        /// <summary>The word under the mouse (one part of a multipart name). Lexical only; no server round trip.</summary>
        internal static SnapshotSpan? IdentifierSpanAt(SnapshotPoint point)
        {
            if (point.Position >= point.Snapshot.Length || !IsIdentifierChar(point.GetChar()))
            {
                return null;
            }

            var line = point.GetContainingLine();
            int start = point.Position;
            while (start > line.Start.Position && IsIdentifierChar((point.Snapshot[start - 1])))
            {
                start--;
            }
            int end = point.Position;
            while (end < line.End.Position && IsIdentifierChar(point.Snapshot[end]))
            {
                end++;
            }
            return end > start ? new SnapshotSpan(point.Snapshot, start, end - start) : (SnapshotSpan?)null;
        }

        internal static bool IsIdentifierChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$' || c == '[' || c == ']' || c == '"';

        private static void Report(string context, Exception ex)
        {
            if (ToolkitPackage.Instance != null)
            {
                ToolkitPackage.Instance.Features.ReportFailure(TableInspectorFeature.FeatureId, context, ex);
            }
            else
            {
                Logger.Error("Table Inspector " + context + " failed before the package loaded", ex);
            }
        }
    }
}
