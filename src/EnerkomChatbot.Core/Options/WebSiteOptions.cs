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

    /// <summary>
    /// Allow-list prefixů URL. Prázdné = ber vše (kromě <see cref="ExcludeUrls"/>).
    /// Neprázdné = ber jen URL začínající některým z prefixů; vše ostatní se zahodí
    /// (i kdyby to nebylo v <see cref="ExcludeUrls"/>). Použití: crawlovat jen tematickou
    /// podsekci webu (např. jen <c>/blog</c> a FAQ, ne ceníky/kontakty/účet).
    /// </summary>
    public string[] IncludeUrlPrefixes { get; set; } = [];

    /// <summary>
    /// Konkrétní URL k indexaci (přesně, včetně query stringu). Když je neprázdné,
    /// tento web se NEprochází přes sitemap ani BFS — načtou se právě jen tyto stránky.
    /// Vhodné pro pár konkrétních stránek aplikace, která nemá sitemapu.
    /// </summary>
    public string[] Urls { get; set; } = [];
}
