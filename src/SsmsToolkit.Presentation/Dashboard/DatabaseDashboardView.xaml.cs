using System.Globalization;
using System.Windows.Controls;
using System.Windows.Markup;

namespace SsmsToolkit.Presentation.Dashboard
{
    public partial class DatabaseDashboardView : UserControl
    {
        public DatabaseDashboardView()
        {
            InitializeComponent();
            // Bindings with StringFormat (dates, N1) use Language, which WPF defaults to en-US.
            Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        }
    }
}
