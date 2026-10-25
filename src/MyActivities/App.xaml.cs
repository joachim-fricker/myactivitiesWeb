using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace MyActivities;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Zahlen/Datum in Bindings im Windows-Format (z. B. 1.234,5) statt en-US
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        base.OnStartup(e);
    }
}
