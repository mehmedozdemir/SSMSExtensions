using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SsmsToolkit.Data.Dashboard;

namespace SsmsToolkit.Presentation.Dashboard
{
    /// <summary>Severity → colour. Fixed colours that read on both light and dark backgrounds.</summary>
    public sealed class SeverityToBrushConverter : IValueConverter
    {
        private static readonly Brush Critical = Freeze(Color.FromRgb(0xE0, 0x4F, 0x4F));
        private static readonly Brush Warning = Freeze(Color.FromRgb(0xE0, 0x9B, 0x2D));
        private static readonly Brush Info = Freeze(Color.FromRgb(0x3A, 0x96, 0xDD));
        private static readonly Brush Ok = Freeze(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush Unavailable = Freeze(Color.FromRgb(0x8A, 0x8A, 0x8A));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as HealthSeverity?)
            {
                case HealthSeverity.Critical: return Critical;
                case HealthSeverity.Warning: return Warning;
                case HealthSeverity.Ok: return Ok;
                case HealthSeverity.Unavailable: return Unavailable;
                default: return Info;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Severity → a short symbol shown in the coloured badge.</summary>
    public sealed class SeverityToGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as HealthSeverity?)
            {
                case HealthSeverity.Critical: return "!";
                case HealthSeverity.Warning: return "!";
                case HealthSeverity.Ok: return "✓";
                case HealthSeverity.Unavailable: return "–";
                default: return "i";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
