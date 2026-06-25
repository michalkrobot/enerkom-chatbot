namespace EnerkomChatbot.Core.Options;

/// <summary>Jeden web k procházení (položka <c>Indexer:Sites</c>). Viz <see cref="IndexerOptions"/>.</summary>
public sealed class WebSiteOptions
{
    /// <summary>URL sitemap.xml (preferováno). Prázdné = jen BFS fallback od kořene.</summary>
    public string SitemapUrl { get; set; } = "";

    /// <summary>Kořen pro BFS fallback, když sitemap chybí nebo je prázdná.</summary>
    public string? CrawlFallbackRootUrl { get; set; }

    /// <summary>Hloubka BFS fallbacku; <c>null</c> = převzít globální <see cref="IndexerOptions.MaxCrawlDepth"/>.</summary>
    public int? MaxCrawlDepth { get; set; }

    /// <summary>URL, které se z tohoto webu vyřadí (balast: vyhledávání, kariéra apod.).</summary>
    public string[] ExcludeUrls { get; set; } = [];
}
