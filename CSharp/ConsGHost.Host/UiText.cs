using System.Globalization;
using System.Resources;

namespace ConsGHost.Host;

internal sealed class UiText(CultureInfo systemCulture, string? preferredLanguage)
{
    internal static IReadOnlyList<(string Tag, string Name)> Languages { get; } =
    [
        ("en", "English"),
        ("zh-Hans", "简体中文"),
        ("ru", "Русский"),
        ("es", "Español"),
        ("fr", "Français")
    ];

    internal static ResourceManager Resources { get; } = new("ConsGHost.Host.Localization.Strings", typeof(UiText).Assembly);

    internal CultureInfo Culture { get; private set; } = Resolve(systemCulture, preferredLanguage);

    internal string this[string key] => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException($"Missing UI string: {key}");

    internal string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    internal void ChangeLanguage(string? preference) => Culture = Resolve(systemCulture, preference);

    internal static int LanguageIndex(string? tag)
    {
        for (int i = 0; i < Languages.Count; i++)
            if (string.Equals(Languages[i].Tag, tag, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    private static CultureInfo Resolve(CultureInfo system, string? preference)
    {
        int index = LanguageIndex(preference);
        if (index > 0) return CultureInfo.GetCultureInfo(Languages[index - 1].Tag);

        for (var culture = system; culture.Name.Length > 0; culture = culture.Parent)
        {
            index = LanguageIndex(culture.Name);
            if (index > 0) return system;
        }
        return CultureInfo.GetCultureInfo("en");
    }
}
