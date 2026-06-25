namespace EnerkomChatbot.Core.Models;

/// <summary>Výsledek vektorového hledání — chunk s podobností k dotazu.</summary>
public sealed record SearchResult
{
    public required long Id { get; init; }
    public required string SourceType { get; init; }
    public required string SourceUri { get; init; }
    public string? Title { get; init; }
    public required string Content { get; init; }

    /// <summary>Kosinová podobnost (1 = shodné, vyšší = lepší).</summary>
    public required double Similarity { get; init; }
}
