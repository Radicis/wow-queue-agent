using Windows.Globalization;
using Windows.Media.Ocr;
using WowQueueAgent.Analysis;
using WowQueueAgent.Capture;
using WowQueueAgent.Net;

namespace WowQueueAgent;

public sealed class QueueMonitor
{
    readonly AgentConfig _cfg;
    readonly WindowFinder _finder;
    readonly WindowCapturer _capturer = new();
    readonly StateClassifier _classifier;
    readonly StateTracker _tracker;
    readonly AgentServer _server;
    readonly OcrEngine _ocr;
    QueueState _lastLogged = QueueState.Unknown;

    public QueueMonitor(AgentConfig cfg, PatternConfig patterns, StateTracker tracker, AgentServer server)
    {
        _cfg = cfg;
        _tracker = tracker;
        _server = server;
        _finder = new WindowFinder(cfg);
        _classifier = new StateClassifier(patterns);
        _ocr = OcrEngine.TryCreateFromLanguage(new Language(cfg.OcrLanguage))
               ?? OcrEngine.TryCreateFromUserProfileLanguages()
               ?? throw new InvalidOperationException(
                   "No Windows OCR language available. Install the English language pack in Windows settings.");
    }

    public string Status { get; private set; } = "Starting";

    public async Task RunAsync(CancellationToken ct)
    {
        var lastBroadcast = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var obs = await ObserveAsync();
                if (obs != null)
                {
                    foreach (var e in _tracker.Update(obs))
                    {
                        Log.Write($"EVENT {e.Kind}");
                        await _server.BroadcastEventAsync(e);
                    }
                    var state = _tracker.Snapshot.State;
                    if (state != _lastLogged)
                    {
                        _lastLogged = state;
                        Log.Write($"STATE {state} | OCR: {obs.Text?.ReplaceLineEndings(" / ")}");
                    }
                    Status = $"{state}" + (_tracker.Snapshot.EtaSeconds is { } s ? $" ({s / 60} min)" : "");
                    await _server.BroadcastStateAsync();
                    lastBroadcast = DateTimeOffset.Now;
                }
                else if (DateTimeOffset.Now - lastBroadcast > TimeSpan.FromSeconds(3))
                {
                    await _server.BroadcastStateAsync();
                    lastBroadcast = DateTimeOffset.Now;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Loop error: " + ex);
                _capturer.Stop();
            }

            try { await Task.Delay(_cfg.PollIntervalMs, ct); } catch (OperationCanceledException) { }
        }
        _capturer.Dispose();
    }

    // Null means "no usable frame this tick": keep the previous state and just heartbeat.
    async Task<Observation?> ObserveAsync()
    {
        var hwnd = _finder.Find();
        if (hwnd == IntPtr.Zero)
        {
            _capturer.Stop();
            return new Observation(QueueState.NoClient);
        }
        if (NativeMethods.IsIconic(hwnd)) return null;

        if (hwnd != _capturer.Hwnd || !_capturer.IsActive)
        {
            if (!_capturer.Start(hwnd)) return null;
            await Task.Delay(300);
        }

        var full = await _capturer.GrabAsync();
        using var frame = full == null ? null : WindowCapturer.Crop(full, _cfg);
        if (frame == null)
        {
            Status = "No usable frame (is WoW in exclusive fullscreen?)";
            return null;
        }
        var result = await _ocr.RecognizeAsync(frame);
        return _classifier.Classify(result.Text);
    }
}
