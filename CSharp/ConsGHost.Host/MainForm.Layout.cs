using ConsGHost.Core;
using static ConsGHost.Host.HostTheme;

namespace ConsGHost.Host;

public sealed partial class MainForm
{
    private readonly Button _launch = Action("启动游戏", primary: true);
    private readonly Button _stop = Action("停止会话");
    private readonly Label _status = Caption("●  连接中", Small, Warning);
    private readonly Label _section = Caption("", Small, Accent);
    private readonly Label _title = Caption("", Heading);
    private readonly Label _subtitle = Caption("", Body, Muted);
    private readonly Panel _workspace = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly List<(Control Content, Button Navigation, string Title, string Description)> _pages = [];
    private readonly TableLayoutPanel _shell = Stack();
    private readonly TableLayoutPanel _logPanel = Stack();
    private readonly Button _logToggle = Action("收起");
    private readonly Button _logCopy = Action("复制");
    private readonly Button _logClear = Action("清空");
    private readonly Label _logSummary = Caption("", Small, Muted);
    private readonly RichTextBox _log = new()
    {
        AccessibleName = "宿主日志", Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = true,
        BackColor = Input, ForeColor = HostTheme.Text, Font = Mono, BorderStyle = BorderStyle.None,
        Margin = Padding.Empty
    };

    private readonly DataGridView _mods = Grid("已发现的模组");
    private readonly DataGridView _extras = Grid("额外 DLL 目录");
    private readonly TextBox _search = InputBox("搜索模组", "按名称或 ID 搜索");
    private readonly Label _modCount = Caption("0", Metric);
    private readonly Label _enabledCount = Caption("0", Metric, Good);
    private readonly Label _issueCount = Caption("0", Metric);
    private readonly Label _modListTitle = Caption("模组列表", Strong);
    private readonly Label _modEmpty = Caption("", Body, Muted);
    private readonly Label _extraEmpty = Caption("暂无额外 DLL 目录。点击「添加目录」开始。", Body, Muted);
    private readonly Label _extraSummary = Caption("", Body, Muted);
    private readonly RichTextBox _modDetails = new()
    {
        AccessibleName = "选中模组详情", Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None,
        BackColor = Surface, ForeColor = HostTheme.Text, Font = Body, Margin = Padding.Empty
    };
    private readonly Button _modFolder = Action("打开模组目录");
    private readonly Button _modConfig = Action("配置…");
    private readonly Button _editExtra = Action("修改目录");
    private readonly Button _deleteExtra = Action("移除目录");
    private readonly TextBox _game = InputBox("Garry’s Mod 目录");
    private readonly TextBox _dotnet = InputBox(".NET 10 x64 根目录");
    private readonly TextBox _arguments = InputBox("游戏参数");
    private readonly CheckBox _hooks = new()
    {
        Text = "启用 Source 引擎 GameFrame Hook", AutoSize = true, Dock = DockStyle.Fill,
        ForeColor = HostTheme.Text, Margin = Padding.Empty
    };
    private readonly NumericUpDown _timeout = new()
    {
        AccessibleName = "启动超时（秒）", Minimum = 10, Maximum = 600, Value = 90, Width = 104,
        BackColor = Input, ForeColor = HostTheme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty
    };
    private readonly Label _settingsFeedback = Caption("", Small, Good);
    private Control? _activePage;
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

        var navigation = Stack();
        navigation.BackColor = Sidebar;
        navigation.Padding = new(16, 18, 16, 16);
        var brand = Stack();
        brand.Padding = new(8, 0, 0, 0);
        Row(brand, Caption("CGH", Heading), SizeType.Absolute, 36);
        Row(brand, Caption("ConsGHost", Strong, Muted), SizeType.Absolute, 22);
        Row(brand, Caption($"v{CghVersion.Current}", Small, Muted), SizeType.Absolute, 22);
        Row(navigation, brand, SizeType.Absolute, 93);
        var navigationLabel = Caption("工作区", Small, Muted);
        navigationLabel.Padding = new(8, 0, 0, 0);
        Row(navigation, navigationLabel, SizeType.Absolute, 32);
        AddPage(navigation, BuildMods(), "模组管理", "管理下一次会话要加载的模组，查看运行位置与依赖。");
        AddPage(navigation, BuildExtras(), "额外 DLL", "管理在模组加载前进入 Garry’s Mod 进程的 x64 原生库目录。");
        AddPage(navigation, BuildSettings(), "设置", "配置游戏与 .NET 路径、启动参数和引擎选项。");
        Row(navigation, new Panel(), SizeType.Percent, 100);
        var footer = Stack();
        footer.Padding = new(8, 0, 0, 0);
        Row(footer, Caption("GARRY’S MOD  /  X64", Small, Accent), SizeType.Absolute, 23);
        Row(footer, Caption("C# · C++ 模组宿主", Small, Muted), SizeType.Absolute, 24);
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

