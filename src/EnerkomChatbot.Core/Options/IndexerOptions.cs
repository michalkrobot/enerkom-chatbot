namespace EnerkomChatbot.Core.Options;

/// <summary>Konfigurace indexeru (sekce <c>Indexer</c>).</summary>
public sealed class IndexerOptions
{
    public const string SectionName = "Indexer";

    public string SitemapUrl { get; set; } = "";
    public string? CrawlFallbackRootUrl { get; set; }
    public int MaxCrawlDepth { get; set; } = 3;

    /// <summary>
    /// Další weby k procházení. Ploché <see cref="SitemapUrl"/>/<see cref="CrawlFallbackRootUrl"/>
    /// tvoří první web (zpětná kompatibilita); zde se přidávají ostatní (např. EDC).
    /// Všechny weby se crawlují v jednom běhu (sweep maže globálně — viz docs/03-indexer.md).
    /// </summary>
    public List<WebSiteOptions> Sites { get; set; } = [];

    /// <summary>Složka s dokumenty (PDF/DOCX/MD/TXT). Dev: lokální FS.</summary>
    public string DocumentsPath { get; set; } = "data/knowledge-base";

    public ChunkOptions Chunk { get; set; } = new();

    public int EmbeddingBatchSize { get; set; } = 100;

    /// <summary>Prodleva mezi HTTP requesty na web (robots.txt Crawl-delay).</summary>
    public int RequestDelayMs { get; set; } = 10000;

    public bool RespectRobotsCrawlDelay { get; set; } = true;

    /// <summary>URL, které se z indexace vyřadí (duplicitní/testovací stránky).</summary>
    public string[] ExcludeUrls { get; set; } = [];

    public string UserAgent { get; set; } = "EnerkomChatbotIndexer/1.0 (+https://www.enerkomhp.cz)";
}
