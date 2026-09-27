using System.Globalization;

namespace G7Bridge.Core;

public static class UiLanguage
{
    public static string Select(CultureInfo? displayCulture) =>
        string.Equals(displayCulture?.TwoLetterISOLanguageName, "nl", StringComparison.OrdinalIgnoreCase) ? "nl" : "en";

    public static string Text(string english, string dutch) =>
        Select(CultureInfo.CurrentUICulture) == "nl" ? dutch : english;

    public static void Initialize(CultureInfo? displayCulture) =>
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(Select(displayCulture));
}
