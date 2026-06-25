namespace EnerkomChatbot.Indexer.Sources;

/// <summary>
/// Minimalistický parser robots.txt — bere skupinu pro <c>User-agent: *</c>:
/// <c>Disallow</c> prefixy a <c>Crawl-delay</c>. Pro vlastní web bohatě stačí.
/// </summary>
internal sealed class RobotsTxt
{
    private readonly List<string> _disallow = [];

    public double? CrawlDelaySeconds { get; private set; }

    public static RobotsTxt Empty => new();

    public bool IsAllowed(string absolutePath)
    {
        foreach (var prefix in _disallow)
        {
            if (prefix.Length > 0 && absolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static RobotsTxt Parse(string content)
    {
        var robots = new RobotsTxt();
        var inWildcardGroup = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "user-agent":
                    inWildcardGroup = value == "*";
                    break;
                case "disallow" when inWildcardGroup:
                    if (value.Length > 0)
                    {
                        robots._disallow.Add(value);
                    }

                    break;
                case "crawl-delay" when inWildcardGroup:
                    if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var delay))
                    {
                        robots.CrawlDelaySeconds = delay;
                    }

                    break;
            }
        }

        return robots;
    }
}
