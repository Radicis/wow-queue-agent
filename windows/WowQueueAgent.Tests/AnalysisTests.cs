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
    [InlineData("Estimated time: <1 minute", 60)]
    [InlineData("Estimated time: < 1 min", 60)]
    [InlineData("Estimated time: less than 1 minute", 60)]
    [InlineData("Estimated time: under a minute", 60)]
    [InlineData("Estimated time: 1 hour 5 minutes", 3900)]
    [InlineData("Estimated time: 45 seconds", 45)]
    public void Eta_Variants(string text, int expected) =>
        Assert.Equal(expected, _c.Classify("Position in queue: 5\n" + text).EtaSeconds);

    [Theory]
    [InlineData("Realm is Full\nPosition in queue: 5\nEstimated time: 45 seconds", 5, 45)]
    [InlineData("Login Servers are Full\nPosition in queue: 1,234\nEstimated time: 12 minutes", 1234, 720)]
    [InlineData("Realm is Full", null, null)]
    public void FullServerDialogs_AreQueue(string text, int? position, int? eta) {
        var o = _c.Classify(text);
        Assert.Equal(QueueState.Queue, o.State);
        Assert.Equal(position, o.Position);
        Assert.Equal(eta, o.EtaSeconds);
    }

    [Theory]
    [InlineData("Choose Your Gameplay Ruleset")]
    [InlineData("Realm\nEnter World\nCreate New Character\nDelete Character")]
    [InlineData("Alliance\nHorde\nHigh Elf\nRacial Traits\nMage")]
    public void PostLoginScreens_AreLoggedIn(string text) =>
        Assert.Equal(QueueState.LoggedIn, _c.Classify(text).State);

    [Fact]
    public void Disconnected() =>
        Assert.Equal(QueueState.Disconnected, _c.Classify("You have been disconnected from the server.").State);

    [Theory]
    [InlineData("Reconnect\nLog Out")]
    [InlineData("have been disconnected. (WOW51900102)")]
    public void AlternateDisconnectScreens_AreDisconnected(string text)
    {
        var oldConfig = new PatternConfig
        {
            Disconnected = new[]
            {
                @"disconnected\s+from\s+(the\s+)?server",
                @"you\s+have\s+been\s+disconnected",
                @"lost\s+connection",
            },
        };
        Assert.Equal(QueueState.Disconnected, new StateClassifier(oldConfig).Classify(text).State);
    }

    [Fact]
    public void Error() =>
        Assert.Equal(QueueState.Error, _c.Classify("Unable to connect").State);

    [Fact]
    public void BlankOcr_IsUnknown() =>
        Assert.Equal(QueueState.Unknown, _c.Classify("").State);

    [Fact]
    public void UnmatchedNonEmptyText_IsUnknown() =>
        Assert.Equal(QueueState.Unknown, _c.Classify("World of Warcraft").State);

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
    public void LoggedInAfterUnknown_NotifiesAfterStartup()
    {
        var (t, _) = Make();
        t.Update(Q(30)); t.Update(Q(30));
        t.Update(S(QueueState.Unknown)); t.Update(S(QueueState.Unknown));
        t.Update(S(QueueState.LoggedIn));
        Assert.Contains(t.Update(S(QueueState.LoggedIn)), e => e.Kind == "LOGGED_IN");
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
