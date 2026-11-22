using ConsGHost.Core;
using static ConsGHost.Host.HostTheme;

namespace ConsGHost.Host;

public sealed partial class MainForm
{
    private readonly Button _launch = Action("", primary: true);
    private readonly Button _stop = Action("");
    private readonly Label _status = Caption("", Small, Warning);
    private readonly Label _section = Caption("", Small, Accent);
    private readonly Label _title = Caption("", Heading);
    private readonly Label _subtitle = Caption("", Body, Muted);
    private readonly Panel _workspace = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly List<(Control Content, Button Navigation, string TitleKey, string DescriptionKey)> _pages = [];
    private readonly TableLayoutPanel _shell = Stack();
    private readonly TableLayoutPanel _logPanel = Stack();
    private readonly Button _logToggle = Action("");
    private readonly Button _logCopy = Action("");
    private readonly Button _logClear = Action("");
    private readonly Label _logSummary = Caption("", Small, Muted);
    private readonly RichTextBox _log = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = true,
        BackColor = Input, ForeColor = HostTheme.Text, Font = Mono, BorderStyle = BorderStyle.None,
        Margin = Padding.Empty
    };

    private readonly DataGridView _mods = Grid("");
    private readonly DataGridView _extras = Grid("");
    private readonly TextBox _search = InputBox();
    private readonly Label _modCount = Caption("0", Metric);
    private readonly Label _enabledCount = Caption("0", Metric, Good);
    private readonly Label _issueCount = Caption("0", Metric);
    private readonly Label _modListTitle = Caption("", Strong);
    private readonly Label _modEmpty = Caption("", Body, Muted);
    private readonly Label _extraEmpty = Caption("", Body, Muted);
    private readonly Label _extraSummary = Caption("", Body, Muted);
    private readonly RichTextBox _modDetails = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None,
        BackColor = Surface, ForeColor = HostTheme.Text, Font = Body, Margin = Padding.Empty
    };
    private readonly Button _modFolder = Action("");
    private readonly Button _modConfig = Action("");
    private readonly Button _editExtra = Action("");
    private readonly Button _deleteExtra = Action("");
    private readonly TextBox _game = InputBox();
    private readonly TextBox _dotnet = InputBox();
    private readonly TextBox _arguments = InputBox();
    private readonly Button _language = Action("");
    private readonly List<ToolStripMenuItem> _languageOptions = [];
    private readonly CheckBox _hooks = new()
    {
        AutoSize = true, Dock = DockStyle.Fill,
        ForeColor = HostTheme.Text, Margin = Padding.Empty
    };
    private readonly NumericUpDown _timeout = new()
    {
        Minimum = 10, Maximum = 600, Value = 90, Width = 104,
        BackColor = Input, ForeColor = HostTheme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty
    };
    private readonly Label _settingsFeedback = Caption("", Small, Good);
    private int _activePageIndex = -1;
    private bool _logExpanded = true;
    private int _logEntries;
    private int _logErrors;

    private void BuildInterface()
    {
        SuspendLayout();
        Text = $"ConsGHost {CghVersion.Current}";
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Body;
        BackColor = Canvas;
        ForeColor = HostTheme.Text;
        ClientSize = new(1260, 794);
        MinimumSize = new(960, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BindControl(_launch, "LaunchGame");
        BindControl(_stop, "StopSession");
        BindControl(_logCopy, "Copy");
        BindControl(_logClear, "Clear");
        BindControl(_modFolder, "OpenModFolder");
        BindControl(_modConfig, "Configure");
        BindControl(_editExtra, "EditFolder");
        BindControl(_deleteExtra, "RemoveFolder");
        BindControl(_extraEmpty, "ExtraEmpty");
        BindControl(_hooks, "EnableGameFrame");
        BindText(() =>
        {
            _log.AccessibleName = _ui["HostLog"];
            _mods.AccessibleName = _ui["ModsFound"];
            _extras.AccessibleName = _ui["DllFolder"];
            _modDetails.AccessibleName = _ui["ModDetails"];
            _search.AccessibleName = _ui["SearchMods"];
            _search.PlaceholderText = _ui["SearchPlaceholder"];
            _game.AccessibleName = _ui["GameFolder"];
            _dotnet.AccessibleName = _ui["DotnetFolder"];
            _arguments.AccessibleName = _ui["GameArguments"];
            _timeout.AccessibleName = _ui["Timeout"];
        });

        var navigation = Stack();
        navigation.BackColor = Sidebar;
        navigation.Padding = new(16, 18, 16, 16);
        var brand = Stack();
        brand.Padding = new(8, 0, 0, 0);
        Row(brand, Caption("CGH", Heading), SizeType.Absolute, 36);
        Row(brand, Caption("ConsGHost", Strong, Muted), SizeType.Absolute, 22);
        Row(brand, Caption($"v{CghVersion.Current}", Small, Muted), SizeType.Absolute, 22);
        Row(navigation, brand, SizeType.Absolute, 93);
        var navigationLabel = UiCaption("Workspace", Small, Muted);
        navigationLabel.Padding = new(8, 0, 0, 0);
        Row(navigation, navigationLabel, SizeType.Absolute, 32);
        AddPage(navigation, BuildMods(), "ModsPage", "ModsDescription");
        AddPage(navigation, BuildExtras(), "ExtrasPage", "ExtrasDescription");
        AddPage(navigation, BuildSettings(), "SettingsPage", "SettingsDescription");
        Row(navigation, new Panel(), SizeType.Percent, 100);
        var footer = Stack();
        footer.Padding = new(8, 0, 0, 0);
        Row(footer, Caption("GARRY’S MOD  /  X64", Small, Accent), SizeType.Absolute, 23);
        Row(footer, UiCaption("HostDescription", Small, Muted), SizeType.Absolute, 24);
        Row(navigation, footer, SizeType.Absolute, 50);

        _shell.Padding = new(22, 16, 22, 16);
        Row(_shell, BuildHeader(), SizeType.Absolute, 106);
        Row(_shell, _workspace, SizeType.Percent, 100);
        Row(_shell, BuildLog(), SizeType.Absolute, 158);
        var layout = Columns(new(SizeType.Absolute, 190), new(SizeType.Percent, 100));
        layout.Controls.Add(navigation, 0, 0);
        layout.Controls.Add(_shell, 1, 0);
        Controls.Add(layout);
        ShowPage(0);
        SetLogExpanded(_logExpanded);
        UpdateLogSummary();
        ResumeLayout();
    }

    private Control BuildHeader()
    {
        var header = Stack();
        var heading = Columns(new(SizeType.Percent, 100), new(SizeType.AutoSize));
        heading.Controls.Add(_title, 0, 0);
        _status.AutoSize = true;
        _status.Dock = DockStyle.None;
        _status.Margin = new(0, 7, 16, 0);
        _launch.Margin = Padding.Empty;
        var actions = Actions(_status, _stop, _launch);
        actions.AutoSize = true;
        actions.Dock = DockStyle.None;
        actions.Anchor = AnchorStyles.Right;
        heading.Controls.Add(actions, 1, 0);
        Row(header, _section, SizeType.Absolute, 20);
        Row(header, heading, SizeType.Absolute, 43);
        Row(header, _subtitle, SizeType.Absolute, 26);
        return header;
    }

    private void AddPage(TableLayoutPanel navigation, Control content, string titleKey, string descriptionKey)
    {
        int index = _pages.Count;
        var link = UiButton(titleKey);
        link.AutoSize = false;
        link.Dock = DockStyle.Fill;
        link.Margin = new(0, 0, 0, 7);
        link.Padding = new(12, 0, 10, 0);
        link.TextAlign = ContentAlignment.MiddleLeft;
        link.Click += (_, _) => ShowPage(index);
        content.Visible = false;
        _workspace.Controls.Add(content);
        _pages.Add((content, link, titleKey, descriptionKey));
        Row(navigation, link, SizeType.Absolute, 49);
    }

    private void ShowPage(int index)
    {
        var page = _pages[index];
        if (_activePageIndex >= 0) _pages[_activePageIndex].Content.Visible = false;
        _activePageIndex = index;
        page.Content.Visible = true;
        page.Content.BringToFront();
        _section.Text = $"CONSGHOST  /  {index + 1:00}";
        UpdatePageTitle();
        foreach (var entry in _pages) HostTheme.Select(entry.Navigation, entry.Navigation == page.Navigation);
    }

    private void UpdatePageTitle()
    {
        var page = _pages[_activePageIndex];
        _title.Text = _ui[page.TitleKey];
        _subtitle.Text = _ui[page.DescriptionKey];
    }

    private Control BuildMods()
    {
        var page = Stack();
        var stats = Columns(new(SizeType.Percent, 33.3f), new(SizeType.Percent, 33.3f), new(SizeType.Percent, 33.4f));
        stats.Controls.Add(MetricCard("ModsFound", _modCount), 0, 0);
        stats.Controls.Add(MetricCard("Enabled", _enabledCount), 1, 0);
        var issues = MetricCard("ScanIssues", _issueCount);
        issues.Margin = new(0, 0, 0, 12);
        stats.Controls.Add(issues, 2, 0);
        Row(page, stats, SizeType.Absolute, 85);

        var refresh = UiButton("Rescan");
        var folder = UiButton("OpenMods");
        var validate = UiButton("CheckLaunch");
        refresh.Click += (_, _) => Guard(RefreshMods);
        folder.Click += (_, _) => OpenFolder(Path.Combine(_root, "Mods"));
        validate.Click += (_, _) => Guard(CheckLaunchConfiguration);
        Row(page, Actions(refresh, folder, validate), SizeType.Absolute, 45);

        AddCheckColumn(_mods, "Enabled", 72);
        AddColumn(_mods, "Name", 50, 118);
        AddColumn(_mods, "Version", 17, 68);
        AddColumn(_mods, "Process", 32, 110);
        _mods.SelectionChanged += (_, _) => { if (!_refreshing) UpdateModDetails(); };
        _mods.CurrentCellDirtyStateChanged += (_, _) => CommitCheckBox(_mods);
        _mods.CellValueChanged += OnModChanged;
        _search.TextChanged += (_, _) => PopulateMods();
        _modFolder.Click += (_, _) => { if (_mods.CurrentRow?.Tag is ModSpec mod) OpenFolder(mod.Directory); };
        _modConfig.Click += (_, _) => Guard(OpenModConfig);

        var list = Card();
        var listHeader = Columns(new(SizeType.Percent, 100), new(SizeType.AutoSize), new(SizeType.Absolute, 151));
        listHeader.Padding = new(12, 0, 12, 0);
        listHeader.Controls.Add(_modListTitle, 0, 0);
        var searchLabel = UiCaption("Search", Small, Muted);
        searchLabel.AutoSize = true;
        searchLabel.Margin = new(0, 0, 8, 0);
        listHeader.Controls.Add(searchLabel, 1, 0);
        _search.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        listHeader.Controls.Add(_search, 2, 0);
        Row(list, listHeader, SizeType.Absolute, 49);
        Row(list, EmptyAwareGrid(_mods, _modEmpty), SizeType.Percent, 100);

        var details = Card(new(13, 6, 13, 10));
        Row(details, UiCaption("ModDetails", Strong, Accent), SizeType.Absolute, 32);
        Row(details, _modDetails, SizeType.Percent, 100);
        var detailActions = Actions(_modConfig, _modFolder);
        detailActions.WrapContents = true;
        detailActions.AutoSize = true;
        detailActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Row(details, detailActions, SizeType.AutoSize, 0);
        var columns = Columns(new(SizeType.Percent, 65), new(SizeType.Percent, 35));
        list.Margin = new(0, 0, 12, 0);
        columns.Controls.Add(list, 0, 0);
        columns.Controls.Add(details, 1, 0);
        Row(page, columns, SizeType.Percent, 100);
        return page;
    }

    private Control BuildExtras()
    {
        var page = Stack();
        var add = UiButton("AddFolder", primary: true);
        add.Click += (_, _) => ChooseExtra(false);
        _editExtra.Click += (_, _) => ChooseExtra(true);
        _deleteExtra.Click += (_, _) => Guard(DeleteExtra);
        Row(page, Actions(add, _editExtra, _deleteExtra), SizeType.Absolute, 45);

        AddCheckColumn(_extras, "Enabled", 72);
        AddColumn(_extras, "DllFolder", 100, 160);
        AddCheckColumn(_extras, "Recursive", 144);
        _extras.SelectionChanged += (_, _) => UpdateExtraActions();
        _extras.CurrentCellDirtyStateChanged += (_, _) => CommitCheckBox(_extras);
        _extras.CellValueChanged += OnExtraChanged;

        var card = Card();
        _extraSummary.Padding = new(12, 0, 12, 0);
        Row(card, _extraSummary, SizeType.Absolute, 39);
        Row(card, EmptyAwareGrid(_extras, _extraEmpty), SizeType.Percent, 100);
        Row(page, card, SizeType.Percent, 100);
        return page;
    }

    private Control BuildSettings()
    {
        var page = Stack();
        var save = UiButton("SaveSettings", primary: true);
        var logs = UiButton("OpenSessionLogs");
        save.Click += (_, _) => Guard(() =>
        {
            SaveSettings();
            _settingsFeedback.Text = $"✓  {_ui["Saved"]}";
            Append("Settings saved.", "info");
        });
        logs.Click += (_, _) => OpenFolder(Path.Combine(_root, "Sessions"));
        _settingsFeedback.AutoSize = true;
        _settingsFeedback.Dock = DockStyle.None;
        _settingsFeedback.Margin = new(6, 7, 0, 0);
        Row(page, Actions(save, logs, _settingsFeedback), SizeType.Absolute, 45);

        var fields = Stack();
        fields.Dock = DockStyle.Top;
        fields.AutoSize = true;
        fields.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var language = Card(new(14, 5, 14, 8));
        Row(language, UiCaption("Language", Strong, Accent), SizeType.Absolute, 27);
        var languageMenu = new ContextMenuStrip();
        AddLanguageOption(_ui["SystemLanguage"], null);
        foreach (var (tag, name) in UiText.Languages) AddLanguageOption(name, tag);
        BindText(UpdateLanguageSelector);
        _language.Click += (_, _) => languageMenu.Show(_language, new Point(0, _language.Height));
        _language.Disposed += (_, _) => languageMenu.Dispose();
        _language.Anchor = AnchorStyles.Left;
        _language.TextAlign = ContentAlignment.MiddleLeft;
        _language.MinimumSize = new(230, 34);
        _language.Margin = Padding.Empty;
        Row(language, _language, SizeType.Absolute, 36);
        Row(fields, language, SizeType.Absolute, 76);

        var paths = Card(new(14, 5, 14, 10));
        paths.Margin = new(0, 10, 0, 0);
        Row(paths, UiCaption("Environment", Strong, Accent), SizeType.Absolute, 32);
        Row(paths, UiCaption("GameFolderHint", Small, Muted), SizeType.Absolute, 24);
        Row(paths, FolderField(_game), SizeType.Absolute, 34);
        Row(paths, UiCaption("DotnetFolder", Small, Muted), SizeType.Absolute, 24);
        Row(paths, FolderField(_dotnet), SizeType.Absolute, 34);
        Row(fields, paths, SizeType.Absolute, 170);

        var launch = Card(new(14, 5, 14, 10));
        launch.Margin = new(0, 10, 0, 0);
        Row(launch, UiCaption("GameStartup", Strong, Accent), SizeType.Absolute, 32);
        Row(launch, UiCaption("GameArgumentsHint", Small, Muted), SizeType.Absolute, 24);
        _arguments.Multiline = true;
        _arguments.AcceptsReturn = true;
        _arguments.ScrollBars = ScrollBars.Vertical;
        _arguments.Dock = DockStyle.Fill;
        Row(launch, _arguments, SizeType.Absolute, 77);
        Row(launch, _hooks, SizeType.Absolute, 34);
        var timeoutLabel = UiCaption("Timeout", Small, Muted);
        timeoutLabel.AutoSize = true;
        timeoutLabel.Dock = DockStyle.None;
        timeoutLabel.Margin = new(0, 4, 10, 0);
        Row(launch, Actions(timeoutLabel, _timeout), SizeType.Absolute, 30);
        Row(launch, UiCaption("InsecureDefault", Small, Muted), SizeType.Absolute, 24);
        Row(fields, launch, SizeType.Absolute, 247);

        _game.TextChanged += (_, _) => _settingsFeedback.Text = "";
        _dotnet.TextChanged += (_, _) => _settingsFeedback.Text = "";
        _arguments.TextChanged += (_, _) => _settingsFeedback.Text = "";
        _hooks.CheckedChanged += (_, _) => _settingsFeedback.Text = "";
        _timeout.ValueChanged += (_, _) => _settingsFeedback.Text = "";
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        scroll.Controls.Add(fields);
        Row(page, scroll, SizeType.Percent, 100);
        return page;

        void AddLanguageOption(string text, string? tag)
        {
            var option = new ToolStripMenuItem(text);
            option.Click += (_, _) => ChangeSelectedLanguage(tag);
            _languageOptions.Add(option);
            languageMenu.Items.Add(option);
        }
    }

    private Control BuildLog()
    {
        _logPanel.BackColor = Input;
        _logPanel.Margin = new(0, 12, 0, 0);
        _logPanel.Padding = new(12, 5, 12, 7);
        var header = Columns(new(SizeType.AutoSize), new(SizeType.Percent, 100), new(SizeType.AutoSize));
        var buttons = Actions(_logCopy, _logClear, _logToggle);
        buttons.AutoSize = true;
        buttons.Dock = DockStyle.None;
        buttons.Anchor = AnchorStyles.Right;
        _logToggle.Margin = Padding.Empty;
        _logToggle.Click += (_, _) => SetLogExpanded(!_logExpanded);
        _logCopy.Click += (_, _) => Guard(() => { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); });
        _logClear.Click += (_, _) => { _log.Clear(); _logEntries = _logErrors = 0; UpdateLogSummary(); };
        var title = UiCaption("HostLog", Strong);
        title.AutoSize = true;
        title.Margin = new(0, 0, 12, 0);
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_logSummary, 1, 0);
        header.Controls.Add(buttons, 2, 0);
        Row(_logPanel, header, SizeType.Absolute, 39);
        Row(_logPanel, _log, SizeType.Percent, 100);
        return _logPanel;
    }

    private void SetLogExpanded(bool expanded)
    {
        _logExpanded = expanded;
        _log.Visible = expanded;
        _logToggle.Text = _ui[expanded ? "Collapse" : "Expand"];
        _shell.RowStyles[2].Height = LogicalToDeviceUnits(expanded ? 158 : 64);
    }

    private void UpdateLogSummary()
    {
        _logSummary.Text = _ui.Format("LogSummary", _logEntries, _logErrors);
        _logSummary.ForeColor = _logErrors > 0 ? Error : Muted;
        _logCopy.Enabled = _logClear.Enabled = _log.TextLength > 0;
    }

    private static TextBox InputBox() => new()
    {
        Font = Body,
        BackColor = Input, ForeColor = HostTheme.Text, BorderStyle = BorderStyle.FixedSingle,
        Margin = Padding.Empty
    };

    private Control FolderField(TextBox input)
    {
        var row = Columns(new(SizeType.Percent, 100), new(SizeType.AutoSize));
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        var browse = UiButton("Browse");
        BindText(() => browse.AccessibleName = _ui.Format("BrowseFor", input.AccessibleName));
        browse.Margin = new(8, 0, 0, 0);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                SelectedPath = input.Text, Description = input.AccessibleName ?? "", UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.SelectedPath;
        };
        row.Controls.Add(input, 0, 0);
        row.Controls.Add(browse, 1, 0);
        return row;
    }

    private static TableLayoutPanel Card(Padding padding = default)
    {
        var card = Stack();
        card.BackColor = Surface;
        card.Padding = padding;
        return card;
    }

    private Control MetricCard(string titleKey, Label value)
    {
        var card = Card(new(14, 5, 10, 3));
        card.Margin = new(0, 0, 12, 12);
        Row(card, UiCaption(titleKey, Small, Muted), SizeType.Absolute, 22);
        Row(card, value, SizeType.Percent, 100);
        return card;
    }

    private static Control EmptyAwareGrid(DataGridView grid, Label empty)
    {
        var area = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.Visible = false;
        area.Controls.Add(grid);
        area.Controls.Add(empty);
        empty.BringToFront();
        return area;
    }

    private void AddColumn(DataGridView grid, string titleKey, float weight, int minimum)
    {
        var column = new DataGridViewTextBoxColumn
        {
            FillWeight = weight, MinimumWidth = minimum, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        BindText(() => column.HeaderText = _ui[titleKey]);
        grid.Columns.Add(column);
    }

    private void AddCheckColumn(DataGridView grid, string titleKey, int width)
    {
        var column = new DataGridViewCheckBoxColumn
        {
            Width = width, AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            DefaultCellStyle = new() { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = Padding.Empty },
            HeaderCell = { Style = new() { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = Padding.Empty } }
        };
        BindText(() => column.HeaderText = _ui[titleKey]);
        grid.Columns.Add(column);
    }

    private void BindText(Action update)
    {
        update();
        _textUpdates.Add(update);
    }

    private T BindControl<T>(T control, string key) where T : Control
    {
        BindText(() => control.Text = _ui[key]);
        return control;
    }

    private Button UiButton(string key, bool primary = false) => BindControl(Action("", primary), key);

    private Label UiCaption(string key, Font? font = null, Color? color = null) => BindControl(Caption("", font, color), key);

    private static void CommitCheckBox(DataGridView grid)
    {
        if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }
}
