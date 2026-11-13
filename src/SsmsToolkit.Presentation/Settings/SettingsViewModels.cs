using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Mvvm;

namespace SsmsToolkit.Presentation.Settings
{
    public enum InspectorOpenMode
    {
        DocumentTab,
        Popup,
    }

    public enum ClickTrigger
    {
        CtrlClick,
        CtrlAltClick,
        Disabled,
    }

    public enum LogLevel
    {
        Error,
        Warning,
        Info,
        Debug,
    }

    /// <summary>A feature row in the settings window: on/off switch plus its own settings view model.</summary>
    public sealed class FeatureItemViewModel : ObservableObject
    {
        private bool _isEnabled;

        public FeatureItemViewModel(string id, string displayName, string description, bool isEnabled, object settings)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            _isEnabled = isEnabled;
            Settings = settings;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }

        /// <summary>Feature-specific settings view model, rendered by a DataTemplate; null if none.</summary>
        public object Settings { get; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => Set(ref _isEnabled, value);
        }
    }

    public sealed class TableInspectorSettingsViewModel : ObservableObject
    {
        private int _rowLimit = 200;
        private int _commandTimeoutSeconds = 15;
        private InspectorOpenMode _openMode;
        private ClickTrigger _trigger;
        private bool _readUncommitted;

        public IReadOnlyList<Option<InspectorOpenMode>> OpenModes { get; } = new[]
        {
            new Option<InspectorOpenMode>(InspectorOpenMode.DocumentTab, Strings.Get("TI_OpenMode_Tab")),
            new Option<InspectorOpenMode>(InspectorOpenMode.Popup, Strings.Get("TI_OpenMode_Popup")),
        };

        public IReadOnlyList<Option<ClickTrigger>> Triggers { get; } = new[]
        {
            new Option<ClickTrigger>(ClickTrigger.CtrlClick, Strings.Get("TI_Trigger_CtrlClick")),
            new Option<ClickTrigger>(ClickTrigger.CtrlAltClick, Strings.Get("TI_Trigger_CtrlAltClick")),
            new Option<ClickTrigger>(ClickTrigger.Disabled, Strings.Get("TI_Trigger_Disabled")),
        };

        public int RowLimit
        {
            get => _rowLimit;
            set => Set(ref _rowLimit, value);
        }

        public int CommandTimeoutSeconds
        {
            get => _commandTimeoutSeconds;
            set => Set(ref _commandTimeoutSeconds, value);
        }

        public InspectorOpenMode OpenMode
        {
            get => _openMode;
            set => Set(ref _openMode, value);
        }

        public ClickTrigger Trigger
        {
            get => _trigger;
            set => Set(ref _trigger, value);
        }

        public bool ReadUncommitted
        {
            get => _readUncommitted;
            set => Set(ref _readUncommitted, value);
        }

        /// <summary>Clamps values to supported ranges.</summary>
        public void Normalize()
        {
            RowLimit = Math.Max(1, Math.Min(Data.Sql.DataQuery.MaxRowLimit, RowLimit));
            CommandTimeoutSeconds = Math.Max(1, Math.Min(600, CommandTimeoutSeconds));
        }
    }

    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly Func<SettingsViewModel, string> _save;
        private FeatureItemViewModel _selectedFeature;
        private LogLevel _logLevel = LogLevel.Info;
        private string _productionPatterns = string.Empty;
        private UiLanguage _language;
        private string _status;

        /// <param name="save">Persists and applies the settings; returns null on success or an error message.</param>
        /// <param name="openLogFolder">Optional action behind the "Open log folder" button.</param>
        public SettingsViewModel(IEnumerable<FeatureItemViewModel> features, Func<SettingsViewModel, string> save, Action openLogFolder = null)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            Features = new ObservableCollection<FeatureItemViewModel>(features ?? Enumerable.Empty<FeatureItemViewModel>());
            _selectedFeature = Features.FirstOrDefault();
            SaveCommand = new RelayCommand(_ => Save());
            OpenLogFolderCommand = new RelayCommand(_ => openLogFolder?.Invoke(), _ => openLogFolder != null);
        }

        public ObservableCollection<FeatureItemViewModel> Features { get; }

        public FeatureItemViewModel SelectedFeature
        {
            get => _selectedFeature;
            set => Set(ref _selectedFeature, value);
        }

        public IReadOnlyList<Option<LogLevel>> LogLevels { get; } = new[]
        {
            new Option<LogLevel>(LogLevel.Error, Strings.Get("LogLevel_Error")),
            new Option<LogLevel>(LogLevel.Warning, Strings.Get("LogLevel_Warning")),
            new Option<LogLevel>(LogLevel.Info, Strings.Get("LogLevel_Info")),
            new Option<LogLevel>(LogLevel.Debug, Strings.Get("LogLevel_Debug")),
        };

        public IReadOnlyList<Option<UiLanguage>> Languages { get; } = new[]
        {
            new Option<UiLanguage>(UiLanguage.Auto, Strings.Get("Lang_Auto")),
            new Option<UiLanguage>(UiLanguage.English, Strings.Get("Lang_English")),
            new Option<UiLanguage>(UiLanguage.Turkish, Strings.Get("Lang_Turkish")),
        };

        public LogLevel LogLevel
        {
            get => _logLevel;
            set => Set(ref _logLevel, value);
        }

        public string ProductionPatterns
        {
            get => _productionPatterns;
            set => Set(ref _productionPatterns, value ?? string.Empty);
        }

        public UiLanguage Language
        {
            get => _language;
            set => Set(ref _language, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        public ICommand SaveCommand { get; }

        public ICommand OpenLogFolderCommand { get; }

        public void Save()
        {
            foreach (var f in Features)
            {
                (f.Settings as TableInspectorSettingsViewModel)?.Normalize();
            }

            string error;
            try
            {
                error = _save(this);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            Status = error == null ? Strings.Get("Settings_Saved") : Strings.Format("Settings_SaveFailed", error);
        }
    }
}
