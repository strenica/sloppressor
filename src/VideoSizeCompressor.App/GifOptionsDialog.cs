using System.Globalization;
using VideoSizeCompressor.Core;

namespace VideoSizeCompressor.App;

internal sealed class GifOptionsDialog : Form
{
    private readonly TextBox width = new() { Dock = DockStyle.Fill, AccessibleName = "GIF width in pixels" };
    private readonly TextBox fps = new() { Dock = DockStyle.Fill, AccessibleName = "GIF frames per second" };
    private readonly TextBox start = new() { Dock = DockStyle.Fill, AccessibleName = "Clip start in seconds" };
    private readonly TextBox duration = new() { Dock = DockStyle.Fill, AccessibleName = "Clip length in seconds" };
    private readonly CheckBox whole = new() { Text = "Use the whole video from the start time", AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
    private readonly Label error = Ui.Label("");
    private readonly GifOptions initial;
    public GifOptions Options { get; private set; }

    public GifOptionsDialog(GifOptions initial)
    {
        this.initial = initial;
        Options = initial;
        Ui.Configure(this, "Convert to GIF", new Size(500, 450));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

        var panel = Ui.Column();
        Ui.Add(panel, Ui.Label("Make a looping GIF", 18, true));
        var file = Ui.Label(Path.GetFileName(initial.InputPath), 10);
        file.AutoEllipsis = true; file.AutoSize = false;
        Ui.Add(panel, file, 30);
        Ui.Add(panel, Ui.Label("The original video is never changed. A shorter clip or lower width keeps the GIF smaller."), 9);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 0, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        width.Text = initial.Width.ToString(CultureInfo.CurrentCulture);
        fps.Text = initial.FramesPerSecond.ToString(CultureInfo.CurrentCulture);
        start.Text = initial.StartSeconds.ToString("0.###", CultureInfo.CurrentCulture);
        duration.Text = initial.DurationSeconds?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";
        AddRow(fields, "Width (pixels, 64–1920)", width);
        AddRow(fields, "Frames per second (1–30)", fps);
        AddRow(fields, "Start (seconds)", start);
        AddRow(fields, "Clip length (seconds)", duration);
        Ui.Add(panel, fields);
        whole.Checked = initial.DurationSeconds is null;
        whole.CheckedChanged += (_, _) => duration.Enabled = !whole.Checked;
        duration.Enabled = !whole.Checked;
        Ui.Add(panel, whole);

        error.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.Firebrick;
        Ui.Add(panel, error, 46);
        var convert = Ui.Button("Convert", true);
        var cancel = Ui.Button("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        convert.Click += (_, _) => Submit(convert);
        Ui.Add(panel, Ui.Buttons(cancel, convert));
        Controls.Add(panel);
        AcceptButton = convert; CancelButton = cancel;
        Shown += (_, _) => { width.Focus(); width.SelectAll(); };
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var text = Ui.Label(label);
        text.Anchor = AnchorStyles.Left;
        table.Controls.Add(text, 0, row);
        control.Margin = new Padding(0, 4, 0, 8);
        table.Controls.Add(control, 1, row);
    }

    private void Submit(Button convert)
    {
        if (!int.TryParse(width.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int widthValue)
            && !int.TryParse(width.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out widthValue))
        {
            Fail("Enter a whole GIF width, such as 480.", width); return;
        }
        if (!int.TryParse(fps.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int fpsValue)
            && !int.TryParse(fps.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out fpsValue))
        {
            Fail("Enter a whole frame rate, such as 12.", fps); return;
        }
        if (!TryDecimal(start.Text, out decimal startValue))
        {
            Fail("Enter a start time in seconds, such as 0 or 2.5.", start); return;
        }
        decimal? durationValue = null;
        if (!whole.Checked)
        {
            if (!TryDecimal(duration.Text, out decimal parsed))
            {
                Fail("Enter a clip length in seconds, such as 10.", duration); return;
            }
            durationValue = parsed;
        }
        GifOptions options = initial with
        {
            Width = widthValue,
            FramesPerSecond = fpsValue,
            StartSeconds = startValue,
            DurationSeconds = durationValue
        };
        try
        {
            options.Validate();
            Options = options;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (CompressionException ex)
        {
            Fail(ex.Message, convert);
        }
    }

    private static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private void Fail(string message, Control focus)
    {
        error.Text = message;
        focus.Focus();
        if (focus is TextBox box) box.SelectAll();
    }
}
