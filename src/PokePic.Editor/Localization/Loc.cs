using System.Globalization;
using System.Resources;
using Avalonia.Markup.Xaml;

namespace PokePic.Editor.Localization;

/// <summary>
/// Localized UI strings from Strings.resx (English) and Strings.it.resx (Italian).
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Resources = new("PokePic.Editor.Localization.Strings", typeof(Strings).Assembly);

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new KeyNotFoundException($"Missing localized string \"{key}\".");

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);
}

/// <summary>
/// XAML usage: <c>Text="{loc:Loc Save}"</c>.
/// </summary>
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
