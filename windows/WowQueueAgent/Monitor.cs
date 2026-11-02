using Windows.Globalization;
using Windows.Media.Ocr;
using WowQueueAgent.Analysis;
using WowQueueAgent.Capture;
using WowQueueAgent.Net;

namespace WowQueueAgent;

public sealed class QueueMonitor
{
    static readonly TimeSpan FullFrameOcrInterval = TimeSpan.FromSeconds(5);

    readonly AgentConfig _cfg;
    readonly WindowFinder _finder;
    readonly WindowCapturer _capturer = new();
    readonly StateClassifier _classifier;
    readonly StateTracker _tracker;
    readonly AgentServer _server;
    readonly OcrEngine _ocr;
    QueueState _lastLogged = QueueState.Unknown;
    DateTimeOffset? _lastFullFrameOcrAt;
    Observation? _lastFullFrameObservation;

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
            ResetFullFrameFallback();
            return new Observation(QueueState.NoClient);
        }
        if (NativeMethods.IsIconic(hwnd)) return null;

        if (hwnd != _capturer.Hwnd || !_capturer.IsActive)
        {
            ResetFullFrameFallback();
            if (!_capturer.Start(hwnd)) return null;
            await Task.Delay(300);
        }

        var full = await _capturer.GrabAsync();
        if (full == null)
        {
            Status = "No usable frame (is WoW in exclusive fullscreen?)";
            return null;
        }

        using var frame = WindowCapturer.Crop(full, _cfg);
        if (frame == null)
        {
            Status = "No usable frame (is WoW in exclusive fullscreen?)";
            return null;
        }

        var result = await _ocr.RecognizeAsync(frame);
        var observation = _classifier.Classify(result.Text);
        if (observation.State != QueueState.Unknown)
        {
            ResetFullFrameFallback();
            return observation;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastFullFrameOcrAt is { } last && now - last < FullFrameOcrInterval)
            return _lastFullFrameObservation ?? observation;

        _lastFullFrameOcrAt = now;
        using var fullFrame = WindowCapturer.Crop(full, _cfg, fullWindow: true);
        if (fullFrame == null) return observation;
        var fullResult = await _ocr.RecognizeAsync(fullFrame);
        _lastFullFrameObservation = _classifier.Classify(fullResult.Text);
        return _lastFullFrameObservation;
    }

    void ResetFullFrameFallback()
    {
        _lastFullFrameOcrAt = null;
        _lastFullFrameObservation = null;
    }
}
