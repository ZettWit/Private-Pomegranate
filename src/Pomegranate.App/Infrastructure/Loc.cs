using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace Pomegranate.App.Infrastructure;

/// <summary>UI text from Strings.resx, so the app can be translated later (spec §2).</summary>
internal static class L
{
    private static readonly ResourceManager Resources = new("Pomegranate.App.Strings", typeof(L).Assembly);

    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary>XAML: <c>Text="{l:Loc Settings_Title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.Get(Key);
}
