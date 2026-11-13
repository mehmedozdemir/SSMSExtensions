using System;
using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace SsmsToolkit.Presentation.Localization
{
    public enum UiLanguage
    {
        Auto,
        English,
        Turkish,
    }

    /// <summary>
    /// All user-facing text. English is the fallback; Turkish is used when selected (or when SSMS runs in Turkish
    /// with <see cref="UiLanguage.Auto"/>). Changes apply to windows opened afterwards.
    /// </summary>
    public static class Strings
    {
        private static readonly ResourceManager English =
            new ResourceManager("SsmsToolkit.Presentation.Localization.Strings", typeof(Strings).Assembly);

        private static readonly ResourceManager Turkish =
            new ResourceManager("SsmsToolkit.Presentation.Localization.StringsTurkish", typeof(Strings).Assembly);

        public static UiLanguage Language { get; set; } = UiLanguage.Auto;

        public static bool IsTurkish =>
            Language == UiLanguage.Turkish
            || (Language == UiLanguage.Auto && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr");

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            try
            {
                var value = IsTurkish ? Turkish.GetString(key, CultureInfo.InvariantCulture) : null;
                return value ?? English.GetString(key, CultureInfo.InvariantCulture) ?? key;
            }
            catch (MissingManifestResourceException)
            {
                return key;
            }
        }

        public static string Format(string key, params object[] args)
        {
            var culture = IsTurkish ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.GetCultureInfo("en-US");
            return string.Format(culture, Get(key), args);
        }
    }

    /// <summary>XAML: Text="{l:Loc Tab_Summary}".</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
    }
}
