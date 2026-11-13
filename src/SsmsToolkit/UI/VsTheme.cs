using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Core;
using SsmsToolkit.Presentation.Theme;

namespace SsmsToolkit.UI
{
    /// <summary>Points the Presentation ThemeKeys at SSMS (Visual Studio) theme resources, so light/dark follow SSMS.</summary>
    internal static class VsTheme
    {
        private static bool _applied;

        public static void Apply()
        {
            if (_applied)
            {
                return;
            }
            _applied = true;

            ThemeKeys.Background = EnvironmentColors.ToolWindowBackgroundBrushKey;
            ThemeKeys.Foreground = EnvironmentColors.ToolWindowTextBrushKey;
            ThemeKeys.MutedForeground = EnvironmentColors.SystemGrayTextBrushKey;
            ThemeKeys.Border = EnvironmentColors.ToolWindowBorderBrushKey;
            ThemeKeys.HeaderBackground = EnvironmentColors.CommandBarGradientBeginBrushKey;
            ThemeKeys.HeaderForeground = EnvironmentColors.CommandBarTextActiveBrushKey;
            ThemeKeys.GridHeaderBackground = HeaderColors.DefaultBrushKey;
            ThemeKeys.GridHeaderForeground = HeaderColors.DefaultTextBrushKey;
            ThemeKeys.GridLine = EnvironmentColors.GridLineBrushKey;
            ThemeKeys.SelectionBackground = TreeViewColors.SelectedItemActiveBrushKey;
            ThemeKeys.SelectionForeground = TreeViewColors.SelectedItemActiveTextBrushKey;
            ThemeKeys.Link = EnvironmentColors.ControlLinkTextBrushKey;
            ThemeKeys.LinkHover = EnvironmentColors.ControlLinkTextHoverBrushKey;
            ThemeKeys.InputBackground = EnvironmentColors.ComboBoxBackgroundBrushKey;
            ThemeKeys.InputForeground = EnvironmentColors.ComboBoxTextBrushKey;
            ThemeKeys.InputBorder = EnvironmentColors.ComboBoxBorderBrushKey;
            ThemeKeys.HoverBackground = EnvironmentColors.CommandBarMouseOverBackgroundBeginBrushKey;
            ThemeKeys.TabSelectedBackground = EnvironmentColors.FileTabSelectedBackgroundBrushKey;
            ThemeKeys.TabSelectedForeground = EnvironmentColors.FileTabSelectedTextBrushKey;
        }

        /// <summary>Uses VS-themed styles for controls our own dictionary does not style (ComboBox, CheckBox).</summary>
        public static void UseVsControlStyles(FrameworkElement element)
        {
            TryAdd(element, typeof(ComboBox), VsResourceKeys.ComboBoxStyleKey);
            TryAdd(element, typeof(CheckBox), VsResourceKeys.CheckBoxStyleKey);
        }

        private static void TryAdd(FrameworkElement element, Type type, object key)
        {
            try
            {
                if (element.TryFindResource(key) is Style style)
                {
                    element.Resources[type] = style;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("VS style " + key + " not available: " + ex.Message);
            }
        }
    }
}
