using System.Reflection;
using System.Text;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Options;

namespace EnerkomChatbot.Core.Rag;

/// <summary>
/// Sestavuje systémový prompt (CZ, anti-halucinace), očíslovaný kontext a citace.
/// Šablona promptu je v embedded resource <c>Rag/Prompts/system-prompt.cs.txt</c>.
/// Viz docs/05-prompts.md.
/// </summary>
public sealed class PromptBuilder
{
    /// <summary>Hláška při vyčerpání limitu (HTTP 429).</summary>
    public const string RateLimitMessage =
        "Omlouvám se, služba je teď dočasně vytížená. Zkuste to prosím za chvíli znovu.";

    private static readonly string Template = LoadTemplate();
    private readonly OrgOptions _org;

    public PromptBuilder(IOptions<OrgOptions> org) => _org = org.Value;

    /// <summary>Systémový prompt s vyplněnou organizací, kontaktem a očíslovaným kontextem.</summary>
    public string BuildSystemPrompt(IReadOnlyList<SearchResult> hits) =>
        Template
            .Replace("{ORG_NAME}", _org.Name)
            .Replace("{CONTACT}", _org.Contact)
            .Replace("{CONTEXT}", BuildContext(hits));

    /// <summary>Očíslované úryvky se zdroji. Číslo [n] koresponduje s pořadím v <see cref="BuildSources"/>.</summary>
    public static string BuildContext(IReadOnlyList<SearchResult> hits)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var title = string.IsNullOrWhiteSpace(hit.Title) ? hit.SourceUri : hit.Title;
            var header = hit.SourceType == "web"
                ? $"[{i + 1}] ({hit.SourceType} — {title}, {hit.SourceUri})"
                : $"[{i + 1}] ({hit.SourceType} — {title})";

            sb.AppendLine(header);
            sb.AppendLine(hit.Content.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Citace pro klienta — paralelně k číslování v kontextu (index n → sources[n-1]).</summary>
    public static IReadOnlyList<Source> BuildSources(IReadOnlyList<SearchResult> hits) =>
        [.. hits.Select(h => new Source
        {
            Title = string.IsNullOrWhiteSpace(h.Title) ? h.SourceUri : h.Title!,
            Uri = h.SourceUri,
            Type = h.SourceType,
        })];

    /// <summary>Předdefinovaná odpověď, když retrieval nic nenajde (negeneruje se přes LLM).</summary>
    public string FallbackAnswer() =>
        $"Na tuto otázku jsem ve zdrojích webu nenašel odpověď. Zkuste prosím dotaz " +
        $"přeformulovat, nebo se obraťte přímo na nás: {_org.Contact}.";

    private static string LoadTemplate()
    {
        var assembly = typeof(PromptBuilder).Assembly;
        var name = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith("system-prompt.cs.txt", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded resource system-prompt.cs.txt nenalezen.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
