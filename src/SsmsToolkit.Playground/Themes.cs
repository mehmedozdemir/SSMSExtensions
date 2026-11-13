using System.Windows;
using System.Windows.Media;
using SsmsToolkit.Presentation.Theme;

namespace SsmsToolkit.Playground
{
    /// <summary>Approximations of the SSMS light and dark themes for the default (string) ThemeKeys.</summary>
    internal static class Themes
    {
        public static ResourceDictionary Create(bool dark)
        {
            var d = new ResourceDictionary();
            void Add(object key, string color) => d[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

            if (dark)
            {
                Add(ThemeKeys.Background, "#1F1F1F");
                Add(ThemeKeys.Foreground, "#E0E0E0");
                Add(ThemeKeys.MutedForeground, "#9A9A9A");
                Add(ThemeKeys.Border, "#3F3F46");
                Add(ThemeKeys.HeaderBackground, "#2D2D30");
                Add(ThemeKeys.HeaderForeground, "#E0E0E0");
                Add(ThemeKeys.GridHeaderBackground, "#2D2D30");
                Add(ThemeKeys.GridHeaderForeground, "#E0E0E0");
                Add(ThemeKeys.GridLine, "#333337");
                Add(ThemeKeys.SelectionBackground, "#264F78");
                Add(ThemeKeys.SelectionForeground, "#FFFFFF");
                Add(ThemeKeys.Link, "#4FA3F7");
                Add(ThemeKeys.LinkHover, "#8CC4FF");
                Add(ThemeKeys.InputBackground, "#2B2B2B");
                Add(ThemeKeys.InputForeground, "#E0E0E0");
                Add(ThemeKeys.InputBorder, "#4A4A4F");
                Add(ThemeKeys.HoverBackground, "#3E3E40");
                Add(ThemeKeys.TabSelectedBackground, "#007ACC");
                Add(ThemeKeys.TabSelectedForeground, "#FFFFFF");
            }
            else
            {
                Add(ThemeKeys.Background, "#FFFFFF");
                Add(ThemeKeys.Foreground, "#1E1E1E");
                Add(ThemeKeys.MutedForeground, "#6D6D6D");
                Add(ThemeKeys.Border, "#CCCEDB");
                Add(ThemeKeys.HeaderBackground, "#EEEEF2");
                Add(ThemeKeys.HeaderForeground, "#1E1E1E");
                Add(ThemeKeys.GridHeaderBackground, "#F3F3F3");
                Add(ThemeKeys.GridHeaderForeground, "#1E1E1E");
                Add(ThemeKeys.GridLine, "#E5E5E5");
                Add(ThemeKeys.SelectionBackground, "#CCE4F7");
                Add(ThemeKeys.SelectionForeground, "#000000");
                Add(ThemeKeys.Link, "#0066CC");
                Add(ThemeKeys.LinkHover, "#003F7F");
                Add(ThemeKeys.InputBackground, "#FFFFFF");
                Add(ThemeKeys.InputForeground, "#1E1E1E");
                Add(ThemeKeys.InputBorder, "#ADADAD");
                Add(ThemeKeys.HoverBackground, "#E5F1FB");
                Add(ThemeKeys.TabSelectedBackground, "#007ACC");
                Add(ThemeKeys.TabSelectedForeground, "#FFFFFF");
            }
            return d;
        }
    }
}
