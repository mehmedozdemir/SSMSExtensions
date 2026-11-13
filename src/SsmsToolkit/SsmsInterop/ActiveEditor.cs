using System.Threading.Tasks;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SsmsToolkit.SsmsInterop
{
    /// <summary>Finds the active SQL query editor view.</summary>
    internal static class ActiveEditor
    {
        /// <summary>SSMS's content type for query editors (verified in Phase 0).</summary>
        public const string SqlContentType = "SQL";

        public static async Task<IWpfTextView> GetActiveSqlViewAsync(IAsyncServiceProvider services)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!(await services.GetServiceAsync(typeof(SVsTextManager)) is IVsTextManager textManager)
                || textManager.GetActiveView(1, null, out var vsView) != 0 || vsView == null)
            {
                return null;
            }

            if (!(await services.GetServiceAsync(typeof(SComponentModel)) is IComponentModel componentModel))
            {
                return null;
            }

            var view = componentModel.GetService<IVsEditorAdaptersFactoryService>()?.GetWpfTextView(vsView);
            return view != null && view.TextBuffer.ContentType.IsOfType(SqlContentType) ? view : null;
        }

        /// <summary>The editor's document path; SSMS uses it as the query editor moniker.</summary>
        public static string GetDocumentMoniker(ITextView view, ITextDocumentFactoryService documents)
        {
            return documents != null && documents.TryGetTextDocument(view.TextDataModel.DocumentBuffer, out var doc) ? doc.FilePath : null;
        }
    }
}
