using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsToolkit.Core;
using SsmsToolkit.Features.Spike;
using SsmsToolkit.SsmsInterop;
using SsmsToolkit.UI;
using Task = System.Threading.Tasks.Task;

namespace SsmsToolkit.Package
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.PackageString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(SpikeToolWindow), Style = VsDockStyle.MDI, MultiInstances = true, Transient = true)]
    public sealed class ToolkitPackage : AsyncPackage
    {
        private static int _nextWindowId;

        public static ToolkitPackage Instance { get; private set; }

        internal SsmsConnectionProvider ConnectionProvider { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
            {
                Logger.Info("Package InitializeAsync started");
                ConnectionProvider = new SsmsConnectionProvider(this);

                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                await SpikeCommands.InitializeAsync(this);

                Instance = this;
                Logger.Info("Package loaded.\r\n" + EnvironmentReport.Build());
            }
            catch (Exception ex)
            {
                // Never let initialization failures surface as an SSMS error dialog.
                Logger.Error("Package initialization failed", ex);
            }
        }

        /// <summary>Returns the loaded package, forcing a load if SSMS has not auto-loaded it yet.</summary>
        public static async Task<ToolkitPackage> GetOrLoadAsync()
        {
            if (Instance != null)
            {
                return Instance;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (Instance == null && Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsShell)) is IVsShell shell)
            {
                var guid = PackageGuids.Package;
                shell.LoadPackage(ref guid, out _);
            }
            return Instance;
        }

        /// <summary>Opens a new spike tool window instance as a document tab and shows the report.</summary>
        public async Task ShowReportAsync(string title, string text)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var id = Interlocked.Increment(ref _nextWindowId);
            var window = await FindToolWindowAsync(typeof(SpikeToolWindow), id, create: true, DisposalToken) as SpikeToolWindow;
            if (window?.Frame is IVsWindowFrame frame)
            {
                window.SetReport(title, text);
                ErrorHandler.ThrowOnFailure(frame.Show());
            }
            else
            {
                Logger.Warn("Could not create spike tool window");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Instance = null;
            }
            base.Dispose(disposing);
        }
    }
}
