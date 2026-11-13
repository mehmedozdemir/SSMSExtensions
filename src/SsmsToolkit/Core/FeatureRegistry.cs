using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SsmsToolkit.Core
{
    /// <summary>A toggleable SsmsToolkit feature.</summary>
    internal interface IFeature
    {
        /// <summary>Stable id used in settings.json, e.g. "table-inspector".</summary>
        string Id { get; }

        string DisplayName { get; }

        string Description { get; }

        bool EnabledByDefault { get; }

        /// <summary>Hooks events/commands. Called on the UI thread.</summary>
        Task ActivateAsync();

        /// <summary>Unhooks everything. Called on the UI thread.</summary>
        Task DeactivateAsync();

        /// <summary>Settings view model shown in the settings window, or null.</summary>
        object CreateSettingsViewModel(ToolkitSettings settings);

        /// <summary>Copies values from the settings view model back into <paramref name="settings"/>.</summary>
        void ApplySettingsViewModel(object viewModel, ToolkitSettings settings);
    }

    /// <summary>
    /// Owns feature on/off state. A feature that throws unexpectedly <see cref="MaxFailures"/> times is turned off
    /// for the rest of the session (not persisted), logged, and the user is told once.
    /// </summary>
    internal sealed class FeatureRegistry
    {
        public const int MaxFailures = 3;

        private readonly List<IFeature> _features = new List<IFeature>();
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _disabledByFailure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();

        /// <summary>Raised (once per feature) after a feature was turned off because of failures.</summary>
        public event Action<IFeature> FeatureAutoDisabled;

        public IReadOnlyList<IFeature> Features => _features;

        public void Register(IFeature feature)
        {
            if (_features.Any(f => string.Equals(f.Id, feature.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Duplicate feature id: " + feature.Id);
            }
            _features.Add(feature);
        }

        public bool IsConfiguredEnabled(IFeature feature, ToolkitSettings settings) =>
            settings.Features.TryGetValue(feature.Id, out var enabled) ? enabled : feature.EnabledByDefault;

        /// <summary>True when the feature is on and has not been turned off by failures.</summary>
        public bool IsActive(string featureId)
        {
            lock (_sync)
            {
                return _active.Contains(featureId);
            }
        }

        /// <summary>Activates/deactivates features to match the settings. Called on the UI thread.</summary>
        public async Task ApplyAsync(ToolkitSettings settings)
        {
            foreach (var feature in _features)
            {
                bool want = IsConfiguredEnabled(feature, settings);
                bool isActive = IsActive(feature.Id);

                if (want && !isActive)
                {
                    lock (_sync)
                    {
                        // Re-enabling in settings gives a feature a fresh start.
                        _disabledByFailure.Remove(feature.Id);
                        _failures.Remove(feature.Id);
                    }
                    await TryAsync(feature, "activate", feature.ActivateAsync, activated: true);
                }
                else if (!want && isActive)
                {
                    await TryAsync(feature, "deactivate", feature.DeactivateAsync, activated: false);
                }
            }
        }

        /// <summary>Records an unexpected exception from a feature entry point. Never throws.</summary>
        public void ReportFailure(string featureId, string context, Exception ex)
        {
            Logger.Error("Feature '" + featureId + "' failed in " + context, ex);

            IFeature disabled = null;
            lock (_sync)
            {
                _failures.TryGetValue(featureId, out var count);
                _failures[featureId] = ++count;
                if (count >= MaxFailures && _active.Remove(featureId))
                {
                    _disabledByFailure.Add(featureId);
                    disabled = _features.FirstOrDefault(f => string.Equals(f.Id, featureId, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (disabled != null)
            {
                Logger.Error("Feature '" + featureId + "' was turned off after " + MaxFailures + " failures.");
                try
                {
                    _ = disabled.DeactivateAsync();
                    FeatureAutoDisabled?.Invoke(disabled);
                }
                catch (Exception notifyEx)
                {
                    Logger.Error("Turning off feature '" + featureId + "' failed", notifyEx);
                }
            }
        }

        private async Task TryAsync(IFeature feature, string what, Func<Task> action, bool activated)
        {
            try
            {
                await action();
                lock (_sync)
                {
                    if (activated)
                    {
                        _active.Add(feature.Id);
                    }
                    else
                    {
                        _active.Remove(feature.Id);
                    }
                }
                Logger.Info("Feature '" + feature.Id + "' " + (activated ? "activated" : "deactivated"));
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _active.Remove(feature.Id);
                }
                Logger.Error("Could not " + what + " feature '" + feature.Id + "'", ex);
            }
        }
    }
}
