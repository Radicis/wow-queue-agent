using System.Drawing;
using System.Drawing.Drawing2D;

namespace WowQueueAgent;

// Original fantasy-game mock screens for testing capture and OCR without game art.
// Test controls sit outside the primary OCR crop region.
static class TestWindow
{
    static readonly Font TitleFont = new("Segoe UI", 21f, FontStyle.Bold);
    static readonly Font HeaderFont = new("Segoe UI", 16f, FontStyle.Bold);
    static readonly Font BodyFont = new("Segoe UI", 13f);
    static readonly Font SmallFont = new("Segoe UI", 10f);
    static readonly Font ButtonFont = new("Segoe UI", 12f, FontStyle.Bold);

    static Form? _form;
    static FantasySurface? _surface;
    static readonly List<Control> ScreenControls = new();
    static NumericUpDown? _minutes;

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
        var surface = new FantasySurface { Dock = DockStyle.Fill };
        _surface = surface;

        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(12, 10, 12, 0),
            BackColor = Color.FromArgb(10, 10, 20),
        };
        _minutes = new NumericUpDown { Minimum = 0, Maximum = 600, Value = 9, Width = 60 };

        Button TestButton(string text, Action onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        strip.Controls.Add(TestButton("Queue", ShowQueue));
        strip.Controls.Add(new Label
        {
            Text = "min:",
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(6, 8, 0, 0),
        });
        strip.Controls.Add(_minutes);
        strip.Controls.Add(TestButton("Disconnected", ShowDisconnected));
        strip.Controls.Add(TestButton("Reconnect", ShowReconnect));
        strip.Controls.Add(TestButton("Error", ShowError));
        strip.Controls.Add(TestButton("Ruleset", ShowRuleset));
        strip.Controls.Add(TestButton("Character select", ShowCharacterSelect));
        strip.Controls.Add(TestButton("Character create", ShowCharacterCreate));
        strip.Controls.Add(TestButton("Blank / Unknown", ShowBlank));
        _minutes.ValueChanged += (_, _) =>
        {
            if (_surface?.Controls.OfType<Panel>().Any(p => p.Name == "queue-dialog") == true)
                ShowQueue();
        };

        form.Controls.Add(surface);
        form.Controls.Add(strip);
        form.FormClosed += (_, _) =>
        {
            _form = null;
            _surface = null;
            _minutes = null;
            ScreenControls.Clear();
        };
        _form = form;
        ShowQueue();
        form.Show();
    }

    static void ClearScreen()
    {
        if (_surface == null) return;
        foreach (var control in ScreenControls)
        {
            _surface.Controls.Remove(control);
            control.Dispose();
        }
        ScreenControls.Clear();
    }

    static Panel AddPanel(Rectangle bounds, string? name = null)
    {
        var panel = new Panel
        {
            Bounds = bounds,
            Name = name ?? "",
            BackColor = Color.FromArgb(34, 29, 25),
        };
        panel.Paint += (_, e) =>
        {
            using var outer = new Pen(Color.FromArgb(191, 157, 98), 3f);
            using var inner = new Pen(Color.FromArgb(91, 73, 51), 1f);
            e.Graphics.DrawRectangle(outer, 1, 1, panel.Width - 3, panel.Height - 3);
            e.Graphics.DrawRectangle(inner, 6, 6, panel.Width - 13, panel.Height - 13);
        };
        AddControl(panel);
        return panel;
    }

    static Label AddText(Control parent, string text, Rectangle bounds, Font font,
        ContentAlignment alignment = ContentAlignment.MiddleCenter, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            Bounds = bounds,
            Font = font,
            ForeColor = color ?? Color.FromArgb(242, 222, 164),
            TextAlign = alignment,
            BackColor = Color.Transparent,
        };
        parent.Controls.Add(label);
        if (ReferenceEquals(parent, _surface)) ScreenControls.Add(label);
        return label;
    }

    static void AddControl(Control control)
    {
        if (_surface == null) return;
        _surface.Controls.Add(control);
        ScreenControls.Add(control);
    }

    static void AddButton(Control parent, string text, Rectangle bounds)
    {
        var button = new Panel
        {
            Bounds = bounds,
            BackColor = Color.FromArgb(91, 38, 27),
        };
        button.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(209, 171, 100), 2f);
            e.Graphics.DrawRectangle(pen, 1, 1, button.Width - 3, button.Height - 3);
        };
        AddText(button, text, new Rectangle(4, 2, bounds.Width - 8, bounds.Height - 4), ButtonFont);
        parent.Controls.Add(button);
    }

    static void ShowQueue()
    {
        ClearScreen();
        var box = AddPanel(new Rectangle(340, 195, 600, 285), "queue-dialog");
        AddText(box, "Realm is Full", new Rectangle(24, 18, 552, 48), TitleFont);
        AddText(box,
            $"Position in queue: 42\nEstimated time: {_minutes?.Value ?? 9} minutes",
            new Rectangle(28, 82, 544, 92), BodyFont);
        AddButton(box, "Change Realm", new Rectangle(92, 206, 190, 44));
        AddButton(box, "Cancel", new Rectangle(318, 206, 190, 44));
    }

    static void ShowDisconnected()
    {
        ClearScreen();
        var box = AddPanel(new Rectangle(270, 230, 740, 280));
        AddText(box, "Connection Interrupted", new Rectangle(24, 18, 692, 48), TitleFont);
        AddText(box, "You have been disconnected. (WOW51900102)",
            new Rectangle(32, 90, 676, 54), BodyFont);
        AddButton(box, "Help", new Rectangle(170, 190, 170, 44));
        AddButton(box, "Okay", new Rectangle(400, 190, 170, 44));
    }

    static void ShowReconnect()
    {
        ClearScreen();
        var box = AddPanel(new Rectangle(375, 270, 530, 180));
        AddButton(box, "Reconnect", new Rectangle(90, 35, 350, 50));
        AddButton(box, "Log Out", new Rectangle(150, 105, 230, 42));
    }

    static void ShowError()
    {
        ClearScreen();
        var box = AddPanel(new Rectangle(320, 230, 640, 270));
        AddText(box, "Unable to Connect", new Rectangle(24, 18, 592, 48), TitleFont);
        AddText(box, "Please check your connection and try again.",
            new Rectangle(32, 90, 576, 54), BodyFont);
        AddButton(box, "Okay", new Rectangle(220, 190, 200, 44));
    }

    static void ShowRuleset()
    {
        ClearScreen();
        AddText(_surface!, "Choose Your Gameplay Ruleset",
            new Rectangle(200, 12, 880, 64), TitleFont, color: Color.White);

        var choices = new[]
        {
            ("Normal", "Explore a cooperative world."),
            ("PvP", "Compete against other players."),
            ("Roleplay", "Create stories with other players."),
            ("Roleplay PvP", "Roleplay in a contested world."),
        };
        for (int i = 0; i < choices.Length; i++)
        {
            var (title, description) = choices[i];
            var card = AddPanel(new Rectangle(35 + i * 305, 145, 275, 370));
            AddText(card, title, new Rectangle(12, 16, 251, 42), HeaderFont);
            var emblem = new Panel
            {
                Bounds = new Rectangle(95, 74, 85, 85),
                BackColor = Color.FromArgb(74, 63, 47),
            };
            emblem.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(216, 177, 93), 3f);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawEllipse(pen, 4, 4, emblem.Width - 9, emblem.Height - 9);
                e.Graphics.DrawLine(pen, 22, 42, 62, 42);
                e.Graphics.DrawLine(pen, 42, 22, 42, 62);
            };
            card.Controls.Add(emblem);
            AddText(card, description, new Rectangle(18, 180, 239, 118), SmallFont,
                color: Color.FromArgb(224, 218, 198));
            AddButton(card, "Select", new Rectangle(57, 310, 161, 40));
        }
    }

    static void ShowCharacterSelect()
    {
        ClearScreen();
        AddCharacterModel(new Rectangle(430, 125, 390, 455));

        var list = AddPanel(new Rectangle(1038, 72, 232, 490));
        AddText(list, "Realm: Silvervale", new Rectangle(10, 12, 212, 42), HeaderFont);
        AddText(list, "Characters\n\nZan\nLevel 70 Mage\n\nBananastick\nLevel 70 Warrior",
            new Rectangle(14, 65, 204, 300), BodyFont, ContentAlignment.TopLeft);
        AddButton(list, "Select Character", new Rectangle(20, 408, 192, 46));

        var actions = AddPanel(new Rectangle(270, 584, 740, 72));
        AddButton(actions, "Enter World", new Rectangle(22, 12, 190, 46));
        AddButton(actions, "Create New Character", new Rectangle(256, 12, 230, 46));
        AddButton(actions, "Delete Character", new Rectangle(530, 12, 185, 46));
    }

    static void ShowCharacterCreate()
    {
        ClearScreen();
        AddCharacterModel(new Rectangle(430, 125, 390, 455));

        var factions = AddPanel(new Rectangle(8, 70, 226, 492));
        AddText(factions, "ALLIANCE", new Rectangle(12, 12, 202, 44), HeaderFont);
        AddText(factions, "Human\nDwarf\nElf\n\nHORDE\n\nOrc\nTroll\nTauren",
            new Rectangle(18, 68, 190, 380), BodyFont, ContentAlignment.TopLeft);

        var details = AddPanel(new Rectangle(1038, 70, 232, 492));
        AddText(details, "High Elf", new Rectangle(10, 12, 212, 42), HeaderFont);
        AddText(details, "Racial Traits\n\nKeen Senses\nArcane Affinity\n\nClass\nMage",
            new Rectangle(14, 66, 204, 330), BodyFont, ContentAlignment.TopLeft);

        var actions = AddPanel(new Rectangle(270, 584, 740, 72));
        AddButton(actions, "Create Character", new Rectangle(180, 12, 220, 46));
        AddButton(actions, "Back", new Rectangle(450, 12, 130, 46));
    }

    static void ShowBlank()
    {
        ClearScreen();
    }

    static void AddCharacterModel(Rectangle bounds)
    {
        var model = new CharacterSilhouette { Bounds = bounds };
        AddControl(model);
    }

    sealed class FantasySurface : Panel
    {
        public FantasySurface()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(28, 30, 38);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var sky = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(39, 55, 75), Color.FromArgb(106, 57, 29), LinearGradientMode.Vertical);
            g.FillRectangle(sky, ClientRectangle);

            var horizon = (int)(Height * 0.68);
            using var far = new SolidBrush(Color.FromArgb(74, 76, 80));
            using var near = new SolidBrush(Color.FromArgb(39, 40, 43));
            g.FillPolygon(far, new[]
            {
                new Point(0, horizon + 65), new Point(Width / 5, horizon - 45),
                new Point(Width / 3, horizon + 28), new Point(Width * 3 / 5, horizon - 70),
                new Point(Width * 4 / 5, horizon + 20), new Point(Width, horizon - 35),
                new Point(Width, Height), new Point(0, Height),
            });
            g.FillPolygon(near, new[]
            {
                new Point(0, horizon + 108), new Point(Width / 4, horizon + 54),
                new Point(Width / 2, horizon + 92), new Point(Width * 3 / 4, horizon + 48),
                new Point(Width, horizon + 100), new Point(Width, Height), new Point(0, Height),
            });

            using var arch = new Pen(Color.FromArgb(97, 63, 39), 28f);
            g.DrawArc(arch, -250, -360, Width + 500, Height + 500, 190, 160);
            using var trim = new Pen(Color.FromArgb(163, 120, 71), 4f);
            g.DrawArc(trim, -235, -345, Width + 470, Height + 470, 190, 160);
        }
    }

    sealed class CharacterSilhouette : Control
    {
        public CharacterSilhouette()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width / 2f;
            using var shadow = new SolidBrush(Color.FromArgb(95, 11, 12, 18));
            using var armor = new SolidBrush(Color.FromArgb(48, 63, 78));
            using var trim = new SolidBrush(Color.FromArgb(183, 133, 60));
            using var skin = new SolidBrush(Color.FromArgb(191, 154, 119));

            g.FillEllipse(shadow, cx - 48, Height - 58, 96, 22);
            g.FillEllipse(skin, cx - 23, 24, 46, 54);
            g.FillPolygon(armor, new[]
            {
                new Point((int)cx - 48, 88), new Point((int)cx + 48, 88),
                new Point((int)cx + 66, 210), new Point((int)cx + 32, 270),
                new Point((int)cx - 32, 270), new Point((int)cx - 66, 210),
            });
            g.FillPolygon(trim, new[]
            {
                new Point((int)cx - 45, 93), new Point((int)cx, 119),
                new Point((int)cx + 45, 93), new Point((int)cx + 28, 139),
                new Point((int)cx, 154), new Point((int)cx - 28, 139),
            });
            g.FillRectangle(armor, cx - 33, 266, 28, 132);
            g.FillRectangle(armor, cx + 5, 266, 28, 132);
            g.FillPolygon(skin, new[]
            {
                new Point((int)cx - 62, 112), new Point((int)cx - 88, 232),
                new Point((int)cx - 70, 240), new Point((int)cx - 42, 144),
            });
            g.FillPolygon(skin, new[]
            {
                new Point((int)cx + 62, 112), new Point((int)cx + 88, 232),
                new Point((int)cx + 70, 240), new Point((int)cx + 42, 144),
            });
        }
    }
}
