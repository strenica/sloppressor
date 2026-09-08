using System.Diagnostics;

namespace VideoSizeCompressor.App;

internal static class Ui
{
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(37, 86, 204);

    public static void Configure(Form form, string title, Size size)
    {
        form.Text = title;
        form.ClientSize = size;
        form.MinimumSize = new Size(Math.Min(size.Width + 16, 480), Math.Min(size.Height + 39, 360));
        form.StartPosition = FormStartPosition.CenterScreen;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = SystemColors.Window;
        form.ForeColor = SystemColors.WindowText;
        form.Padding = new Padding(24);
        form.Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        form.Shown += (_, _) =>
        {
            Rectangle area = Screen.FromControl(form).WorkingArea;
            if (form.Width <= area.Width - 24 && form.Height <= area.Height - 24) return;
            form.Size = new Size(Math.Min(form.Width, area.Width - 24), Math.Min(form.Height, area.Height - 24));
            form.Location = new Point(area.Left + (area.Width - form.Width) / 2, area.Top + (area.Height - form.Height) / 2);
        };
    }

    public static Label Label(string text, float size = 10, bool bold = false) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 10)
    };

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(105, 36), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(8, 0, 0, 0) };
        if (primary && !SystemInformation.HighContrast)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
        }
        return button;
    }

    public static TableLayoutPanel Column()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0, Margin = Padding.Empty, AutoScroll = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }
    public static void Add(TableLayoutPanel table, Control control, float height = 0)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(height == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(control, 0, row);
    }

    public static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
        panel.Controls.AddRange(buttons);
        return panel;
    }

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open folder", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
