using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Core;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Settings;

namespace SsmsToolkit.UI
{
    /// <summary>Single-instance settings document tab. The view model is rebuilt from disk every time it is shown.</summary>
    [Guid("5d0f7a7e-3c1b-4f5e-9a6f-2b9c8e1d4a73")]
    public sealed class SettingsToolWindow : ToolWindowPane
    {
        // Content must be non-null while VS creates the frame (see TableInspectorToolWindow).
        private readonly System.Windows.Controls.ContentControl _host = new System.Windows.Controls.ContentControl();

        public SettingsToolWindow() : base(null)
        {
            Caption = "SsmsToolkit";
            Content = _host;
        }

        internal void Load(FeatureRegistry registry, Func<ToolkitSettings, string> saveAndApply, Action openLogFolder)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SettingsStore.Current.Clone();

            var features = registry.Features
                .Select(f => new FeatureItemViewModel(f.Id, f.DisplayName, f.Description, registry.IsConfiguredEnabled(f, settings), f.CreateSettingsViewModel(settings)))
                .ToList();

            var vm = new SettingsViewModel(features, current =>
            {
                var updated = SettingsStore.Current.Clone();
                updated.Language = current.Language;
                updated.LogLevel = current.LogLevel;
                updated.ProductionServerPatterns = current.ProductionPatterns;
                foreach (var item in current.Features)
                {
                    updated.Features[item.Id] = item.IsEnabled;
                    registry.Features.First(f => f.Id == item.Id).ApplySettingsViewModel(item.Settings, updated);
                }
                return saveAndApply(updated);
            }, openLogFolder)
            {
                Language = settings.Language,
                LogLevel = settings.LogLevel,
                ProductionPatterns = settings.ProductionServerPatterns,
            };

            var view = new SettingsView { DataContext = vm };
            VsTheme.UseVsControlStyles(view);
            Caption = Strings.Get("Settings_Title");
            _host.Content = view;
        }
    }
}
