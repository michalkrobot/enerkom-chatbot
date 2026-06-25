using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnerkomChatbot.Indexer.Sources;

/// <summary>
/// Stahuje stránky webu: primárně ze <c>sitemap.xml</c>, fallback BFS od kořene (stejná doména).
/// HTML čistí AngleSharpem (odstraní nav/footer/script…), respektuje robots.txt a crawl-delay.
/// Viz docs/03-indexer.md.
/// </summary>
public sealed partial class WebCrawler(
    HttpClient httpClient,
    IOptions<IndexerOptions> options,
    ILogger<WebCrawler> logger) : ISourceLoader
{
    private static readonly string[] NoiseSelectors =
        ["script", "style", "nav", "header", "footer", "aside", "noscript",
         "[role=navigation]", "[role=banner]", "[role=contentinfo]",
         ".cookie", ".cookies", "#cookie", "#cookies", ".cookie-bar"];

    private readonly IndexerOptions _options = options.Value;
    private readonly HtmlParser _parser = new();

    public string Name => "web";

    public async IAsyncEnumerable<RawSource> LoadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var robots = await LoadRobotsAsync(cancellationToken);
        var delay = EffectiveDelay(robots);

        var urls = await ResolveUrlsAsync(robots, cancellationToken);
        if (urls.Count == 0)
        {
            logger.LogWarning("Crawler nenašel žádné URL (sitemap ani fallback).");
            yield break;
        }

        logger.LogInformation("Crawler zpracuje {Count} URL (delay {Delay} ms).", urls.Count, delay);

        var first = true;
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!first)
            {
                await Task.Delay(delay, cancellationToken);
            }

            first = false;

            RawSource? source = null;
            try
            {
                var html = await httpClient.GetStringAsync(url, cancellationToken);
                source = CleanPage(url, html);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nepodařilo se stáhnout/zpracovat {Url} — přeskakuji.", url);
            }

            if (source is not null && !string.IsNullOrWhiteSpace(source.Text))
            {
                yield return source;
            }
        }
    }

    private RawSource CleanPage(string url, string html)
    {
        var doc = _parser.ParseDocument(html);

        foreach (var selector in NoiseSelectors)
        {
            foreach (var element in doc.QuerySelectorAll(selector))
            {
                element.Remove();
            }
        }

        var main = doc.QuerySelector("main")
                   ?? doc.QuerySelector("article")
                   ?? (IElement?)doc.Body;

        var text = NormalizeWhitespace(main?.TextContent ?? "");
        var title = ExtractTitle(doc);

        return new RawSource { SourceType = "web", Uri = url, Title = title, Text = text };
    }

    private static string? ExtractTitle(IDocument doc)
    {
        var ogTitle = doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content");
        if (!string.IsNullOrWhiteSpace(ogTitle))
        {
            return ogTitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(doc.Title))
        {
            return doc.Title.Trim();
        }

        return doc.QuerySelector("h1")?.TextContent.Trim();
    }

    private async Task<IReadOnlyList<string>> ResolveUrlsAsync(RobotsTxt robots, CancellationToken cancellationToken)
    {
        var exclude = new HashSet<string>(_options.ExcludeUrls, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_options.SitemapUrl))
        {
            try
            {
                var fromSitemap = await FetchSitemapUrlsAsync(_options.SitemapUrl, depth: 0, cancellationToken);
                var filtered = fromSitemap.Where(u => !exclude.Contains(u)).Distinct().ToList();
                if (filtered.Count > 0)
                {
                    return filtered;
                }

                logger.LogWarning("Sitemap {Url} prázdná — zkouším BFS fallback.", _options.SitemapUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sitemap {Url} se nepodařilo načíst — zkouším BFS fallback.", _options.SitemapUrl);
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.CrawlFallbackRootUrl))
        {
            return await CrawlBfsAsync(_options.CrawlFallbackRootUrl!, robots, exclude, cancellationToken);
        }

        return [];
    }

    private async Task<List<string>> FetchSitemapUrlsAsync(string sitemapUrl, int depth, CancellationToken cancellationToken)
    {
        var xml = await httpClient.GetStringAsync(sitemapUrl, cancellationToken);
        var doc = XDocument.Parse(xml);
        var isIndex = doc.Root?.Name.LocalName == "sitemapindex";

        var locs = doc.Descendants()
            .Where(e => e.Name.LocalName == "loc")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .ToList();

        if (!isIndex)
        {
            return locs;
        }

        var urls = new List<string>();
        if (depth >= 2)
        {
            return urls;
        }

        foreach (var nested in locs)
        {
            try
            {
                urls.AddRange(await FetchSitemapUrlsAsync(nested, depth + 1, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Vnořená sitemap {Url} selhala.", nested);
            }
        }

        return urls;
    }

    private async Task<List<string>> CrawlBfsAsync(string rootUrl, RobotsTxt robots, HashSet<string> exclude, CancellationToken cancellationToken)
    {
        const int maxPages = 200;
        var root = new Uri(rootUrl);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var queue = new Queue<(string Url, int Depth)>();
        queue.Enqueue((root.GetLeftPart(UriPartial.Path), 0));
        var delay = EffectiveDelay(robots);
        var first = true;

        while (queue.Count > 0 && result.Count < maxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (url, depth) = queue.Dequeue();

            if (!visited.Add(url) || exclude.Contains(url) || depth > _options.MaxCrawlDepth)
            {
                continue;
            }

            var uri = new Uri(url);
            if (uri.Host != root.Host || !robots.IsAllowed(uri.AbsolutePath))
            {
                continue;
            }

            if (!first)
            {
                await Task.Delay(delay, cancellationToken);
            }

            first = false;

            string html;
            try
            {
                html = await httpClient.GetStringAsync(url, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "BFS: {Url} selhalo.", url);
                continue;
            }

            result.Add(url);

            if (depth < _options.MaxCrawlDepth)
            {
                foreach (var link in ExtractLinks(html, uri))
                {
                    if (!visited.Contains(link))
                    {
                        queue.Enqueue((link, depth + 1));
                    }
                }
            }
        }

        return result;
    }

    private IEnumerable<string> ExtractLinks(string html, Uri baseUri)
    {
        var doc = _parser.ParseDocument(html);
        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
            {
                continue;
            }

            if (Uri.TryCreate(baseUri, href, out var abs)
                && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            {
                yield return abs.GetLeftPart(UriPartial.Path);
            }
        }
    }

    private async Task<RobotsTxt> LoadRobotsAsync(CancellationToken cancellationToken)
    {
        if (!_options.RespectRobotsCrawlDelay)
        {
            return RobotsTxt.Empty;
        }

        var baseUrl = !string.IsNullOrWhiteSpace(_options.CrawlFallbackRootUrl)
            ? _options.CrawlFallbackRootUrl
            : _options.SitemapUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return RobotsTxt.Empty;
        }

        try
        {
            var robotsUrl = new Uri(new Uri(baseUrl), "/robots.txt");
            var content = await httpClient.GetStringAsync(robotsUrl, cancellationToken);
            return RobotsTxt.Parse(content);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "robots.txt nedostupné — pokračuji bez něj.");
            return RobotsTxt.Empty;
        }
    }

    private int EffectiveDelay(RobotsTxt robots)
    {
        var baseDelay = _options.RequestDelayMs;
        if (_options.RespectRobotsCrawlDelay && robots.CrawlDelaySeconds is { } seconds)
        {
            return Math.Max(baseDelay, (int)(seconds * 1000));
        }

        return baseDelay;
    }

    private static string NormalizeWhitespace(string text)
    {
        var collapsed = WhitespaceRuns().Replace(text, " ");
        return MultiNewline().Replace(collapsed, "\n\n").Trim();
    }

    [GeneratedRegex(@"[ \t\f\v ]+")]
    private static partial Regex WhitespaceRuns();

    [GeneratedRegex(@"(\s*\n\s*){2,}")]
    private static partial Regex MultiNewline();
}
