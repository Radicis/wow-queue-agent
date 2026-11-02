using System.Drawing;
using QRCoder;
using WowQueueAgent.Analysis;
using WowQueueAgent.Net;

namespace WowQueueAgent;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "WowQueueAgent.SingleInstance", out var first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        try { Run(); }
        catch (Exception ex)
        {
            Log.Write("Fatal: " + ex);
            MessageBox.Show(ex.Message, "WoW Queue Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void Run()
    {
        var cfg = ConfigStore.LoadConfig();
        var patterns = ConfigStore.LoadPatterns();
        var tracker = new StateTracker(cfg);
        var server = new AgentServer(cfg, tracker);
        server.StartAsync().GetAwaiter().GetResult();
        var monitor = new QueueMonitor(cfg, patterns, tracker, server);

        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => monitor.RunAsync(cts.Token));

        var menu = new ContextMenuStrip();
        var status = new ToolStripMenuItem("Starting") { Enabled = false };
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show pairing QR", null, (_, _) => ShowPairing(server));
        menu.Items.Add("Launch test app", null, (_, _) => TestWindow.Show());
        menu.Items.Add("Open config folder", null, (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", ConfigStore.BaseDir));
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        using var icon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "WoW Queue Agent",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => ShowPairing(server);

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            var text = $"{monitor.Status} | phones: {server.ClientCount}";
            status.Text = text;
            icon.Text = ("WoW: " + text)[..Math.Min(63, text.Length + 5)];
        };
        timer.Start();

        if (cfg.ShowPairingOnStart) ShowPairing(server);

        Application.Run();

        timer.Stop();
        icon.Visible = false;
        cts.Cancel();
        try { loop.Wait(3000); } catch { }
        server.DisposeAsync().AsTask().Wait(3000);
    }

    static Form? _pairingForm;

    static string AppVersion =>
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "?").Split('+')[0];

    static void ShowPairing(AgentServer server)
    {
        if (_pairingForm is { IsDisposed: false })
        {
            _pairingForm.WindowState = FormWindowState.Normal;
            _pairingForm.Activate();
            return;
        }

        var hosts = server.GetHosts();
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(server.PairingPayload(), QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        using var ms = new MemoryStream(qr.GetGraphic(8));
        var image = new Bitmap(ms);

        var form = new Form
        {
            Text = $"WoW Queue Agent v{AppVersion} - Pair your phone",
            StartPosition = FormStartPosition.CenterScreen,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            Font = new Font("Segoe UI", 10f),
        };

        var qrBox = new PictureBox
        {
            Image = image, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(300, 300),
            Margin = new Padding(0, 4, 20, 0),
        };

        var right = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            Width = 380,
        };
        Label Text(string t, float size = 10f, FontStyle style = FontStyle.Regular, int bottom = 8) => new()
        {
            Text = t, AutoSize = true, MaximumSize = new Size(380, 0),
            Font = new Font("Segoe UI", size, style), Margin = new Padding(0, 0, 0, bottom),
        };

        right.Controls.Add(Text("Pair your phone", 16f, FontStyle.Bold, 4));
        right.Controls.Add(Text("Your phone must be on the same Wi-Fi / network as this PC.", bottom: 12));
        right.Controls.Add(Text("1. Install and open the WoW Queue app.", bottom: 2));
        right.Controls.Add(Text("2. Tap \"Scan QR code\" and point it at the code on the left.", bottom: 2));
        right.Controls.Add(Text("3. Done - the app starts showing your queue status.", bottom: 14));

        right.Controls.Add(Text("Can't scan? Enter these in the app:", 10f, FontStyle.Bold, 4));
        right.Controls.Add(Text("PC address", 9f, bottom: 0));
        var addresses = hosts.Length > 0 ? hosts : new[] { "(no network found)" };
        for (int i = 0; i < addresses.Length; i++)
        {
            var addr = addresses[i];
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
            row.Controls.Add(new Label
            {
                Text = addr, AutoSize = true, Font = new Font("Consolas", i == 0 ? 22f : 12f, FontStyle.Bold),
                Margin = new Padding(0, 0, 10, 0),
            });
            if (hosts.Length > 0)
            {
                var copy = new Button { Text = "Copy", AutoSize = true, Anchor = AnchorStyles.Left };
                copy.Click += (_, _) => { Clipboard.SetText(addr); copy.Text = "Copied"; };
                row.Controls.Add(copy);
            }
            right.Controls.Add(row);
        }
        right.Controls.Add(Text("Port", 9f, bottom: 0));
        right.Controls.Add(Text(server.Port.ToString(), 22f, FontStyle.Bold, 10));
        if (hosts.Length > 1)
            right.Controls.Add(Text("Several addresses? Use the first (large) one; try the others only if it fails.", 9f, bottom: 10));
        right.Controls.Add(Text(
            "If the phone can't connect, allow WowQueueAgent through Windows Firewall (Private networks) " +
            "and make sure your router doesn't isolate Wi-Fi clients.", 9f, bottom: 12));

        var test = new Button { Text = "Launch test app (fake WoW window)", AutoSize = true };
        test.Click += (_, _) => TestWindow.Show();
        right.Controls.Add(test);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(20),
        };
        layout.Controls.Add(qrBox);
        layout.Controls.Add(right);
        form.Controls.Add(layout);
        form.FormClosed += (_, _) => { image.Dispose(); _pairingForm = null; };
        _pairingForm = form;
        form.Show();
        form.Activate();
    }
}
