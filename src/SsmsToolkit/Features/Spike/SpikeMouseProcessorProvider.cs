using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsToolkit.Core;
using SsmsToolkit.Package;

namespace SsmsToolkit.Features.Spike
{
    /// <summary>
    /// Phase 0: registered for the broad "text" content type so we can discover which content type SSMS
    /// really uses for query editors. Every view creation is logged; Ctrl+Click is only handled in SQL-like views.
    /// </summary>
    [Export(typeof(IMouseProcessorProvider))]
    [Name("SsmsToolkit.SpikeMouseProcessor")]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SpikeMouseProcessorProvider : IMouseProcessorProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; }

        public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
        {
            try
            {
                var contentType = wpfTextView.TextBuffer.ContentType;
                string path = null;
                if (TextDocumentFactory != null && TextDocumentFactory.TryGetTextDocument(wpfTextView.TextBuffer, out var doc))
                {
                    path = doc.FilePath;
                }

                Logger.Info("Text view created: contentType='" + contentType.TypeName + "'" +
                            ", baseTypes=[" + string.Join(" > ", BaseChain(contentType)) + "]" +
                            ", roles=[" + string.Join(",", wpfTextView.Roles) + "]" +
                            ", document='" + path + "'");

                return new SpikeMouseProcessor(wpfTextView);
            }
            catch (Exception ex)
            {
                Logger.Error("GetAssociatedProcessor failed", ex);
                return null;
            }
        }

        private static IEnumerable<string> BaseChain(IContentType type)
        {
            var seen = new HashSet<string>();
            var queue = new Queue<IContentType>(type.BaseTypes);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (seen.Add(t.TypeName))
                {
                    yield return t.TypeName;
                    foreach (var b in t.BaseTypes)
                    {
                        queue.Enqueue(b);
                    }
                }
            }
        }
    }

    internal sealed class SpikeMouseProcessor : MouseProcessorBase
    {
        private const int MaxFailures = 3;
        private static int _failures;

        private readonly IWpfTextView _view;

        public SpikeMouseProcessor(IWpfTextView view)
        {
            _view = view;
        }

        public override void PreprocessMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Volatile.Read(ref _failures) >= MaxFailures || Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            try
            {
                var contentType = _view.TextBuffer.ContentType.TypeName;
                var identifier = GetIdentifierAt(e);
                bool sqlLike = IsSqlLike(_view.TextBuffer.ContentType);

                Logger.Info("Ctrl+Click: contentType='" + contentType + "', sqlLike=" + sqlLike + ", identifier='" + identifier + "'");

                if (!sqlLike || string.IsNullOrEmpty(identifier))
                {
                    return;
                }

                // Suppress the default Ctrl+Click (word selection) so the click is ours.
                e.Handled = true;

                // Fire-and-forget is intentional: the body catches and logs everything.
#pragma warning disable VSSDK007
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        var package = await ToolkitPackage.GetOrLoadAsync();
                        if (package == null)
                        {
                            Logger.Warn("Ctrl+Click: package not available");
                            return;
                        }

                        var report = await SpikeProbe.RunAsync(package.ConnectionProvider, identifier, package.DisposalToken);
                        await package.ShowReportAsync("Spike: " + identifier, "Content type: " + contentType + "\r\n" + report);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Ctrl+Click handling failed", ex);
                    }
                });
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                var count = Interlocked.Increment(ref _failures);
                Logger.Error("Ctrl+Click preprocessing failed (" + count + "/" + MaxFailures + ")", ex);
            }
        }

        private static bool IsSqlLike(IContentType type)
        {
            return type.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0
                   || type.BaseTypes.Any(IsSqlLike);
        }

        private string GetIdentifierAt(MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(_view.VisualElement);
            var line = _view.TextViewLines?.GetTextViewLineContainingYCoordinate(pt.Y + _view.ViewportTop);
            var pos = line?.GetBufferPositionFromXCoordinate(pt.X + _view.ViewportLeft);
            if (pos == null)
            {
                return null;
            }

            // Spike-level tokenizer: multipart names like [dbo].[Table] or db.dbo.Table. Phase 1 uses ScriptDom.
            var snapshotLine = pos.Value.GetContainingLine();
            var text = snapshotLine.GetText();
            int index = pos.Value.Position - snapshotLine.Start.Position;
            if (index >= text.Length || !IsNameChar(text[index]))
            {
                return null;
            }

            int start = index;
            while (start > 0 && IsNameChar(text[start - 1]))
            {
                start--;
            }
            int end = index;
            while (end < text.Length - 1 && IsNameChar(text[end + 1]))
            {
                end++;
            }

            return text.Substring(start, end - start + 1).Trim('.');
        }

        private static bool IsNameChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$' || c == '.' || c == '[' || c == ']';
    }
}
