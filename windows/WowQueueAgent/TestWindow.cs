using System.Drawing;

namespace WowQueueAgent;

// Fake "World of Warcraft" window for trying the pipeline without the real game.
// Controls sit in a bottom strip so they fall outside the OCR crop region.
static class TestWindow
{
    static Form? _form;

    public static void Show()
    {
        if (_form is { IsDisposed: false })
        {
            _form.Activate();
            return;
        }

        var form = new Form
        {
            Text = "World of Warcraft",
            ClientSize = new Size(1280, 720),
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Color.FromArgb(20, 20, 40),
        };

        var dialog = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Arial", 24f),
        };

        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12, 10, 12, 0),
            BackColor = Color.FromArgb(10, 10, 20),
        };

        var minutes = new NumericUpDown { Minimum = 0, Maximum = 600, Value = 9, Width = 60 };
        void Set(string text) => dialog.Text = text;
        Button Btn(string text, Action onClick)
        {
            var b = new Button { Text = text, AutoSize = true, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            b.Click += (_, _) => onClick();
            return b;
        }

        void Queue() => Set($"Realm Queue\nPosition in queue: 42\nEstimated time: {minutes.Value} minutes");

        strip.Controls.Add(Btn("Queue", Queue));
        strip.Controls.Add(new Label { Text = "min:", ForeColor = Color.White, AutoSize = true, Margin = new Padding(6, 8, 0, 0) });
        strip.Controls.Add(minutes);
        strip.Controls.Add(Btn("Disconnected", () => Set("You have been disconnected from the server.")));
        strip.Controls.Add(Btn("Error", () => Set("Unable to connect")));
        strip.Controls.Add(Btn("Logged in (no dialog)", () => Set("")));
        minutes.ValueChanged += (_, _) => { if (dialog.Text.StartsWith("Realm Queue")) Queue(); };

        form.Controls.Add(dialog);
        form.Controls.Add(strip);
        Queue();
        form.FormClosed += (_, _) => _form = null;
        _form = form;
        form.Show();
    }
}
