using System.Globalization;
using System.Resources;
using System.Text;
using ConsGHost.Core;

namespace ConsGHost.Host;

public sealed partial class MainForm
{
    private async Task CheckLocalizationAsync(string output)
    {
        CheckLanguageResources();
        var samples = new (string System, string? Preference, string Culture, string Settings)[]
        {
            ("en-GB", null, "en-GB", "Settings"),
            ("zh-CN", null, "zh-CN", "设置"),
            ("zh-SG", null, "zh-SG", "设置"),
            ("ru-RU", null, "ru-RU", "Настройки"),
            ("es-MX", null, "es-MX", "Ajustes"),
            ("fr-CA", null, "fr-CA", "Paramètres"),
            ("de-DE", null, "en", "Settings"),
            ("zh-TW", null, "en", "Settings"),
            ("fr-FR", "unknown", "fr-FR", "Paramètres"),
            ("fr-FR", "en", "en", "Settings"),
            ("en-US", "ZH-hans", "zh-Hans", "设置")
        };
        foreach (var (system, preference, culture, settings) in samples)
        {
            var ui = new UiText(CultureInfo.GetCultureInfo(system), preference);
            if (ui.Culture.Name != culture || ui["SettingsPage"] != settings)
                throw new InvalidOperationException($"Unexpected UI language for {system} with preference {preference}.");
        }

        int originalLanguage = UiText.LanguageIndex(_settings.UiLanguage);
        Size originalSize = Size;
        string originalGame = _game.Text;
        string persistedGame = _settings.GameDirectory;
        string log = _log.Text;
        if (!log.Contains($"Connected to CGH Runtime {CghVersion.Current}.", StringComparison.Ordinal))
            throw new InvalidOperationException("The runtime connection was not logged in English.");
        int expectedModCount = _mods.Rows.Count;
        Size = MinimumSize;
        _game.Text = "unsaved test edit";
        try
        {
            for (int i = 0; i < UiText.Languages.Count; i++)
            {
                string tag = UiText.Languages[i].Tag;
                _pages[2].Navigation.PerformClick();
                _languageOptions[i + 1].PerformClick();
                var saved = Json.Read<HostSettings>(SettingsPath);
                if (_activePageIndex != 2 || !_languageOptions[i + 1].Checked || _ui.Culture.Name != tag ||
                    !_language.Text.StartsWith(UiText.Languages[i].Name, StringComparison.Ordinal) ||
                    saved.UiLanguage != tag || saved.GameDirectory != persistedGame || _game.Text != "unsaved test edit" ||
                    _mods.Rows.Count != expectedModCount || !_log.Text.StartsWith(log, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Changing languages disturbed the UI or settings for {tag}.");

                RefreshMods();
                string expectedScan = $"Scan complete · Mods: {_catalog.Mods.Count} · Issues: {_catalog.Issues.Count}";
                if (!_log.Text.TrimEnd().EndsWith(expectedScan, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The scan message was not logged in English for {tag}.");

                using (var reopened = new MainForm())
                {
                    if (reopened._ui.Culture.Name != tag || !reopened._languageOptions[i + 1].Checked)
                        throw new InvalidOperationException($"The saved language {tag} was not restored on startup.");
                }

                for (int page = 0; page < _pages.Count; page++)
                {
                    _pages[page].Navigation.PerformClick();
                    if (_title.Text != _ui[_pages[page].TitleKey] || _pages[page].Navigation.Text != _title.Text ||
                        _launch.Text != _ui["LaunchGame"] || _mods.Columns[1].HeaderText != _ui["Name"] ||
                        _search.PlaceholderText != _ui["SearchPlaceholder"] || _status.Text != $"●  {_ui["Ready"]}")
                        throw new InvalidOperationException($"Live localization is incomplete for {tag} on page {page}.");
                    await Task.Delay(60);
                    using var bitmap = new Bitmap(Width, Height);
                    DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                    bitmap.Save(Path.Combine(output, $"{tag}-page-{page}.png"));
                }
            }

            _languageOptions[0].PerformClick();
            if (Json.Read<HostSettings>(SettingsPath).UiLanguage != null ||
                _ui.Culture.Name != new UiText(CultureInfo.CurrentUICulture, null).Culture.Name)
                throw new InvalidOperationException("The system language option did not restore automatic selection.");
        }
        finally
        {
            _languageOptions[originalLanguage].PerformClick();
            _game.Text = originalGame;
            Size = originalSize;
            ShowPage(0);
        }
    }

    private static void CheckLanguageResources()
    {
        var resources = new ResourceManager(UiText.Resources.BaseName, typeof(UiText).Assembly);
        var english = ReadResources(CultureInfo.InvariantCulture);
        foreach (var (tag, _) in UiText.Languages.Skip(1))
        {
            var translations = ReadResources(CultureInfo.GetCultureInfo(tag));
            if (translations.Count != english.Count)
                throw new InvalidOperationException($"The {tag} translation does not have the English set of message keys.");

            foreach (var (key, text) in english)
                if (!translations.TryGetValue(key, out var translated) || string.IsNullOrWhiteSpace(translated) ||
                    CompositeFormat.Parse(translated).MinimumArgumentCount != CompositeFormat.Parse(text).MinimumArgumentCount)
                    throw new InvalidOperationException($"Missing or invalid UI message in {tag}: {key}");
        }

        Dictionary<string, string> ReadResources(CultureInfo culture)
        {
            var set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                ?? throw new InvalidOperationException($"Missing UI resources for {culture.Name}.");
            return set.Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        }
    }
}
