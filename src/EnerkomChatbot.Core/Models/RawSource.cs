namespace EnerkomChatbot.Core.Models;

/// <summary>
/// Surový zdroj načtený indexerem (webová stránka nebo dokument) před chunkováním.
/// </summary>
public sealed record RawSource
{
    /// <summary>Typ zdroje: "web" | "pdf" | "docx" | "md" | "txt".</summary>
    public required string SourceType { get; init; }

    /// <summary>URL stránky nebo cesta/název souboru.</summary>
    public required string Uri { get; init; }

    /// <summary>Titulek stránky / název dokumentu (může chybět).</summary>
    public string? Title { get; init; }

    /// <summary>Extrahovaný čistý text zdroje.</summary>
    public required string Text { get; init; }
}
