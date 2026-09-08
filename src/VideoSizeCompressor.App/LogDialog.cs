namespace VideoSizeCompressor.App;

internal sealed class LogDialog : Form
{
    public LogDialog(string log)
    {
        Ui.Configure(this, "Compression log", new Size(780, 500));
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
            ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), Text = log, AccessibleName = "Detailed compression log" };
        var panel = Ui.Column();
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowCount = 1; panel.Controls.Add(text, 0, 0);
        var copy = Ui.Button("Copy Log", true);
        var close = Ui.Button("Close");
        copy.Click += (_, _) =>
        {
            try { if (text.Text.Length > 0) Clipboard.SetText(text.Text); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Clipboard unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        close.Click += (_, _) => Close();
        Ui.Add(panel, Ui.Buttons(close, copy));
        Controls.Add(panel);
        CancelButton = close;
    }
}
