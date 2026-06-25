namespace EnerkomChatbot.Indexer;

/// <summary>Co indexovat (z argumentů příkazové řádky).</summary>
public sealed record IndexingRunOptions
{
    public bool Web { get; init; }
    public bool Docs { get; init; }

    /// <summary>Nezapisuje do DB ani nevolá embeddings — jen statistiky.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Bez <c>--web</c>/<c>--docs</c> = obojí. <c>--dry-run</c> = jen statistiky.
    /// </summary>
    public static IndexingRunOptions Parse(string[] args)
    {
        var web = args.Contains("--web");
        var docs = args.Contains("--docs");
        var dryRun = args.Contains("--dry-run");

        if (!web && !docs)
        {
            web = true;
            docs = true;
        }

        return new IndexingRunOptions { Web = web, Docs = docs, DryRun = dryRun };
    }
}
