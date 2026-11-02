using WowQueueAgent;
using WowQueueAgent.Analysis;
using Xunit;

namespace WowQueueAgent.Tests;

public class ClassifierTests
{
    readonly StateClassifier _c = new(new PatternConfig());

    [Fact]
    public void QueueDialog_ParsesPositionAndEta()
    {
        var o = _c.Classify("Realm Queue\nPosition in queue: 1,234\nEstimated time: 12 minutes");
        Assert.Equal(QueueState.Queue, o.State);
        Assert.Equal(1234, o.Position);
        Assert.Equal(720, o.EtaSeconds);
    }

    [Theory]
    [InlineData("Estimated time: less than a minute", 60)]
    [InlineData("Estimated time: 1 hour 5 minutes", 3900)]
    [InlineData("Estimated time: 45 seconds", 45)]
    public void Eta_Variants(string text, int expected) =>
        Assert.Equal(expected, _c.Classify("Position in queue: 5\n" + text).EtaSeconds);

    [Fact]
    public void Disconnected() =>
        Assert.Equal(QueueState.Disconnected, _c.Classify("You have been disconnected from the server.").State);

    [Fact]
    public void Error() =>
        Assert.Equal(QueueState.Error, _c.Classify("Unable to connect").State);

    [Fact]
    public void NoDialog_IsLoggedIn() =>
        Assert.Equal(QueueState.LoggedIn, _c.Classify("").State);

    [Fact]
    public void DisconnectBeatsQueue() =>
        Assert.Equal(QueueState.Disconnected,
            _c.Classify("Position in queue: 3\nDisconnected from server").State);

    [Theory]
    // Simulates OCR misreading digits as lookalike letters (O/o->0, I/l/i->1, S/s->5, B/b->8).
    [InlineData("Position in queue: 1O5", 105)]
    [InlineData("Position in queue: l,2S4", 1254)]
    public void Position_CorrectsOcrDigitLookalikes(string text, int expected) =>
        Assert.Equal(expected, _c.Classify(text + "\nEstimated time: 1 minute").Position);

    [Fact]
    public void Eta_CorrectsOcrDigitLookalikes() =>
        Assert.Equal(300, _c.Classify("Position in queue: 5\nEstimated time: S minutes").EtaSeconds);
}

public class TrackerTests
{
    static Observation Q(int minutes) => new(QueueState.Queue, 10, minutes * 60);
    static Observation S(QueueState s) => new(s);

    static (StateTracker t, DateTimeOffset[] clock) Make()
    {
        var clock = new[] { DateTimeOffset.Now };
        return (new StateTracker(new AgentConfig { DebounceFrames = 2 }, () => clock[0]), clock);
    }

    [Fact]
    public void Debounce_RequiresConsecutiveFrames()
    {
        var (t, _) = Make();
        t.Update(S(QueueState.LoggedIn)); t.Update(S(QueueState.LoggedIn));
        t.Update(S(QueueState.Disconnected));
        Assert.Equal(QueueState.LoggedIn, t.Snapshot.State);
        var ev = t.Update(S(QueueState.Disconnected));
        Assert.Equal(QueueState.Disconnected, t.Snapshot.State);
        Assert.Contains(ev, e => e.Kind == "DISCONNECTED");
    }

    [Fact]
    public void StartupState_DoesNotNotify()
    {
        var (t, _) = Make();
        t.Update(S(QueueState.LoggedIn));
        Assert.Empty(t.Update(S(QueueState.LoggedIn)));
    }

    [Fact]
    public void QueueToLoggedIn_Notifies()
    {
        var (t, _) = Make();
        t.Update(Q(30)); t.Update(Q(30));
        t.Update(S(QueueState.LoggedIn));
        var ev = t.Update(S(QueueState.LoggedIn));
        Assert.Contains(ev, e => e.Kind == "LOGGED_IN");
    }

    [Fact]
    public void Soon_FiresOnceAndRearms()
    {
        var (t, _) = Make();
        t.Update(Q(30)); t.Update(Q(30));
        Assert.Empty(t.Update(Q(15)));
        Assert.Contains(t.Update(Q(10)), e => e.Kind == "QUEUE_SOON");
        Assert.True(t.Snapshot.Soon);
        Assert.DoesNotContain(t.Update(Q(9)), e => e.Kind == "QUEUE_SOON");
        t.Update(Q(20));
        Assert.Contains(t.Update(Q(8)), e => e.Kind == "QUEUE_SOON");
    }

    [Fact]
    public void RecentEvents_ReplayWindow()
    {
        var (t, clock) = Make();
        t.Update(S(QueueState.LoggedIn)); t.Update(S(QueueState.LoggedIn));
        t.Update(S(QueueState.Error)); t.Update(S(QueueState.Error));
        Assert.Single(t.RecentEvents(TimeSpan.FromMinutes(10)));
        clock[0] += TimeSpan.FromMinutes(11);
        Assert.Empty(t.RecentEvents(TimeSpan.FromMinutes(10)));
    }
}
