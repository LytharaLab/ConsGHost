using System.Diagnostics;
using System.Globalization;
using ConsGHost.Core;
using static ConsGHost.Host.HostTheme;

namespace ConsGHost.Host;

public sealed partial class MainForm : Form
{
    private readonly string _root = AppContext.BaseDirectory;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly UiText _ui;
    private readonly List<Action> _textUpdates = [];
    private readonly Exception? _settingsReadError;
    private HostSettings _settings = new();
    private RuntimeClient? _runtime;
    private ScanResult _catalog = new([], []);
    private string _state = "connecting";
    private int _stateProcessId;
    private bool _refreshing, _settingsLoaded, _closing, _closed;
    private string SettingsPath => Path.Combine(_root, "cgh_settings.json");

    public MainForm()
    {
        try { if (File.Exists(SettingsPath)) _settings = Json.Read<HostSettings>(SettingsPath); }
        catch (Exception error) { _settingsReadError = error; }
        _ui = new(CultureInfo.CurrentUICulture, _settings.UiLanguage);
        BuildInterface();
        SetState("connecting");
        _launch.Click += async (_, _) => await LaunchAsync();
        _stop.Click += async (_, _) => await StopAsync();
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += OnClosing;
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (_settingsReadError != null) throw new InvalidDataException("Could not read CGH settings.", _settingsReadError);
            if (!File.Exists(SettingsPath)) Json.Write(SettingsPath, _settings);
            _game.Text = _settings.GameDirectory;
            _dotnet.Text = _settings.DotnetRoot;
            _arguments.Lines = _settings.GameArguments.ToArray();
            _hooks.Checked = _settings.EnableEngineHooks;
            _timeout.Value = Math.Clamp(_settings.StartupTimeoutSeconds, 10, 600);
            RefreshMods();
            RefreshExtras();
            _settingsLoaded = true;
            _runtime = new(_root);
            _runtime.Message += OnMessage;
            await _runtime.ConnectAsync();
            Append($"Connected to CGH Runtime {CghVersion.Current}.", "info");
            _initialized.TrySetResult();
        }
        catch (Exception error)
        {
            Append($"CGH could not initialize: {error}", "error");
            SetState("disconnected");
            _initialized.TrySetException(error);
        }
    }

    private void UpdateLanguageSelector()
    {
        int selected = UiText.LanguageIndex(_settings.UiLanguage);
        _languageOptions[0].Text = _ui["SystemLanguage"];
        for (int i = 0; i < _languageOptions.Count; i++) _languageOptions[i].Checked = i == selected;
        string label = _languageOptions[selected].Text!;
        _language.AccessibleName = $"{_ui["Language"]} · {label}";
        _language.Text = $"{label}  ▾";
    }

    private void ChangeSelectedLanguage(string? preference)
    {
        if (!_settingsLoaded || string.Equals(preference, _settings.UiLanguage, StringComparison.OrdinalIgnoreCase)) return;

        try { Persist(_settings with { UiLanguage = preference }); }
        catch (Exception error)
        {
            ReportError(error);
            return;
        }

        _ui.ChangeLanguage(preference);
        SuspendLayout();
        try
        {
            foreach (var update in _textUpdates) update();
            _settingsFeedback.Text = "";
            UpdatePageTitle();
            UpdateStatusText();
            SetLogExpanded(_logExpanded);
            UpdateLogSummary();
            PopulateMods();
            UpdateExtraSummary();
        }
        finally { ResumeLayout(); }
    }

    private void RefreshMods()
    {
        _catalog = ModCatalog.Scan(Path.Combine(_root, "Mods"));
        PopulateMods();
        foreach (var issue in _catalog.Issues) Append($"Mod folder {issue.Directory}: {issue.Message}", "error");
        Append($"Scan complete · Mods: {_catalog.Mods.Count} · Issues: {_catalog.Issues.Count}", "info");
    }

    private bool IsEnabled(ModSpec mod) => mod.Metadata.Enabled && !_settings.DisabledMods.Contains(mod.Metadata.Id);

    private void PopulateMods()
    {
        string? selectedId = (_mods.CurrentRow?.Tag as ModSpec)?.Metadata.Id;
        var query = _search.Text.Trim();
        DataGridViewRow? selectedRow = null;
        _refreshing = true;
        try
        {
            _mods.Rows.Clear();
            foreach (var mod in _catalog.Mods)
            {
                var m = mod.Metadata;
                if (!m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !m.Id.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                int index = _mods.Rows.Add(IsEnabled(mod), m.Name, m.Version,
                    _ui[m.LoadAsSubprocess ? "Subprocess" : "GameProcess"]);
                var row = _mods.Rows[index];
                row.Tag = mod;
                row.Cells[1].ToolTipText = m.Id;
                if (!m.Enabled)
                {
                    row.Cells[0].ReadOnly = true;
                    row.Cells[0].ToolTipText = _ui["MetadataDisabledHint"];
                }

                if (m.Id == selectedId) selectedRow = row;
            }

            if (_mods.RowCount > 0) _mods.CurrentCell = (selectedRow ?? _mods.Rows[0]).Cells[0];
        }
        finally { _refreshing = false; }

        _modListTitle.Text = _ui.Format("ModList", _mods.RowCount);
        _modEmpty.Text = _ui[_catalog.Mods.Count == 0 ? "ModsEmpty" : "SearchEmpty"];
        _mods.Visible = _mods.RowCount > 0;
        _modEmpty.Visible = _mods.RowCount == 0;
        UpdateModSummary();
        UpdateModDetails();
    }

    private void UpdateModSummary()
    {
        _modCount.Text = _catalog.Mods.Count.ToString(_ui.Culture);
        _enabledCount.Text = _catalog.Mods.Count(IsEnabled).ToString(_ui.Culture);
        _issueCount.Text = _catalog.Issues.Count.ToString(_ui.Culture);
        _issueCount.ForeColor = _catalog.Issues.Count > 0 ? Warning : HostTheme.Text;
    }

    private void UpdateModDetails()
    {
        var mod = _mods.CurrentRow?.Tag as ModSpec;
        _modFolder.Enabled = mod != null;
        _modConfig.Enabled = mod?.HasConfig == true;
        _modDetails.Clear();

        void Line(string text, Font? font = null, Color? color = null)
        {
            _modDetails.SelectionFont = font ?? Body;
            _modDetails.SelectionColor = color ?? HostTheme.Text;
            _modDetails.AppendText(text + Environment.NewLine);
        }

        if (mod == null)
        {
            Line(_ui["SelectMod"], color: Muted);
            return;
        }

        var m = mod.Metadata;
        bool enabled = IsEnabled(mod);
        string status = _ui[!m.Enabled ? "MetadataDisabled" : enabled ? "Enabled" : "Disabled"];
        Line(m.Name, Strong);
        Line($"{m.Id}  ·  v{m.Version}", Small, Muted);
        Line("");
        Line($"{status}  ·  {_ui[m.LoadAsSubprocess ? "Subprocess" : "GameProcess"]}", Strong, enabled ? Good : Muted);
        Line(_ui.Format("ModuleCounts", mod.Managed.Count, mod.Native.Count), Small, Muted);
        if (!string.IsNullOrWhiteSpace(m.Author)) Line(_ui.Format("Author", m.Author), Small, Muted);
        if (!string.IsNullOrWhiteSpace(m.Description))
        {
            Line("");
            Line(m.Description);
        }
        if (mod.Addons.Count > 0)
        {
            Line("");
            Line(_ui["GmodAddons"], Strong);
            foreach (var addon in mod.Addons)
                Line(addon.Ignored ? _ui.Format("IgnoredAddon", addon.Name) : addon.Name, Small, Muted);
        }
        if (m.Dependencies.Count > 0)
        {
            Line("");
            Line(_ui["Dependencies"], Strong);
            foreach (var dependency in m.Dependencies)
            {
                string version = dependency.MinVersion == null ? "" : $"  ≥ {dependency.MinVersion}";
                if (dependency.MaxVersionExclusive != null) version += $"  < {dependency.MaxVersionExclusive}";
                string label = dependency.Id + version;
                Line(dependency.Optional ? _ui.Format("OptionalDependency", label) : label, Small, Muted);
            }
        }

        if (m.LoadAfter.Count > 0)
        {
            Line("");
            Line(_ui["LoadAfter"], Strong);
            foreach (string id in m.LoadAfter) Line(id, Small, Muted);
        }

        _modDetails.Select(0, 0);
        _modDetails.ScrollToCaret();
    }

    private void OpenModConfig()
    {
        if (_mods.CurrentRow?.Tag is not ModSpec mod) return;
        var config = ModConfig.Open(mod.Directory) ?? throw new FileNotFoundException(ModConfig.DefaultFileName);
        using var dialog = new ModConfigDialog(mod.Metadata.Name, config, _ui);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Append($"Mod settings saved · {mod.Metadata.Id}", "info");
    }

    private void OnModChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_refreshing || e.RowIndex < 0 || e.ColumnIndex != 0 || _mods.Rows[e.RowIndex].Tag is not ModSpec mod) return;
        var cell = _mods.Rows[e.RowIndex].Cells[0];
        bool enabled = cell.Value is true;
        try
        {
            var disabled = _settings.DisabledMods.Where(id => id != mod.Metadata.Id).ToList();
            if (!enabled) disabled.Add(mod.Metadata.Id);
            Persist(_settings with { DisabledMods = disabled });
        }
        catch (Exception error)
        {
            RestoreCheckbox(cell, IsEnabled(mod));
            ReportError(error);
        }
        UpdateModSummary();
        UpdateModDetails();
    }

    private void RefreshExtras(int selection = 0)
    {
        _refreshing = true;
        try
        {
            _extras.Rows.Clear();
            foreach (var entry in _settings.ExtraLibraries) _extras.Rows.Add(entry.Enabled, entry.Path, entry.Recursive);
            if (_extras.RowCount > 0) _extras.CurrentCell = _extras.Rows[Math.Min(selection, _extras.RowCount - 1)].Cells[1];
        }
        finally { _refreshing = false; }

        _extras.Visible = _extras.RowCount > 0;
        _extraEmpty.Visible = _extras.RowCount == 0;
        UpdateExtraActions();
        UpdateExtraSummary();
    }

    private void UpdateExtraActions()
    {
        _editExtra.Enabled = _deleteExtra.Enabled = _extras.CurrentRow != null;
    }

    private void UpdateExtraSummary()
    {
        _extraSummary.Text = _ui.Format("ExtraSummary", _settings.ExtraLibraries.Count, _settings.ExtraLibraries.Count(e => e.Enabled));
    }

    private void OnExtraChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_refreshing || e.RowIndex < 0 || e.ColumnIndex is not (0 or 2)) return;
        var entry = _settings.ExtraLibraries[e.RowIndex];
        var cell = _extras.Rows[e.RowIndex].Cells[e.ColumnIndex];
        bool isEnabledCell = e.ColumnIndex == 0;
        var updated = isEnabledCell ? entry with { Enabled = cell.Value is true } : entry with { Recursive = cell.Value is true };
        try { UpdateExtra(e.RowIndex, updated); }
        catch (Exception error)
        {
            RestoreCheckbox(cell, isEnabledCell ? entry.Enabled : entry.Recursive);
            ReportError(error);
        }
        UpdateExtraSummary();
    }

    private void RestoreCheckbox(DataGridViewCell cell, bool value)
    {
        _refreshing = true;
        try { cell.Value = value; }
        finally { _refreshing = false; }
    }

    private void ChooseExtra(bool edit)
    {
        int index = edit ? _extras.CurrentRow?.Index ?? -1 : _settings.ExtraLibraries.Count;
        if (index < 0) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = _ui["ChooseExtraFolder"], UseDescriptionForTitle = true,
            SelectedPath = edit ? _settings.ExtraLibraries[index].Path : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Guard(() =>
        {
            var entry = edit ? _settings.ExtraLibraries[index] with { Path = dialog.SelectedPath } : new() { Path = dialog.SelectedPath };
            UpdateExtra(index, entry);
            RefreshExtras(index);
        });
    }

    private void DeleteExtra()
    {
        if (_extras.CurrentRow is not { } row) return;
        UpdateExtra(row.Index, null);
        RefreshExtras(row.Index);
    }

    private void UpdateExtra(int index, ExtraLibraryDirectory? entry)
    {
        var entries = _settings.ExtraLibraries.ToList();
        if (entry == null) entries.RemoveAt(index);
        else if (index == entries.Count) entries.Add(entry);
        else entries[index] = entry;
        Persist(_settings with { ExtraLibraries = entries });
    }

    private void SaveSettings() => Persist(_settings with
    {
        GameDirectory = _game.Text.Trim(),
        DotnetRoot = _dotnet.Text.Trim(),
        GameArguments = _arguments.Lines.Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
        EnableEngineHooks = _hooks.Checked,
        StartupTimeoutSeconds = (int)_timeout.Value
    });

    private void Persist(HostSettings settings)
    {
        Json.Write(SettingsPath, settings);
        _settings = settings;
    }

    private void CheckLaunchConfiguration()
    {
        SaveSettings();
        var selected = SessionPreparation.Prepare(_root, _settings);
        SetLogExpanded(true);
        Append(selected.Count == 0 ? "Launch settings are valid. No mods are enabled."
            : $"Launch settings are valid · {string.Join(" → ", selected.Select(m => m.Metadata.Id))}", "info");
    }

    private async Task LaunchAsync()
    {
        if (!_launch.Enabled || _runtime == null) return;
        try
        {
            SaveSettings();
            RefreshMods();
            _ = SessionPreparation.Prepare(_root, _settings);
            SetState("starting");
            await _runtime.SendAsync("launch");
        }
        catch (Exception error)
        {
            if (_state == "starting") SetState("idle");
            ReportError(error);
        }
    }

    private async Task StopAsync()
    {
        if (!_stop.Enabled || _runtime == null) return;
        _stop.Enabled = false;
        try { await _runtime.SendAsync("stop"); }
        catch (Exception error)
        {
            _stop.Enabled = _state is "starting" or "running";
            ReportError(error);
        }
    }

    private void OnMessage(WireMessage message)
    {
        if (IsDisposed || !IsHandleCreated || _closing) return;
        BeginInvoke(() =>
        {
            if (_closing) return;
            if (message.Kind == "state") SetState(message.Text ?? "disconnected", message.ProcessId);
            else Append($"{message.Mod ?? message.Endpoint ?? "runtime"}: {message.Text}",
                message.Kind == "fault" ? "error" : message.Level ?? "info");
        });
    }

    private void SetState(string state, int pid = 0)
    {
        _state = state;
        _stateProcessId = pid;
        bool busy = state is "starting" or "running" or "stopping";
        _launch.Enabled = state == "idle";
        _stop.Enabled = state is "starting" or "running";
        _workspace.Enabled = _settingsLoaded && !busy;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        var (text, color) = _state switch
        {
            "connecting" => (_ui["Connecting"], Warning),
            "idle" => (_ui["Ready"], Good),
            "starting" => (_ui["Starting"], Warning),
            "running" => (_ui.Format("Running", _stateProcessId), Good),
            "stopping" => (_ui["Stopping"], Warning),
            _ => (_ui["Disconnected"], Error)
        };
        _status.Text = $"●  {text}";
        _status.ForeColor = color;
    }

    private void Append(string message, string level)
    {
        bool error = level == "error";
        _logEntries++;
        if (error) _logErrors++;
        if (_log.TextLength > 250000)
        {
            _log.Select(0, 100000);
            _log.SelectedText = "";
        }
        _log.Select(_log.TextLength, 0);
        _log.SelectionColor = error ? Error : level == "warning" ? Warning : Muted;
        string time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _log.AppendText($"[{time}]  {message}{Environment.NewLine}");
        _log.ScrollToCaret();
        UpdateLogSummary();
    }

    private void OpenFolder(string path) => Guard(() =>
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    });

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) { ReportError(error); }
    }

    private void ReportError(Exception error)
    {
        SetLogExpanded(true);
        Append($"Operation failed: {error.Message}", "error");
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        Enabled = false;
        try
        {
            if (_settingsLoaded) SaveSettings();
        }
        catch (Exception error)
        {
            _closing = false;
            Enabled = true;
            ReportError(error);
            return;
        }

        try
        {
            if (_runtime != null) await _runtime.DisposeAsync();
        }
        finally
        {
            _closed = true;
            Close();
        }
    }

    /// <summary>Renders the normal desktop pages and checks UI state and persistence without launching the game.</summary>
    public async Task CheckAndRenderAsync(string output)
    {
        await _initialized.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(200);
        if (_mods.Rows.Count != _catalog.Mods.Count || _pages.Count != 3 || !_launch.Enabled ||
            Text != $"ConsGHost {CghVersion.Current}")
            throw new InvalidOperationException("Host initial UI state is inconsistent.");

        Directory.CreateDirectory(output);
        await CheckLocalizationAsync(output);
        if (_mods.Rows.Count > 0 && _mods.Rows[0].Tag is ModSpec mod)
        {
            _search.Text = Guid.NewGuid().ToString("N");
            if (_mods.Rows.Count != 0 || !_modEmpty.Visible || _modFolder.Enabled)
                throw new InvalidOperationException("Mod search did not show its empty state.");

            _search.Text = mod.Metadata.Id;
            var row = _mods.Rows.Cast<DataGridViewRow>().First(row => ReferenceEquals(row.Tag, mod));
            if (mod.Metadata.Enabled)
                CheckToggle(row.Cells[0], settings => !settings.DisabledMods.Contains(mod.Metadata.Id));
            _search.Clear();
        }

        var configFixture = Path.Combine(output, "config-fixture");
        Directory.CreateDirectory(configFixture);
        File.WriteAllText(Path.Combine(configFixture, ModConfig.DefaultFileName), """{"enabled":true,"nested":{"count":1}}""");
        File.WriteAllText(Path.Combine(configFixture, "config.json"), """{"enabled":true,"extra":"keep"}""");
        using (var dialog = new ModConfigDialog("CGH", ModConfig.Open(configFixture)!, _ui))
        {
            dialog.Show(this);
            await dialog.CheckAndRenderAsync(output);
            dialog.Close();
        }

        _pages[1].Navigation.PerformClick();
        if (_extras.Rows.Count == 0 && (!_extraEmpty.Visible || _editExtra.Enabled || _deleteExtra.Enabled))
            throw new InvalidOperationException("Extra DLL empty state is inconsistent.");
        if (_extras.Rows.Count > 0)
        {
            CheckToggle(_extras.Rows[0].Cells[0], settings => settings.ExtraLibraries[0].Enabled);
            CheckToggle(_extras.Rows[0].Cells[2], settings => settings.ExtraLibraries[0].Recursive);
        }

        SaveSettings();
        for (int i = 0; i < _pages.Count; ++i)
        {
            _pages[i].Navigation.PerformClick();
            if (!_pages[i].Content.Visible || _title.Text != _ui[_pages[i].TitleKey])
                throw new InvalidOperationException("Workspace navigation did not activate the expected page.");
            await RenderAsync($"page-{i}.png");
        }

        SetLogExpanded(false);
        if (_log.Visible || _shell.RowStyles[2].Height != LogicalToDeviceUnits(64))
            throw new InvalidOperationException("Log panel did not collapse.");
        Size = MinimumSize;
        await RenderAsync("compact-settings.png");

        File.WriteAllText(Path.Combine(output, "result.txt"),
            "PASS: runtime connected, navigation, search, settings persistence, both config editors, DLL page, log collapse, and English log output in all 5 interface languages checked.");

        void CheckToggle(DataGridViewCell cell, Func<HostSettings, bool> readValue)
        {
            bool initial = cell.Value is true;
            try
            {
                cell.Value = !initial;
                if (readValue(Json.Read<HostSettings>(SettingsPath)) != !initial)
                    throw new InvalidOperationException("Checkbox persistence failed.");
            }
            finally { cell.Value = initial; }
        }

        async Task RenderAsync(string file)
        {
            await Task.Delay(100);
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, file));
        }
    }
}
