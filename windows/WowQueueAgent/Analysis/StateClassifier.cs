using System.Text.RegularExpressions;

namespace WowQueueAgent.Analysis;

public sealed class StateClassifier
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly string[] RequiredDisconnectedIndicators =
    {
        @"have\s+been\s+disconnected",
    };

    static readonly Regex Reconnect = new(@"\breconnect\b", Opts);
    static readonly Regex LogOut = new(@"\blog\s*out\b", Opts);
    static readonly Regex Alliance = new(@"\balliance\b", Opts);
    static readonly Regex Horde = new(@"\bhorde\b", Opts);
    static readonly Regex CharacterCreation = new(@"\b(racial\s+traits|character\s+creation|character\s+name)\b", Opts);

    readonly Regex[] _disconnected, _error, _queue, _loggedIn;
    readonly Regex _position, _eta;

    public StateClassifier(PatternConfig p)
    {
        _disconnected = p.Disconnected.Concat(RequiredDisconnectedIndicators)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => new Regex(x, Opts)).ToArray();
        _error = p.Error.Select(x => new Regex(x, Opts)).ToArray();
        _queue = p.QueueIndicators.Select(x => new Regex(x, Opts)).ToArray();
        _loggedIn = p.LoggedIn.Select(x => new Regex(x, Opts)).ToArray();
        _position = new Regex(p.QueuePosition, Opts);
        _eta = new Regex(p.QueueEta, Opts);
    }

    public Observation Classify(string text)
    {
        if (_disconnected.Any(r => r.IsMatch(text))) return new(QueueState.Disconnected, Text: text);
        if (_error.Any(r => r.IsMatch(text))) return new(QueueState.Error, Text: text);

        if (_queue.Any(r => r.IsMatch(text)))
        {
            int? position = null, eta = null;
            var pm = _position.Match(text);
            if (pm.Success && pm.Groups.Count > 1)
            {
                var digits = new string(FixOcrDigits(pm.Groups[1].Value).Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n)) position = n;
            }
            var em = _eta.Match(text);
            if (em.Success && em.Groups.Count > 1) eta = ParseEta(em.Groups[1].Value);
            return new(QueueState.Queue, position, eta, text);
        }

        if (_loggedIn.Any(r => r.IsMatch(text)) ||
            (Alliance.IsMatch(text) && Horde.IsMatch(text) && CharacterCreation.IsMatch(text)))
            return new(QueueState.LoggedIn, Text: text);

        if (Reconnect.IsMatch(text) && LogOut.IsMatch(text))
            return new(QueueState.Disconnected, Text: text);

        return new(QueueState.Unknown, Text: text);
    }

    static readonly Regex Unit = new(@"([\dOoIlSsBb]+)\s*(hours?|hrs?|minutes?|mins?|seconds?|secs?)\b", Opts);
    static readonly Regex LessThan = new(
        @"(?:<\s*[1Il]|less\s+than\s+(?:a|[1Il]|one)|under\s+(?:a|[1Il]|one))\s*(?:minutes?|mins?)\b", Opts);

    // Maps characters the OCR engine commonly confuses with digits, so queue
    // position/ETA numbers still parse correctly even when misread as letters.
    static readonly Dictionary<char, char> DigitLookalikes = new()
    {
        ['O'] = '0', ['o'] = '0',
        ['I'] = '1', ['l'] = '1', ['i'] = '1',
        ['S'] = '5', ['s'] = '5',
        ['B'] = '8', ['b'] = '8',
    };

    static string FixOcrDigits(string s) =>
        new(s.Select(c => DigitLookalikes.TryGetValue(c, out var d) ? d : c).ToArray());

    public static int? ParseEta(string text)
    {
        if (LessThan.IsMatch(text)) return 60;
        var total = 0;
        var found = false;
        foreach (Match m in Unit.Matches(text))
        {
            var digits = FixOcrDigits(m.Groups[1].Value);
            if (!int.TryParse(digits, out var value)) continue;
            found = true;
            var unit = m.Groups[2].Value.ToLowerInvariant();
            total += unit.StartsWith('h') ? value * 3600 : unit.StartsWith('m') ? value * 60 : value;
        }
        return found ? total : null;
    }
}
