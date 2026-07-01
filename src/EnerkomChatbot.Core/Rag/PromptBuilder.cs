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

    private static readonly string Template = LoadTemplate("system-prompt.cs.txt");
    private static readonly string ExpandTemplate = LoadTemplate("expand-query.cs.txt");
    private readonly OrgOptions _org;

    public PromptBuilder(IOptions<OrgOptions> org) => _org = org.Value;

    /// <summary>Systémový prompt s vyplněnou organizací, kontaktem a očíslovaným kontextem.</summary>
    public string BuildSystemPrompt(IReadOnlyList<SearchResult> hits) =>
        Template
            .Replace("{ORG_NAME}", _org.Name)
            .Replace("{CONTACT_URL}", _org.ContactUrl)
            .Replace("{CONTEXT}", BuildContext(hits));

    /// <summary>Hláška v kontextu, když retrieval nic nenašel (pozdravy a běžnou konverzaci LLM zvládne i tak).</summary>
    public const string EmptyContext =
        "(Pro tento dotaz nebyly nalezeny žádné relevantní úryvky z webu.)";

    /// <summary>Očíslované úryvky se zdroji. Číslo [n] koresponduje s pořadím v <see cref="BuildSources"/>.</summary>
    public static string BuildContext(IReadOnlyList<SearchResult> hits)
    {
        if (hits.Count == 0)
        {
            return EmptyContext;
        }

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

    /// <summary>
    /// Sestaví zprávy pro rozšíření dotazu: systémový pokyn + (volitelně) přepis konverzace a poslední
    /// dotaz. Výstupem modelu je až <paramref name="maxQueries"/> vyhledávacích dotazů, každý na řádku
    /// (viz <c>expand-query.cs.txt</c>).
    /// </summary>
    public static IReadOnlyList<ChatMessage> BuildExpansionMessages(IReadOnlyList<ChatMessage> history, string question, int maxQueries)
    {
        var sb = new StringBuilder();

        if (history.Count > 0)
        {
            sb.AppendLine("Historie konverzace:");
            foreach (var m in history)
            {
                var speaker = m.Role == ChatRoles.Assistant ? "Asistent" : "Uživatel";
                sb.Append(speaker).Append(": ").AppendLine(m.Content.Trim());
            }

            sb.AppendLine();
        }

        sb.Append("Poslední dotaz: ").AppendLine(question);
        sb.AppendLine();
        sb.Append($"Vygeneruj nejvýše {maxQueries} vyhledávacích dotazů, každý na samostatný řádek:");

        return
        [
            ChatMessage.System(ExpandTemplate),
            ChatMessage.User(sb.ToString()),
        ];
    }

    private static string LoadTemplate(string suffix)
    {
        var assembly = typeof(PromptBuilder).Assembly;
        var name = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded resource {suffix} nenalezen.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
