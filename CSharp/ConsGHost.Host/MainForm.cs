using System.Diagnostics;
using ConsGHost.Core;

namespace ConsGHost.Host;

public sealed class MainForm : Form
{
    private readonly string _root = AppContext.BaseDirectory;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _mods = Grid();
    private readonly DataGridView _extras = Grid();
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(18, 23, 31), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 10), BorderStyle = BorderStyle.None };
    private readonly Label _status = new() { Text = "准备中", AutoSize = true, ForeColor = Color.FromArgb(89, 205, 184), Padding = new Padding(12, 15, 0, 0) };
    private readonly Button _launch = Button("▶  启动游戏");
    private readonly Button _stop = Button("停止会话");
    private readonly TextBox _game = new() { Width = 620 };
    private readonly TextBox _dotnet = new() { Width = 620 };
    private readonly TextBox _arguments = new() { Width = 620, Height = 100, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _hooks = new() { Text = "启用 Source 引擎 GameFrame Hook", AutoSize = true };
    private readonly NumericUpDown _timeout = new() { Minimum = 10, Maximum = 600, Value = 90, Width = 100 };
    private HostSettings _settings = new();
    private RuntimeClient? _runtime;
    private ScanResult _catalog = new([], []);
    private bool _refreshing, _busy, _closing, _closed;
    private string SettingsPath => Path.Combine(_root, "cgh_settings.json");
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MainForm()
    {
        Text = "ConsGHost · Con's GMod Host";
        MinimumSize = new(1000, 740); Size = new(1200, 850); StartPosition = FormStartPosition.CenterScreen;
        Font = new("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(29, 35, 45); ForeColor = Color.Gainsboro;
        var header = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new(22, 12, 22, 8) };
        header.Controls.Add(new Label { Text = "CGH  /  ConsGHost", Font = new(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Location = new(22, 12) });
        header.Controls.Add(new Label { Text = "Garry's Mod 64-bit  ·  Managed + Native  ·  .NET 10", AutoSize = true, Location = new(25, 58), ForeColor = Color.Silver });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 475, Padding = new(0, 10, 0, 0) };
        actions.Controls.AddRange([_launch, _stop, _status]); header.Controls.Add(actions);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, Panel1MinSize = 260, Panel2MinSize = 120 };
        split.Panel1.Controls.Add(_tabs); split.Panel2.Controls.Add(_log);
        Controls.Add(split); Controls.Add(header);
        BuildMods(); BuildExtras(); BuildSettings();
        _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabs.ItemSize = new(120, 36);
        _tabs.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush(e.Index == _tabs.SelectedIndex ? Color.FromArgb(47, 79, 90) : BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, Font, e.Bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        _launch.Click += async (_, _) => await LaunchAsync();
        _stop.Click += async (_, _) => await GuardAsync(async () => { if (_runtime != null) await _runtime.SendAsync("stop"); });
        Shown += async (_, _) => { split.SplitterDistance = Math.Max(300, split.Height - 235); await InitializeAsync(); };
        FormClosing += OnClosing;
    }
    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        BackgroundColor = Color.FromArgb(34, 41, 53), BorderStyle = BorderStyle.None, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        DefaultCellStyle = new() { BackColor = Color.FromArgb(34, 41, 53), ForeColor = Color.Gainsboro, SelectionBackColor = Color.FromArgb(51, 76, 95), SelectionForeColor = Color.White, Padding = new(5) },
        ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(43, 52, 66), ForeColor = Color.White, Padding = new(4), WrapMode = DataGridViewTriState.True }, EnableHeadersVisualStyles = false
    };
    private static Button Button(string text) => new() { Text = text, AutoSize = true, Height = 38, FlatStyle = FlatStyle.Flat, Padding = new(9, 5, 9, 5), Margin = new(4), BackColor = Color.FromArgb(47, 66, 82), ForeColor = Color.White };
    private TabPage Page(string name, string help, Control content, params Control[] buttons)
    {
        var page = new TabPage(name) { BackColor = BackColor, ForeColor = ForeColor, Padding = new(12) };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50 }; toolbar.Controls.AddRange(buttons);
        var label = new Label { Text = help, Dock = DockStyle.Bottom, Height = 45, Padding = new(0, 10, 0, 0), ForeColor = Color.Silver };
        page.Controls.Add(content); page.Controls.Add(toolbar); page.Controls.Add(label); _tabs.TabPages.Add(page); return page;
    }
    private void BuildMods()
    {
        _mods.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", FillWeight = 35 });
        foreach (var (name, title, width) in new[] { ("Name", "模组", 90), ("Id", "ID", 90), ("Version", "版本", 45), ("Mode", "运行位置", 65), ("Modules", "Managed / Native", 70), ("Description", "说明", 160) })
            _mods.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, FillWeight = width, ReadOnly = true });
        var refresh = Button("重新扫描"); refresh.Click += (_, _) => Guard(RefreshMods);
        var open = Button("打开 Mods"); open.Click += (_, _) => OpenFolder(Path.Combine(_root, "Mods"));
        var folder = Button("打开选中模组"); folder.Click += (_, _) => { if (_mods.CurrentRow?.Tag is ModSpec m) OpenFolder(m.Directory); };
        var validate = Button("检查启动配置"); validate.Click += (_, _) => Guard(() => { SaveSettings(); var selected = SessionPreparation.Prepare(_root, _settings); Append("依赖检查通过：" + string.Join(" → ", selected.Select(m => m.Metadata.Id)), "info"); });
        Page("模组管理", "扫描宿主同目录 Mods；启用状态、隔离方式与依赖关系在下一次启动时生效。", _mods, refresh, open, folder, validate);
        _mods.CurrentCellDirtyStateChanged += (_, _) => { if (_mods.IsCurrentCellDirty) _mods.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _mods.CellValueChanged += (_, e) =>
        {
            if (_refreshing || e.RowIndex < 0 || e.ColumnIndex != 0 || _mods.Rows[e.RowIndex].Tag is not ModSpec spec) return;
            Guard(() =>
            {
                bool enabled = _mods.Rows[e.RowIndex].Cells[0].Value is true;
                _settings.DisabledMods.RemoveAll(id => id == spec.Metadata.Id);
                if (!enabled) _settings.DisabledMods.Add(spec.Metadata.Id);
                Json.Write(SettingsPath, _settings);
            });
        };
    }
    private void BuildExtras()
    {
        _extras.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "启用", FillWeight = 30 });
        _extras.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DLL 目录", FillWeight = 220, ReadOnly = true });
        _extras.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "递归子目录", FillWeight = 50 });
        var add = Button("增加目录"); add.Click += (_, _) => ChooseExtra(false);
        var edit = Button("修改目录"); edit.Click += (_, _) => ChooseExtra(true);
        var delete = Button("删除选中"); delete.Click += (_, _) => Guard(() => { if (_extras.CurrentRow?.Tag is ExtraLibraryDirectory e) _settings.ExtraLibraries.Remove(e); Json.Write(SettingsPath, _settings); RefreshExtras(); });
        Page("额外 DLL", "启用目录中的 x64 原生 DLL 会在模组加载前进入 GMod 进程；这不是 .NET 程序集目录。", _extras, add, edit, delete);
        _extras.CurrentCellDirtyStateChanged += (_, _) => { if (_extras.IsCurrentCellDirty) _extras.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _extras.CellValueChanged += (_, e) =>
        {
            if (_refreshing || e.RowIndex < 0 || _extras.Rows[e.RowIndex].Tag is not ExtraLibraryDirectory entry) return;
            Guard(() => { entry.Enabled = _extras.Rows[e.RowIndex].Cells[0].Value is true; entry.Recursive = _extras.Rows[e.RowIndex].Cells[2].Value is true; Json.Write(SettingsPath, _settings); });
        };
    }
    private void ChooseExtra(bool edit)
    {
        using var dialog = new FolderBrowserDialog { Description = "选择包含 x64 原生依赖 DLL 的目录", UseDescriptionForTitle = true };
        var selected = edit ? _extras.CurrentRow?.Tag as ExtraLibraryDirectory : null;
        if (edit && selected == null) return;
        if (selected != null) dialog.SelectedPath = selected.Path;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Guard(() =>
        {
            if (selected != null) selected.Path = dialog.SelectedPath;
            else _settings.ExtraLibraries.Add(new() { Path = dialog.SelectedPath });
            Json.Write(SettingsPath, _settings); RefreshExtras();
        });
    }
    private void BuildSettings()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(15) };
        void Row(string label, Control control) { flow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new(3, 12, 3, 5) }); flow.Controls.Add(control); }
        Row("Garry's Mod 目录（包含 gmod_win64.exe）", _game);
        var browse = Button("浏览游戏目录"); browse.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = _game.Text }; if (d.ShowDialog(this) == DialogResult.OK) _game.Text = d.SelectedPath; }; flow.Controls.Add(browse);
        Row(".NET 10 x64 根目录", _dotnet);
        Row("游戏参数（每行一个参数；引号无需手写）", _arguments);
        flow.Controls.Add(_hooks); Row("启动超时（秒）", _timeout);
        var save = Button("保存设置"); save.Click += (_, _) => Guard(() => { SaveSettings(); Append("设置已保存。", "info"); });
        var logs = Button("打开会话日志"); logs.Click += (_, _) => OpenFolder(Path.Combine(_root, "Sessions"));
        Page("设置", "默认 -insecure 适用于本地原生模组开发。引擎接口不兼容时会报告错误；LAPI 尚未包含在此版本。", flow, save, logs);
    }
    private async Task InitializeAsync()
    {
        try
        {
            _settings = File.Exists(SettingsPath) ? Json.Read<HostSettings>(SettingsPath) : new();
            if (!File.Exists(SettingsPath)) Json.Write(SettingsPath, _settings);
            _game.Text = _settings.GameDirectory; _dotnet.Text = _settings.DotnetRoot;
            _arguments.Lines = _settings.GameArguments.ToArray(); _hooks.Checked = _settings.EnableEngineHooks;
            _timeout.Value = Math.Clamp(_settings.StartupTimeoutSeconds, 10, 600);
            RefreshMods(); RefreshExtras();
            _runtime = new(_root); _runtime.Message += OnMessage; await _runtime.ConnectAsync();
            Append("CGH Runtime 已连接；选择模组后即可启动。", "info");
            _initialized.TrySetResult();
        }
        catch (Exception error) { Append(error.ToString(), "error"); SetState("disconnected"); _initialized.TrySetException(error); }
    }
    private void RefreshMods()
    {
        _catalog = ModCatalog.Scan(Path.Combine(_root, "Mods"));
        _refreshing = true;
        try
        {
            _mods.Rows.Clear();
            foreach (var mod in _catalog.Mods)
            {
                var m = mod.Metadata;
                int index = _mods.Rows.Add(m.Enabled && !_settings.DisabledMods.Contains(m.Id), m.Name, m.Id, m.Version,
                    m.LoadAsSubprocess ? "独立子进程" : "GMod 进程", $"{mod.Managed.Count} / {mod.Native.Count}", m.Description);
                _mods.Rows[index].Tag = mod;
                if (!m.Enabled) { _mods.Rows[index].Cells[0].ReadOnly = true; _mods.Rows[index].Cells[5].ToolTipText = "元数据 enabled=false；请先修改元数据。"; }
            }
        }
        finally { _refreshing = false; }
        foreach (var issue in _catalog.Issues) Append(issue.Directory + ": " + issue.Message, "error");
        Append($"扫描完成：{_catalog.Mods.Count} 个模组，{_catalog.Issues.Count} 个错误。", "info");
    }
    private void RefreshExtras()
    {
        _refreshing = true;
        try { _extras.Rows.Clear(); foreach (var e in _settings.ExtraLibraries) { int index = _extras.Rows.Add(e.Enabled, e.Path, e.Recursive); _extras.Rows[index].Tag = e; } }
        finally { _refreshing = false; }
    }
    private void SaveSettings()
    {
        _settings.GameDirectory = _game.Text.Trim(); _settings.DotnetRoot = _dotnet.Text.Trim();
        _settings.GameArguments = _arguments.Lines.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        _settings.EnableEngineHooks = _hooks.Checked; _settings.StartupTimeoutSeconds = (int)_timeout.Value;
        Json.Write(SettingsPath, _settings);
    }
    private async Task LaunchAsync() => await GuardAsync(async () =>
    {
        if (_busy || _runtime == null) return;
        SaveSettings(); RefreshMods(); _ = SessionPreparation.Prepare(_root, _settings);
        SetState("starting"); await _runtime.SendAsync("launch");
    });
    private void OnMessage(WireMessage message)
    {
        if (IsDisposed || !IsHandleCreated || _closing) return;
        BeginInvoke(() =>
        {
            if (message.Kind == "state") SetState(message.Text ?? "unknown", message.ProcessId);
            else Append($"{(message.Mod ?? message.Endpoint ?? "runtime")}: {message.Text}", message.Kind == "fault" ? "error" : message.Level ?? "info");
        });
    }
    private void SetState(string state, int pid = 0)
    {
        _busy = state is "starting" or "running" or "stopping";
        _launch.Enabled = !_busy && state != "disconnected"; _stop.Enabled = state is "starting" or "running";
        _tabs.Enabled = !_busy;
        _status.Text = state switch { "idle" => "就绪", "starting" => "启动中…", "running" => $"运行中 · {pid}", "stopping" => "停止中…", _ => "未连接" };
    }
    private void Append(string text, string level)
    {
        if (_log.TextLength > 250000) { _log.Select(0, 100000); _log.SelectedText = ""; }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = level == "error" ? Color.Salmon : level == "warning" ? Color.Khaki : Color.Gainsboro;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}"); _log.ScrollToCaret();
    }
    private void OpenFolder(string path) => Guard(() => { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); });
    private void Guard(Action action) { try { action(); } catch (Exception e) { Append(e.Message, "error"); } }
    private async Task GuardAsync(Func<Task> action) { try { await action(); } catch (Exception e) { Append(e.Message, "error"); SetState("idle"); } }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true; if (_closing) return; _closing = true; Enabled = false;
        try { SaveSettings(); if (_runtime != null) await _runtime.DisposeAsync(); }
        finally { _closed = true; Close(); }
    }
    /// <summary>In-app diagnostic rendering and persistence check; does not launch the game.</summary>
    public async Task CheckAndRenderAsync(string output)
    {
        await _initialized.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(200);
        if (_mods.Rows.Count != _catalog.Mods.Count || _tabs.TabPages.Count != 3 || !_launch.Enabled)
            throw new InvalidOperationException("Host initial UI state is inconsistent.");
        Directory.CreateDirectory(output);
        if (_mods.Rows.Count > 0 && _mods.Rows[0].Tag is ModSpec mod && mod.Metadata.Enabled)
        {
            bool initial = _mods.Rows[0].Cells[0].Value is true;
            _mods.Rows[0].Cells[0].Value = !initial;
            if (Json.Read<HostSettings>(SettingsPath).DisabledMods.Contains(mod.Metadata.Id) != initial)
                throw new InvalidOperationException("Mod enable/disable persistence failed.");
            _mods.Rows[0].Cells[0].Value = initial;
        }
        SaveSettings();
        for (int i = 0; i < _tabs.TabPages.Count; ++i)
        {
            _tabs.SelectedIndex = i; await Task.Delay(100);
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, $"page-{i}.png"));
        }
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: runtime connected, 3 pages rendered, mod toggle persisted and restored, settings saved.");
    }
}
