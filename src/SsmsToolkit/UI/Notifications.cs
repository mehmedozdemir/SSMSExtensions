using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsToolkit.Core;

namespace SsmsToolkit.UI
{
    /// <summary>Status bar text and the main-window info bar. Never throws.</summary>
    internal static class Notifications
    {
        public static async Task StatusAsync(string text)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar bar)
                {
                    bar.IsFrozen(out int frozen);
                    if (frozen == 0)
                    {
                        bar.SetText(text ?? string.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("Status bar update failed: " + ex.Message);
            }
        }

        /// <summary>Shows a warning info bar on the SSMS main window with optional action links.</summary>
        public static async Task InfoBarAsync(string text, params (string Text, Action OnClick)[] actions)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsShell)) is IVsShell shell)
                    || shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObj) != 0
                    || !(hostObj is IVsInfoBarHost host)
                    || !(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsInfoBarUIFactory)) is IVsInfoBarUIFactory factory))
                {
                    await StatusAsync(text);
                    return;
                }

                var items = new IVsInfoBarActionItem[actions.Length];
                for (int i = 0; i < actions.Length; i++)
                {
                    items[i] = new InfoBarHyperlink(actions[i].Text, actions[i].OnClick);
                }

                var model = new InfoBarModel(text, items, KnownMonikers.StatusWarning, isCloseButtonVisible: true);
                var element = factory.CreateInfoBar(model);
                element.Advise(new InfoBarEvents(), out _);
                host.AddInfoBar(element);
            }
            catch (Exception ex)
            {
                Logger.Warn("Info bar failed", ex);
            }
        }

        private sealed class InfoBarEvents : IVsInfoBarUIEvents
        {
            public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
            {
            }

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                try
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    (actionItem.ActionContext as Action)?.Invoke();
                }
                catch (Exception ex)
                {
                    Logger.Warn("Info bar action failed", ex);
                }
            }
        }
    }
}
