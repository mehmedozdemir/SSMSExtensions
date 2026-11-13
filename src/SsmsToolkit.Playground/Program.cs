using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SsmsToolkit.Data.Metadata;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Parsing;
using SsmsToolkit.Data.Sql;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Settings;
using SsmsToolkit.Presentation.TableInspector;
using SsmsToolkit.Presentation.Theme;

namespace SsmsToolkit.Playground
{
    /// <summary>
    /// Usage:
    ///   SsmsToolkit.Playground.exe [--dark] [--lang tr|en] [--tab N] [--settings]
    ///                              [--fake | --cs "connection string"] [--table dbo.Name]
    /// Without --cs, sample data is used.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            bool dark = args.Contains("--dark");
            app.Resources.MergedDictionaries.Add(Themes.Create(dark));

            var lang = Arg(args, "--lang");
            Strings.Language = lang == "tr" ? UiLanguage.Turkish : lang == "en" ? UiLanguage.English : UiLanguage.Auto;

            Window window;
            if (args.Contains("--settings"))
            {
                window = CreateSettingsWindow();
            }
            else
            {
                var cs = Arg(args, "--cs");
                ITableMetadataSource source = cs == null
                    ? (ITableMetadataSource)new FakeMetadataSource()
                    : new SqlTableMetadataSource(new ConnectionTarget(cs, null, null), new MetadataOptions());
                var host = new PlaygroundHost(source);
                var table = Arg(args, "--table") ?? "dbo.PersonelCardDetails";
                window = host.CreateWindow(table, int.TryParse(Arg(args, "--tab"), out var tab) ? tab : 1, args.Contains("--select"));
            }

            window.Background = (System.Windows.Media.Brush)app.Resources[ThemeKeys.Background];
            app.Run(window);
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static Window CreateSettingsWindow()
        {
            var ti = new TableInspectorSettingsViewModel { RowLimit = 200, CommandTimeoutSeconds = 15 };
            var features = new[]
            {
                new FeatureItemViewModel("table-inspector", Strings.Get("Feature_TableInspector_Name"), Strings.Get("Feature_TableInspector_Description"), true, ti),
                new FeatureItemViewModel("result-grid", "Result Grid Tools", "(Phase 3 placeholder)", false, null),
            };
            var vm = new SettingsViewModel(features, s => null, () => { })
            {
                ProductionPatterns = "*prod*;*-prd-*",
            };
            return new Window { Title = "Settings — Playground", Width = 900, Height = 620, Content = new SettingsView { DataContext = vm } };
        }

        private sealed class PlaygroundHost : IInspectorHost
        {
            private readonly ITableMetadataSource _source;

            public PlaygroundHost(ITableMetadataSource source)
            {
                _source = source;
            }

            public Window CreateWindow(string tableName, int tabIndex, bool selectFirstRow = false)
            {
                var name = SqlNameResolver.ParseMultipartName(tableName) ?? new ObjectName(null, null, null, tableName);
                var result = _source.ResolveAsync(new[] { name }, CancellationToken.None).GetAwaiter().GetResult();
                if (result.Status != ResolveStatus.Found)
                {
                    return new Window { Title = "Playground", Width = 500, Height = 200, Content = new TextBlock { Text = result.Status + " " + result.Detail, Margin = new Thickness(20) } };
                }

                var vm = new TableInspectorViewModel(result.Object, _source, this,
                    new InspectorOptions { RowLimit = 200, IsProduction = _source.ServerName.IndexOf("prod", StringComparison.OrdinalIgnoreCase) >= 0 });
                var window = new Window
                {
                    Title = vm.Title + " — Playground",
                    Width = 1100,
                    Height = 640,
                    Content = new TableInspectorView { DataContext = vm },
                };
                window.Closed += (s, e) => vm.Dispose();
                window.Loaded += async (s, e) =>
                {
                    vm.SelectedTab = vm.Tabs[Math.Max(0, Math.Min(vm.Tabs.Count - 1, tabIndex))];
                    await vm.StartAsync();
                    if (selectFirstRow)
                    {
                        // Screenshot helper: show the script panel filled.
                        switch (vm.SelectedTab)
                        {
                            case ColumnsTab c: c.Selected = c.Columns.FirstOrDefault(); break;
                            case IndexListTab i: i.Selected = i.Items.FirstOrDefault(); break;
                            case ConstraintsTab k: k.Selected = k.Items.FirstOrDefault(); break;
                            case ForeignKeysTab f: f.SelectedOutgoing = f.Outgoing.FirstOrDefault(); break;
                            case TriggersTab t: t.Selected = t.Items.FirstOrDefault(); break;
                            case DependenciesTab d: d.SelectedUsedBy = d.UsedBy.FirstOrDefault(); break;
                        }
                    }
                };
                return window;
            }

            public Task OpenTableAsync(ObjectName name)
            {
                var w = CreateWindow(name.ToString(), 1, false);
                w.Background = (System.Windows.Media.Brush)Application.Current.Resources[ThemeKeys.Background];
                w.Show();
                return Task.CompletedTask;
            }

            public Task OpenQueryAsync(string sql, string database)
            {
                MessageBox.Show(sql, "Open in new query (" + database + ")");
                return Task.CompletedTask;
            }

            public void CopyToClipboard(string text) => Clipboard.SetText(text ?? string.Empty);

            public void ReportError(string context, Exception exception) =>
                Console.Error.WriteLine(context + ": " + exception);
        }
    }
}
