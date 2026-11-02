namespace WowQueueAgent.Analysis;

// Debounces raw observations into a stable state and derives notification events.
public sealed class StateTracker
{
    readonly int _debounce;
    readonly int _soonSeconds;
    readonly Func<DateTimeOffset> _clock;
    readonly string _bootId = Guid.NewGuid().ToString("N")[..8];
    readonly List<AgentEvent> _events = new();

    QueueState _stable = QueueState.Unknown;
    QueueState _pending = QueueState.Unknown;
    int _pendingCount;
    DateTimeOffset _since;
    DateTimeOffset _observedAt;
    int? _position, _eta;
    DateTimeOffset? _etaUpdatedAt;
    bool _soonArmed = true;
    int _seq;

    public StateTracker(AgentConfig cfg, Func<DateTimeOffset>? clock = null)
    {
        _debounce = Math.Max(1, cfg.DebounceFrames);
        _soonSeconds = cfg.SoonThresholdMinutes * 60;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _since = _observedAt = _clock();
    }

    public string BootId => _bootId;

    public StateSnapshot Snapshot => new(_stable, _position, _eta, _etaUpdatedAt, IsSoon, _since, _observedAt);

    bool IsSoon => _stable == QueueState.Queue && _eta is { } e && e <= _soonSeconds;

    public IReadOnlyList<AgentEvent> RecentEvents(TimeSpan window)
    {
        var cutoff = _clock() - window;
        lock (_events) return _events.Where(e => e.At >= cutoff).ToList();
    }

    public IReadOnlyList<AgentEvent> Update(Observation obs)
    {
        var now = _clock();
        _observedAt = now;
        var fired = new List<AgentEvent>();

        if (obs.State == _pending) _pendingCount++;
        else { _pending = obs.State; _pendingCount = 1; }

        if (_pending != _stable && _pendingCount >= _debounce)
        {
            var previous = _stable;
            _stable = _pending;
            _since = now;
            _position = _eta = null;
            _etaUpdatedAt = null;
            if (previous != QueueState.Unknown) fired.AddRange(TransitionEvents(_stable, now));
            if (_stable != QueueState.Queue) _soonArmed = true;
        }

        if (_stable == QueueState.Queue && obs.State == QueueState.Queue)
        {
            _position = obs.Position ?? _position;
            if (obs.EtaSeconds is { } eta)
            {
                if (eta != _eta) _etaUpdatedAt = now;
                _eta = eta;
            }
        }

        if (_stable == QueueState.Queue && _eta is { } e)
        {
            if (_soonArmed && e <= _soonSeconds)
            {
                _soonArmed = false;
                fired.Add(Make("QUEUE_SOON", $"Almost there: about {Math.Max(1, e / 60)} min left in queue", now));
            }
            else if (!_soonArmed && e > _soonSeconds * 3 / 2) _soonArmed = true;
        }

        lock (_events)
        {
            _events.AddRange(fired);
            _events.RemoveAll(x => x.At < now - TimeSpan.FromHours(1));
        }
        return fired;
    }

    IEnumerable<AgentEvent> TransitionEvents(QueueState s, DateTimeOffset now)
    {
        switch (s)
        {
            case QueueState.LoggedIn: yield return Make("LOGGED_IN", "You are logged in", now); break;
            case QueueState.Disconnected: yield return Make("DISCONNECTED", "Disconnected from the server", now); break;
            case QueueState.Error: yield return Make("ERROR", "The login screen shows an error", now); break;
            case QueueState.NoClient: yield return Make("NO_CLIENT", "WoW client not found", now); break;
        }
    }

    AgentEvent Make(string kind, string message, DateTimeOffset at) =>
        new($"{_bootId}:{++_seq}", kind, message, at);
}
