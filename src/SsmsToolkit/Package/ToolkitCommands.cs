using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using SsmsToolkit.Core;
using SsmsToolkit.Features.TableInspector;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.SsmsInterop;
using Task = System.Threading.Tasks.Task;

namespace SsmsToolkit.Package
{
    /// <summary>Menu commands: Settings, Open Log Folder, About, Inspect Table (context menu + Ctrl+K, Ctrl+T).</summary>
    internal static class ToolkitCommands
    {
        public static async Task InitializeAsync(ToolkitPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            if (!(await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands))
            {
                Logger.Warn("IMenuCommandService not available; menu commands not registered");
                return;
            }

            Add(commands, PackageIds.SettingsCommand, () => package.ShowSettingsAsync());
            Add(commands, PackageIds.OpenLogFolderCommand, () => { OpenLogFolder(); return Task.CompletedTask; });
            Add(commands, PackageIds.AboutCommand, () => { ShowAbout(package); return Task.CompletedTask; });

            var inspect = Add(commands, PackageIds.InspectTableCommand, () => InspectAtCaretAsync(package));
            inspect.BeforeQueryStatus += (s, e) =>
            {
                // Visible only while the feature is on; the handler checks the active editor itself.
                inspect.Visible = inspect.Enabled = package.Features.IsActive(TableInspectorFeature.FeatureId);
            };
        }

        public static void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(Logger.LogDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Logger.LogDirectory + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn("Open log folder failed", ex);
            }
        }

        private static OleMenuCommand Add(OleMenuCommandService commands, int id, Func<Task> action)
        {
            var cmd = new OleMenuCommand((s, e) =>
            {
#pragma warning disable VSSDK007 // Fire-and-forget; exceptions are caught inside.
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Command 0x" + id.ToString("X4") + " failed", ex);
                    }
                });
#pragma warning restore VSSDK007
            }, new CommandID(PackageGuids.CommandSet, id));
            commands.AddCommand(cmd);
            return cmd;
        }

        private static async Task InspectAtCaretAsync(ToolkitPackage package)
        {
            try
            {
                // Object Explorer (or another tool window) has focus: use the selected table/view node.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (ObjectExplorerInterop.IsToolWindowActive())
                {
                    var target = await ObjectExplorerInterop.GetSelectedTableAsync(package);
                    if (target != null)
                    {
                        await package.Launcher.InspectObjectExplorerAsync(target);
                        return;
                    }
                }

                var view = await ActiveEditor.GetActiveSqlViewAsync(package);
                if (view == null)
                {
                    await UI.Notifications.StatusAsync(Strings.Get("Msg_NoName"));
                    return;
                }

                var caret = view.Caret.Position.BufferPosition;
                int offset = caret.Position;
                // Caret right after a word (just typed it): use the last character of the word.
                if (offset > 0 && (offset == caret.Snapshot.Length || !TableInspectorMouseProcessor.IsIdentifierChar(caret.GetChar()))
                    && TableInspectorMouseProcessor.IsIdentifierChar(new SnapshotPoint(caret.Snapshot, offset - 1).GetChar()))
                {
                    offset--;
                }

                var documents = (await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel)?.GetService<ITextDocumentFactoryService>();
                await package.Launcher.InspectAtAsync(view.TextSnapshot.GetText(), offset, ActiveEditor.GetDocumentMoniker(view, documents));
            }
            catch (Exception ex)
            {
                package.Features.ReportFailure(TableInspectorFeature.FeatureId, "InspectTable command", ex);
            }
        }

        private static void ShowAbout(ToolkitPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string host;
            try
            {
                host = Process.GetCurrentProcess().MainModule?.FileVersionInfo.ProductVersion;
            }
            catch
            {
                host = "?";
            }

            var version = typeof(ToolkitCommands).Assembly.GetName().Version.ToString(3);
            var text = Strings.Format("About_Text", version, "SSMS " + host, Logger.LogDirectory, SettingsStore.FilePath);
            VsShellUtilities.ShowMessageBox(package, text, "SsmsToolkit", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
