using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Settings;

namespace SsmsToolkit.Core
{
    /// <summary>%LocalAppData%\SsmsToolkit\settings.json. Contains no secrets.</summary>
    [DataContract]
    internal sealed class ToolkitSettings
    {
        [DataMember(Order = 0)] public int Version { get; set; } = 1;
        [DataMember(Order = 1)] public UiLanguage Language { get; set; } = UiLanguage.Auto;
        [DataMember(Order = 2)] public LogLevel LogLevel { get; set; } = LogLevel.Info;
        [DataMember(Order = 3)] public string ProductionServerPatterns { get; set; } = "*prod*";

        /// <summary>Feature id → enabled. Features not listed use their default.</summary>
        [DataMember(Order = 4)] public Dictionary<string, bool> Features { get; set; } = new Dictionary<string, bool>();

        [DataMember(Order = 5)] public TableInspectorSettings TableInspector { get; set; } = new TableInspectorSettings();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // DataContractJsonSerializer skips constructors: restore defaults for members missing from the file.
            Version = 1;
            ProductionServerPatterns = "*prod*";
            LogLevel = LogLevel.Info;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Features = Features ?? new Dictionary<string, bool>();
            TableInspector = TableInspector ?? new TableInspectorSettings();
            ProductionServerPatterns = ProductionServerPatterns ?? string.Empty;
        }

        public ToolkitSettings Clone()
        {
            using (var ms = new MemoryStream())
            {
                Serializer.WriteObject(ms, this);
                ms.Position = 0;
                return (ToolkitSettings)Serializer.ReadObject(ms);
            }
        }

        internal static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(
            typeof(ToolkitSettings),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
    }

    [DataContract]
    internal sealed class TableInspectorSettings
    {
        [DataMember(Order = 0)] public int RowLimit { get; set; } = 200;
        [DataMember(Order = 1)] public int CommandTimeoutSeconds { get; set; } = 15;
        [DataMember(Order = 2)] public InspectorOpenMode OpenMode { get; set; } = InspectorOpenMode.DocumentTab;
        [DataMember(Order = 3)] public ClickTrigger Trigger { get; set; } = ClickTrigger.CtrlClick;
        [DataMember(Order = 4)] public bool ReadUncommitted { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // DataContractJsonSerializer skips constructors: restore defaults for missing members.
            RowLimit = 200;
            CommandTimeoutSeconds = 15;
        }
    }

    /// <summary>Loads/saves settings. Corrupt files are backed up and replaced with defaults.</summary>
    internal static class SettingsStore
    {
        private static readonly object Sync = new object();
        private static ToolkitSettings _current;

        public static string FilePath { get; } = Path.Combine(Logger.RootDirectory, "settings.json");

        public static event EventHandler Changed;

        /// <summary>A snapshot; callers must not modify it (use <see cref="Save"/>).</summary>
        public static ToolkitSettings Current
        {
            get
            {
                lock (Sync)
                {
                    return _current ?? (_current = Load());
                }
            }
        }

        public static void Save(ToolkitSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var temp = FilePath + ".tmp";
                using (var fs = File.Create(temp))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    ToolkitSettings.Serializer.WriteObject(writer, settings);
                }

                if (File.Exists(FilePath))
                {
                    File.Replace(temp, FilePath, null);
                }
                else
                {
                    File.Move(temp, FilePath);
                }

                _current = settings.Clone();
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static ToolkitSettings Load()
        {
            if (!File.Exists(FilePath))
            {
                return new ToolkitSettings();
            }

            try
            {
                using (var fs = File.OpenRead(FilePath))
                {
                    return (ToolkitSettings)ToolkitSettings.Serializer.ReadObject(fs) ?? new ToolkitSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("settings.json is invalid; using defaults. A copy was kept as settings.json.bad", ex);
                try
                {
                    File.Copy(FilePath, FilePath + ".bad", overwrite: true);
                }
                catch
                {
                    // Best effort.
                }
                return new ToolkitSettings();
            }
        }
    }
}
