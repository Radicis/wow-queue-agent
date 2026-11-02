using System.Text.Json;

namespace WowQueueAgent;

public sealed class AgentConfig
{
    public int Port { get; set; } = 47821;
    public bool ShowPairingOnStart { get; set; } = true;
    public int PollIntervalMs { get; set; } = 1000;
    // Consecutive identical observations required before a state change is accepted.
    public int DebounceFrames { get; set; } = 2;
    public int SoonThresholdMinutes { get; set; } = 10;
    public string[] ProcessNames { get; set; } =
        { "Wow", "Wow-64", "WowClassic", "WowT", "WowB", "WowClassicT", "WowClassicB" };
    public string[] WindowTitles { get; set; } = { "World of Warcraft" };
    // The centre is OCR'd first; an occasional full-window pass handles UI text near the edges.
    public double CropX { get; set; } = 0.2;
    public double CropY { get; set; } = 0.2;
    public double CropWidth { get; set; } = 0.6;
    public double CropHeight { get; set; } = 0.6;
    public string OcrLanguage { get; set; } = "en-US";
}

public sealed class PatternConfig
{
    public string[] Disconnected { get; set; } =
    {
        @"disconnected\s+from\s+(the\s+)?server",
        @"you\s+have\s+been\s+disconnected",
        @"lost\s+connection",
    };

    public string[] Error { get; set; } =
    {
        @"unable\s+to\s+connect",
        @"failed\s+to\s+connect",
        @"connection\s+(timed\s+out|failed)",
        @"login\s+failed",
        @"incorrect\s+account",
        @"unable\s+to\s+authenticate",
        @"(account|game)\s+(has\s+been\s+)?(banned|suspended|locked)",
        @"version\s+mismatch",
        @"server\s+is\s+(down|unavailable)",
        @"realm\s+is\s+(down|offline)",
    };

    public string[] QueueIndicators { get; set; } =
    {
        @"position\s+in\s+queue",
        @"estimated\s+time",
        @"realm\s+is\s+full",
        @"login\s+servers?\s+are\s+full",
    };

    public string[] LoggedIn { get; set; } =
    {
        @"\benter\s+world\b",
        @"\bcreate\s+new\s+character\b",
        @"\bchoose\s+your\s+gameplay\s+ruleset\b",
        @"\bdelete\s+character\b",
    };

    // Character classes include common OCR digit lookalikes (O/o, I/l/i, S/s, B/b);
    // StateClassifier normalizes these to digits before parsing.
    public string QueuePosition { get; set; } = @"position\s+in\s+queue\s*:?\s*([\dOoIlSsBb.,]+)";
    public string QueueEta { get; set; } = @"estimated\s+time\s*:?\s*([^\n]+)";
}

public static class ConfigStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string BaseDir =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static AgentConfig LoadConfig()
    {
        return Load<AgentConfig>("config.json");
    }

    public static PatternConfig LoadPatterns() => Load<PatternConfig>("patterns.json");

    static T Load<T>(string file) where T : new()
    {
        var path = Path.Combine(BaseDir, file);
        if (File.Exists(path))
        {
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T(); }
            catch (Exception ex) { Log.Write($"Bad {file}, using defaults: {ex.Message}"); }
        }
        var fresh = new T();
        Save(file, fresh);
        return fresh;
    }

    static void Save<T>(string file, T value) =>
        File.WriteAllText(Path.Combine(BaseDir, file), JsonSerializer.Serialize(value, Json));
}

public static class Log
{
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(Path.Combine(ConfigStore.BaseDir, "agent.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
