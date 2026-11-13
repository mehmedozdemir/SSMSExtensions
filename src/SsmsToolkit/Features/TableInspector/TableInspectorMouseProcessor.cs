using System;
using System.ComponentModel.Composition;
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
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; }

        public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView) =>
            new TableInspectorMouseProcessor(wpfTextView, TextDocumentFactory);
    }

    /// <summary>Ctrl+Click (configurable) on an identifier in a SQL query editor opens the Table Inspector.</summary>
    internal sealed class TableInspectorMouseProcessor : MouseProcessorBase
    {
        private readonly IWpfTextView _view;
        private readonly ITextDocumentFactoryService _documents;

        public TableInspectorMouseProcessor(IWpfTextView view, ITextDocumentFactoryService documents)
        {
            _view = view;
            _documents = documents;
        }

        public override void PreprocessMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            try
            {
                if (!IsTriggerPressed())
                {
                    return;
                }

                var package = ToolkitPackage.Instance;
                bool active = package != null
                    ? package.Features.IsActive(TableInspectorFeature.FeatureId)
                    : !SettingsStore.Current.Features.TryGetValue(TableInspectorFeature.FeatureId, out var enabled) || enabled;
                if (!active)
                {
                    return;
                }

                var position = GetPosition(e);
                if (position == null || !IsIdentifierChar(position.Value.GetChar()))
                {
                    return;
                }

                // The click is ours: suppress SSMS's default Ctrl+Click (word selection).
                e.Handled = true;

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
                ToolkitPackage.Instance?.Features.ReportFailure(TableInspectorFeature.FeatureId, "mouse", ex);
                if (ToolkitPackage.Instance == null)
                {
                    Logger.Error("Ctrl+Click failed before the package loaded", ex);
                }
            }
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

        private SnapshotPoint? GetPosition(MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(_view.VisualElement);
            var line = _view.TextViewLines?.GetTextViewLineContainingYCoordinate(pt.Y + _view.ViewportTop);
            return line?.GetBufferPositionFromXCoordinate(pt.X + _view.ViewportLeft);
        }

        internal static bool IsIdentifierChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$' || c == '[' || c == ']' || c == '"';
    }
}
