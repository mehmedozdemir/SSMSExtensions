using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Core;
using SsmsToolkit.Package;
using Task = System.Threading.Tasks.Task;

namespace SsmsToolkit.Features.Spike
{
    /// <summary>Phase 0 menu commands under Extensions/Tools > SsmsToolkit.</summary>
    internal static class SpikeCommands
    {
        public static async Task InitializeAsync(ToolkitPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            if (!(await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands))
            {
                Logger.Warn("IMenuCommandService not available; menu commands not registered");
                return;
            }

            Add(commands, PackageIds.ShowConnectionCommand, () => RunProbe(package));
            Add(commands, PackageIds.OpenTestWindowCommand, () => OpenTestWindow(package));
            Add(commands, PackageIds.OpenLogFolderCommand, OpenLogFolder);
            Logger.Info("Menu commands registered");
        }

        private static void Add(OleMenuCommandService commands, int id, Action action)
        {
            var cmd = new MenuCommand((s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error("Command 0x" + id.ToString("X4") + " failed", ex);
                }
            }, new CommandID(PackageGuids.CommandSet, id));
            commands.AddCommand(cmd);
        }

        private static void RunProbe(ToolkitPackage package)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var report = await SpikeProbe.RunAsync(package.ConnectionProvider, null, package.DisposalToken);
                    await package.ShowReportAsync("Spike: Active Connection", report);
                }
                catch (Exception ex)
                {
                    Logger.Error("Show Active Connection failed", ex);
                }
            });
        }

        private static void OpenTestWindow(ToolkitPackage package)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await package.ShowReportAsync("Spike: Test Window", "If you can read this in a document tab, the tool window check passed.\r\n\r\n" + EnvironmentReport.Build());
                }
                catch (Exception ex)
                {
                    Logger.Error("Open Test Window failed", ex);
                }
            });
        }

        private static void OpenLogFolder()
        {
            Directory.CreateDirectory(Logger.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Logger.LogDirectory + "\"") { UseShellExecute = true });
        }
    }
}
