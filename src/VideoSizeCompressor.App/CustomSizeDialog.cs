using System.Globalization;

namespace VideoSizeCompressor.App;

internal sealed class CustomSizeDialog : Form
{
    private readonly TextBox size = new() { Dock = DockStyle.Fill, AccessibleName = "Maximum output size in megabytes" };
    private readonly Label error = Ui.Label("");
    public decimal MaximumMegabytes { get; private set; }

    public CustomSizeDialog(decimal initial = 20m)
    {
        Ui.Configure(this, "Custom compression size", new Size(430, 270));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        size.Text = initial.ToString("0.######", CultureInfo.CurrentCulture);
        var panel = Ui.Column();
        Ui.Add(panel, Ui.Label("Maximum output size (MB):", 12, true));
        Ui.Add(panel, size, 40);
        Ui.Add(panel, Ui.Label("1 MB = 1,000,000 bytes. Decimals are welcome.", 9));
        error.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.Firebrick;
        Ui.Add(panel, error, 54);
        var compress = Ui.Button("Compress", true);
        var cancel = Ui.Button("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        compress.Click += (_, _) =>
        {
            const NumberStyles style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
            string text = size.Text.Trim();
            bool valid = decimal.TryParse(text, style, CultureInfo.CurrentCulture, out decimal value)
                || decimal.TryParse(text, style, CultureInfo.InvariantCulture, out value);
            if (!valid || value < 0.000001m || value > 1_000_000m)
            {
                error.Text = "Enter a positive size, such as 7.5 or 20 (up to 1,000,000 MB).";
                size.Focus(); size.SelectAll(); return;
            }
            MaximumMegabytes = value;
            DialogResult = DialogResult.OK; Close();
        };
        Ui.Add(panel, Ui.Buttons(cancel, compress));
        Controls.Add(panel);
        AcceptButton = compress; CancelButton = cancel;
        Shown += (_, _) => { size.Focus(); size.SelectAll(); };
    }
}
