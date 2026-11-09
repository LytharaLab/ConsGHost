using System.Text.Json;
using System.Text.Json.Nodes;
using ConsGHost.Core;
using static ConsGHost.Host.HostTheme;

namespace ConsGHost.Host;

internal sealed class ModConfigDialog : Form
{
    private readonly ModConfig _config;
    private readonly string _original;
    private readonly Button _simpleButton = Action("简单配置");
    private readonly Button _advancedButton = Action("高级配置");
    private readonly Panel _simple = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly RichTextBox _json = new()
    {
        AccessibleName = "JSON 配置", Dock = DockStyle.Fill, BackColor = Input, ForeColor = HostTheme.Text,
        Font = Mono, WordWrap = false, AcceptsTab = true, DetectUrls = false, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label _feedback = Caption("", Small, Error);
    private readonly List<Action> _apply = [];
    private JsonObject _draft = new();
    private bool _advanced = true;

    public ModConfigDialog(string name, ModConfig config)
    {
        _config = config;
        _original = config.ReadRaw();
        _json.Text = _original;
        Text = $"配置 · {name}";
        Font = Body;
        BackColor = Canvas;
        ForeColor = HostTheme.Text;
        ClientSize = new(720, 530);
        MinimumSize = new(520, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        var body = Stack();
        body.Padding = new(16);
        Row(body, Caption(name, Heading), SizeType.Absolute, 43);
        Row(body, Caption("保存后写入此模组的 config.json。", Small, Muted), SizeType.Absolute, 28);
        Row(body, Actions(_simpleButton, _advancedButton), SizeType.Absolute, 43);
        var workspace = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        workspace.Controls.AddRange([_simple, _json]);
        Row(body, workspace, SizeType.Percent, 100);
        Row(body, _feedback, SizeType.Absolute, 43);
        var save = Action("保存", primary: true);
        var cancel = Action("取消");
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        Row(body, Actions(save, cancel), SizeType.Absolute, 38);
        Controls.Add(body);

        _simpleButton.Click += (_, _) => ChangeMode(advanced: false);
        _advancedButton.Click += (_, _) => ChangeMode(advanced: true);
        save.Click += (_, _) => Save();
        ShowMode();
        ChangeMode(advanced: false);
    }

    private void ChangeMode(bool advanced)
    {
        if (_advanced == advanced) return;
        Run(() =>
        {
            if (advanced) _json.Text = CollectSimple();
            else BuildSimple(_json.Text);
            _advanced = advanced;
            ShowMode();
        });
    }

    private void ShowMode()
    {
        _simple.Visible = !_advanced;
        _json.Visible = _advanced;
        HostTheme.Select(_simpleButton, !_advanced);
        HostTheme.Select(_advancedButton, _advanced);
    }

    private void BuildSimple(string raw)
    {
        _draft = _config.ReadEffective(raw);
        var defaults = _config.ReadDefaults();
        _apply.Clear();
        while (_simple.Controls.Count > 0) _simple.Controls[0].Dispose();
        var fields = Stack();
        fields.Dock = DockStyle.Top;
        fields.AutoSize = true;
        fields.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        fields.Padding = new(12);
        _simple.Controls.Add(fields);
        if (defaults.Count == 0)
            Row(fields, Caption("模板没有定义字段，可在高级配置中编辑 JSON。", color: Muted), SizeType.Absolute, 36);
        AddFields(fields, defaults, _draft, "");
    }

    private void AddFields(TableLayoutPanel fields, JsonObject defaults, JsonObject values, string prefix)
    {
        foreach (var (key, fallback) in defaults)
        {
            string label = prefix + key;
            var value = values[key];
            if (fallback is JsonObject { Count: > 0 } group && value is JsonObject nested)
            {
                AddFields(fields, group, nested, label + ".");
                continue;
            }

            if (fallback?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                var check = new CheckBox { Text = label, AccessibleName = label, Checked = value!.GetValue<bool>(), Dock = DockStyle.Fill };
                Row(fields, check, SizeType.Absolute, 32);
                _apply.Add(() => values[key] = check.Checked);
                continue;
            }

            bool isString = fallback?.GetValueKind() == JsonValueKind.String;
            var input = new TextBox
            {
                AccessibleName = label, Dock = DockStyle.Fill, BackColor = Input, ForeColor = HostTheme.Text,
                Text = isString ? value!.GetValue<string>() : value?.ToJsonString() ?? "null",
                Multiline = true, WordWrap = false, BorderStyle = BorderStyle.FixedSingle, Margin = new(0, 2, 0, 6)
            };
            string original = input.Text;
            Row(fields, Caption(label, Small, Muted), SizeType.Absolute, 23);
            Row(fields, input, SizeType.Absolute, original.Contains('\n') ? 68 : 32);
            _apply.Add(() =>
            {
                if (input.Text == original)
                {
                    values[key] = value?.DeepClone();
                    return;
                }
                try { values[key] = isString ? JsonValue.Create(input.Text) : JsonNode.Parse(input.Text); }
                catch (JsonException error) { throw new InvalidDataException($"{label}: {error.Message}", error); }
            });
        }
    }

    private string CollectSimple()
    {
        foreach (var apply in _apply) apply();
        var raw = _draft.ToJsonString(Json.Options);
        _ = _config.ReadEffective(raw);
        return raw;
    }

    private void Save() => Run(() =>
    {
        _config.SaveRaw(_advanced ? _json.Text : CollectSimple(), _original);
        DialogResult = DialogResult.OK;
    });

    private void Run(Action action)
    {
        try { _feedback.Text = ""; action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _feedback.Text = error.Message;
        }
    }

    internal async Task CheckAndRenderAsync(string output)
    {
        await RenderAsync("config-simple.png");
        var form = (TableLayoutPanel)_simple.Controls[0];
        form.Controls.OfType<CheckBox>().Single().Checked = false;
        _advancedButton.PerformClick();
        if (!_advanced || _json.Text.Contains("\"enabled\": true") || !_json.Text.Contains("\"extra\""))
            throw new InvalidOperationException("Switching to JSON lost the simple edit or an unknown key.");
        await RenderAsync("config-advanced.png");
        _json.Text = """{"enabled":false,"nested":{"count":3},"extra":"advanced"}""";
        _simpleButton.PerformClick();
        if (_advanced || ((TableLayoutPanel)_simple.Controls[0]).Controls.OfType<TextBox>().Single().Text != "3")
            throw new InvalidOperationException("Switching to the form lost the advanced edit.");
        Save();
        var saved = _config.Read();
        if (DialogResult != DialogResult.OK || saved.GetProperty("enabled").GetBoolean() ||
            saved.GetProperty("nested").GetProperty("count").GetInt32() != 3 || saved.GetProperty("extra").GetString() != "advanced")
            throw new InvalidOperationException("The configuration form did not persist both edits.");

        async Task RenderAsync(string name)
        {
            await Task.Delay(100);
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, name));
        }
    }
}
