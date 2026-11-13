using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsToolkit.Core;
using SsmsToolkit.Features.TableInspector;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.SsmsInterop;
using SsmsToolkit.UI;
using Task = System.Threading.Tasks.Task;

namespace SsmsToolkit.Package
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.PackageString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideBindingPath]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(TableInspectorToolWindow), Style = VsDockStyle.MDI, MultiInstances = true, Transient = true)]
    [ProvideToolWindow(typeof(SettingsToolWindow), Style = VsDockStyle.MDI, MultiInstances = false, Transient = true)]
    public sealed class ToolkitPackage : AsyncPackage
    {
        private bool _autoDisableNotified;

        public static ToolkitPackage Instance { get; private set; }

        internal FeatureRegistry Features { get; } = new FeatureRegistry();

        internal TableInspectorLauncher Launcher { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
            {
                var settings = SettingsStore.Current;
                Logger.MinimumLevel = settings.LogLevel;
                Strings.Language = settings.Language;
                Logger.Info("Package initializing (SsmsToolkit " + typeof(ToolkitPackage).Assembly.GetName().Version + ")");

                var connections = new SsmsConnectionProvider(this);
                Launcher = new TableInspectorLauncher(connections, new InspectorWindowManager(this, Features), Features);
                Features.Register(new TableInspectorFeature());
                Features.FeatureAutoDisabled += OnFeatureAutoDisabled;

                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                VsTheme.Apply();
                await ToolkitCommands.InitializeAsync(this);
                await Features.ApplyAsync(settings);

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
        public static async System.Threading.Tasks.Task<ToolkitPackage> GetOrLoadAsync()
        {
            if (Instance != null)
            {
                return Instance;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (Instance == null && GetGlobalService(typeof(SVsShell)) is IVsShell shell)
            {
                var guid = PackageGuids.Package;
                shell.LoadPackage(ref guid, out _);
            }
            return Instance;
        }

        internal async Task ShowSettingsAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var pane = (SettingsToolWindow)await FindToolWindowAsync(typeof(SettingsToolWindow), 0, create: true, DisposalToken);
            if (pane?.Frame is IVsWindowFrame frame)
            {
                pane.Load(Features, SaveAndApply, ToolkitCommands.OpenLogFolder);
                ErrorHandler.ThrowOnFailure(frame.Show());
            }
        }

        /// <returns>null on success, otherwise an error message for the settings window.</returns>
        private string SaveAndApply(ToolkitSettings settings)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                SettingsStore.Save(settings);
                Logger.MinimumLevel = settings.LogLevel;
                Strings.Language = settings.Language;
                JoinableTaskFactory.Run(() => Features.ApplyAsync(settings));
                Logger.Info("Settings saved");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error("Saving settings failed", ex);
                return ex.Message;
            }
        }

        private void OnFeatureAutoDisabled(IFeature feature)
        {
            if (_autoDisableNotified)
            {
                return;
            }
            _autoDisableNotified = true;

#pragma warning disable VSSDK007 // Fire-and-forget notification; never throws.
            _ = JoinableTaskFactory.RunAsync(() => Notifications.InfoBarAsync(
                Strings.Format("Msg_FeatureDisabled", feature.DisplayName),
                (Strings.Get("Msg_OpenLog"), ToolkitCommands.OpenLogFolder),
                (Strings.Get("Msg_OpenSettings"), () => _ = JoinableTaskFactory.RunAsync(ShowSettingsAsync))));
#pragma warning restore VSSDK007
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