    private void AddPage(TableLayoutPanel navigation, Control content, string title, string description)
    {
        int index = _pages.Count;
        var link = Action($"{index + 1:00}    {title}");
        link.AccessibleName = title;
        link.AutoSize = false;
        link.Dock = DockStyle.Fill;
        link.Margin = new(0, 0, 0, 7);
        link.Padding = new(12, 0, 10, 0);
        link.TextAlign = ContentAlignment.MiddleLeft;
        link.Click += (_, _) => ShowPage(index);
        content.Visible = false;
        _workspace.Controls.Add(content);
        _pages.Add((content, link, title, description));
        Row(navigation, link, SizeType.Absolute, 49);
    }

    private void ShowPage(int index)
    {
        var page = _pages[index];
        if (_activePage != null) _activePage.Visible = false;
        _activePage = page.Content;
        _activePage.Visible = true;
        _activePage.BringToFront();
        _section.Text = $"CONSGHOST  /  {index + 1:00}";
        _title.Text = page.Title;
        _subtitle.Text = page.Description;
        foreach (var entry in _pages) HostTheme.Select(entry.Navigation, entry.Navigation == page.Navigation);
    }

    private Control BuildMods()
    {
        var page = Stack();
        var stats = Columns(new(SizeType.Percent, 33.3f), new(SizeType.Percent, 33.3f), new(SizeType.Percent, 33.4f));
        stats.Controls.Add(MetricCard("已发现模组", _modCount), 0, 0);
        stats.Controls.Add(MetricCard("已启用", _enabledCount), 1, 0);
        var issues = MetricCard("扫描问题", _issueCount);
        issues.Margin = new(0, 0, 0, 12);
        stats.Controls.Add(issues, 2, 0);
        Row(page, stats, SizeType.Absolute, 85);

        var refresh = Action("重新扫描");
        var folder = Action("打开 Mods");
        var validate = Action("检查启动配置");
        refresh.Click += (_, _) => Guard(RefreshMods);
        folder.Click += (_, _) => OpenFolder(Path.Combine(_root, "Mods"));
        validate.Click += (_, _) => Guard(CheckLaunchConfiguration);
        Row(page, Actions(refresh, folder, validate), SizeType.Absolute, 45);

        AddCheckColumn(_mods, "启用", 70);
        AddColumn(_mods, "名称", 50, 126);
        AddColumn(_mods, "版本", 17, 62);
        AddColumn(_mods, "运行位置", 28, 80);
        _mods.SelectionChanged += (_, _) => { if (!_refreshing) UpdateModDetails(); };
        _mods.CurrentCellDirtyStateChanged += (_, _) => CommitCheckBox(_mods);
        _mods.CellValueChanged += OnModChanged;
        _search.TextChanged += (_, _) => PopulateMods();
        _modFolder.Click += (_, _) => { if (_mods.CurrentRow?.Tag is ModSpec mod) OpenFolder(mod.Directory); };
        _modConfig.Click += (_, _) => Guard(OpenModConfig);

        var list = Card();
        var listHeader = Columns(new(SizeType.Percent, 100), new(SizeType.Absolute, 42), new(SizeType.Absolute, 176));
        listHeader.Padding = new(12, 0, 12, 0);
        listHeader.Controls.Add(_modListTitle, 0, 0);
        listHeader.Controls.Add(Caption("搜索", Small, Muted), 1, 0);
        _search.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        listHeader.Controls.Add(_search, 2, 0);
        Row(list, listHeader, SizeType.Absolute, 49);
        Row(list, EmptyAwareGrid(_mods, _modEmpty), SizeType.Percent, 100);

        var details = Card(new(13, 6, 13, 10));
        Row(details, Caption("模组详情", Strong, Accent), SizeType.Absolute, 32);
        Row(details, _modDetails, SizeType.Percent, 100);
        Row(details, Actions(_modConfig, _modFolder), SizeType.Absolute, 34);
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
        var add = Action("添加目录", primary: true);
        add.Click += (_, _) => ChooseExtra(false);
        _editExtra.Click += (_, _) => ChooseExtra(true);
        _deleteExtra.Click += (_, _) => Guard(DeleteExtra);
        Row(page, Actions(add, _editExtra, _deleteExtra), SizeType.Absolute, 45);

        AddCheckColumn(_extras, "启用", 66);
        AddColumn(_extras, "原生 DLL 目录", 100, 160);
        AddCheckColumn(_extras, "递归子目录", 112);
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
        var save = Action("保存设置", primary: true);
        var logs = Action("打开会话日志");
        save.Click += (_, _) => Guard(() => { SaveSettings(); _settingsFeedback.Text = "✓  已保存"; Append("设置已保存。", "info"); });
        logs.Click += (_, _) => OpenFolder(Path.Combine(_root, "Sessions"));
        _settingsFeedback.AutoSize = true;
        _settingsFeedback.Dock = DockStyle.None;
        _settingsFeedback.Margin = new(6, 7, 0, 0);
        Row(page, Actions(save, logs, _settingsFeedback), SizeType.Absolute, 45);

        var fields = Stack();
        fields.Dock = DockStyle.Top;
        fields.AutoSize = true;
        fields.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var paths = Card(new(14, 5, 14, 10));
        Row(paths, Caption("运行环境", Strong, Accent), SizeType.Absolute, 32);
        Row(paths, Caption("Garry’s Mod 目录 · 包含 gmod_win64.exe", Small, Muted), SizeType.Absolute, 24);
        Row(paths, FolderField(_game), SizeType.Absolute, 34);
        Row(paths, Caption(".NET 10 x64 根目录", Small, Muted), SizeType.Absolute, 24);
        Row(paths, FolderField(_dotnet), SizeType.Absolute, 34);
        Row(fields, paths, SizeType.Absolute, 170);

        var launch = Card(new(14, 5, 14, 10));
        launch.Margin = new(0, 10, 0, 0);
        Row(launch, Caption("游戏启动", Strong, Accent), SizeType.Absolute, 32);
        Row(launch, Caption("游戏参数 · 每行一个参数，由宿主处理引号", Small, Muted), SizeType.Absolute, 24);
        _arguments.Multiline = true;
        _arguments.AcceptsReturn = true;
        _arguments.ScrollBars = ScrollBars.Vertical;
        _arguments.Dock = DockStyle.Fill;
        Row(launch, _arguments, SizeType.Absolute, 77);
        Row(launch, _hooks, SizeType.Absolute, 34);
        var timeoutLabel = Caption("启动超时（秒）", Small, Muted);
        timeoutLabel.AutoSize = true;
        timeoutLabel.Dock = DockStyle.None;
        timeoutLabel.Margin = new(0, 4, 10, 0);
        Row(launch, Actions(timeoutLabel, _timeout), SizeType.Absolute, 30);
        Row(launch, Caption("默认 -insecure 用于本地原生模组开发。", Small, Muted), SizeType.Absolute, 24);
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
    }

