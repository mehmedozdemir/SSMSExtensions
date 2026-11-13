using System.Threading.Tasks;
using SsmsToolkit.Core;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Settings;

namespace SsmsToolkit.Features.TableInspector
{
    /// <summary>
    /// Ctrl+Click (or context menu / shortcut) on a table name → details tab.
    /// The MEF mouse processor and the command are always registered; they check <see cref="FeatureRegistry.IsActive"/>.
    /// </summary>
    internal sealed class TableInspectorFeature : IFeature
    {
        public const string FeatureId = "table-inspector";

        public string Id => FeatureId;

        public string DisplayName => Strings.Get("Feature_TableInspector_Name");

        public string Description => Strings.Get("Feature_TableInspector_Description");

        public bool EnabledByDefault => true;

        public Task ActivateAsync() => Task.CompletedTask;

        public Task DeactivateAsync() => Task.CompletedTask;

        public object CreateSettingsViewModel(ToolkitSettings settings)
        {
            var s = settings.TableInspector;
            return new TableInspectorSettingsViewModel
            {
                RowLimit = s.RowLimit,
                CommandTimeoutSeconds = s.CommandTimeoutSeconds,
                OpenMode = s.OpenMode,
                Trigger = s.Trigger,
                ReadUncommitted = s.ReadUncommitted,
            };
        }

        public void ApplySettingsViewModel(object viewModel, ToolkitSettings settings)
        {
            if (viewModel is TableInspectorSettingsViewModel vm)
            {
                vm.Normalize();
                settings.TableInspector.RowLimit = vm.RowLimit;
                settings.TableInspector.CommandTimeoutSeconds = vm.CommandTimeoutSeconds;
                settings.TableInspector.OpenMode = vm.OpenMode;
                settings.TableInspector.Trigger = vm.Trigger;
                settings.TableInspector.ReadUncommitted = vm.ReadUncommitted;
            }
        }
    }
}
