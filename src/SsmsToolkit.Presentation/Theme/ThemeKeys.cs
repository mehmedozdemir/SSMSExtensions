namespace SsmsToolkit.Presentation.Theme
{
    /// <summary>
    /// Resource keys every view uses through {DynamicResource {x:Static ThemeKeys.X}}.
    /// The host decides what they point to <b>before</b> any view is created:
    /// inside SSMS they are replaced with Visual Studio theme keys (so light/dark switching just works);
    /// the Playground keeps these string keys and supplies its own dictionaries.
    /// </summary>
    public static class ThemeKeys
    {
        public static object Background { get; set; } = "Toolkit.Background";
        public static object Foreground { get; set; } = "Toolkit.Foreground";
        public static object MutedForeground { get; set; } = "Toolkit.MutedForeground";
        public static object Border { get; set; } = "Toolkit.Border";
        public static object HeaderBackground { get; set; } = "Toolkit.HeaderBackground";
        public static object HeaderForeground { get; set; } = "Toolkit.HeaderForeground";
        public static object GridHeaderBackground { get; set; } = "Toolkit.GridHeaderBackground";
        public static object GridHeaderForeground { get; set; } = "Toolkit.GridHeaderForeground";
        public static object GridLine { get; set; } = "Toolkit.GridLine";
        public static object SelectionBackground { get; set; } = "Toolkit.SelectionBackground";
        public static object SelectionForeground { get; set; } = "Toolkit.SelectionForeground";
        public static object Link { get; set; } = "Toolkit.Link";
        public static object LinkHover { get; set; } = "Toolkit.LinkHover";
        public static object InputBackground { get; set; } = "Toolkit.InputBackground";
        public static object InputForeground { get; set; } = "Toolkit.InputForeground";
        public static object InputBorder { get; set; } = "Toolkit.InputBorder";
        public static object HoverBackground { get; set; } = "Toolkit.HoverBackground";
        public static object TabSelectedBackground { get; set; } = "Toolkit.TabSelectedBackground";
        public static object TabSelectedForeground { get; set; } = "Toolkit.TabSelectedForeground";
    }
}