    private Control BuildLog()
    {
        _logPanel.BackColor = Input;
        _logPanel.Margin = new(0, 12, 0, 0);
        _logPanel.Padding = new(12, 5, 12, 7);
        var header = Columns(new(SizeType.Absolute, 82), new(SizeType.Percent, 100), new(SizeType.AutoSize));
        var buttons = Actions(_logCopy, _logClear, _logToggle);
        buttons.AutoSize = true;
        buttons.Dock = DockStyle.None;
        buttons.Anchor = AnchorStyles.Right;
        _logToggle.Margin = Padding.Empty;
        _logToggle.Click += (_, _) => SetLogExpanded(!_logExpanded);
        _logCopy.Click += (_, _) => Guard(() => { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); });
        _logClear.Click += (_, _) => { _log.Clear(); _logEntries = _logErrors = 0; UpdateLogSummary(); };
        header.Controls.Add(Caption("宿主日志", Strong), 0, 0);
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
        _logToggle.Text = expanded ? "收起" : "展开";
        _shell.RowStyles[2].Height = LogicalToDeviceUnits(expanded ? 158 : 64);
    }

    private void UpdateLogSummary()
    {
        _logSummary.Text = $"{_logEntries} 条事件  ·  {_logErrors} 条错误";
        _logSummary.ForeColor = _logErrors > 0 ? Error : Muted;
        _logCopy.Enabled = _logClear.Enabled = _log.TextLength > 0;
    }

    private static TextBox InputBox(string name, string placeholder = "") => new()
    {
        AccessibleName = name, PlaceholderText = placeholder, Font = Body,
        BackColor = Input, ForeColor = HostTheme.Text, BorderStyle = BorderStyle.FixedSingle,
        Margin = Padding.Empty
    };

    private Control FolderField(TextBox input)
    {
        var row = Columns(new(SizeType.Percent, 100), new(SizeType.AutoSize));
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        var browse = Action("浏览…");
        browse.AccessibleName = "浏览 " + input.AccessibleName;
        browse.Margin = new(8, 0, 0, 0);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = input.Text };
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

    private static Control MetricCard(string title, Label value)
    {
        var card = Card(new(14, 5, 10, 3));
        card.Margin = new(0, 0, 12, 12);
        Row(card, Caption(title, Small, Muted), SizeType.Absolute, 22);
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

    private static void AddColumn(DataGridView grid, string title, float weight, int minimum) =>
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title, FillWeight = weight, MinimumWidth = minimum, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private static void AddCheckColumn(DataGridView grid, string title, int width) =>
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = title, Width = width, AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            DefaultCellStyle = new() { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = Padding.Empty },
            HeaderCell = { Style = new() { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = Padding.Empty } }
        });

    private static void CommitCheckBox(DataGridView grid)
    {
        if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }
}
