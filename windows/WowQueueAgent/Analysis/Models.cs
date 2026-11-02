namespace WowQueueAgent.Analysis;

public enum QueueState { Unknown, NoClient, LoggedIn, Queue, Disconnected, Error }

public static class QueueStateExtensions
{
    public static string ToWire(this QueueState s) => s switch
    {
        QueueState.NoClient => "NO_CLIENT",
        QueueState.LoggedIn => "LOGGED_IN",
        QueueState.Queue => "QUEUE",
        QueueState.Disconnected => "DISCONNECTED",
        QueueState.Error => "ERROR",
        _ => "UNKNOWN",
    };
}

public sealed record Observation(QueueState State, int? Position = null, int? EtaSeconds = null, string? Text = null);

public sealed record AgentEvent(string Id, string Kind, string Message, DateTimeOffset At);

public sealed record StateSnapshot(
    QueueState State,
    int? Position,
    int? EtaSeconds,
    DateTimeOffset? EtaUpdatedAt,
    bool Soon,
    DateTimeOffset Since,
    DateTimeOffset ObservedAt);
