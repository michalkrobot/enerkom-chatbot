namespace EnerkomChatbot.Core.Models;

/// <summary>
/// Chunk připravený k uložení do tabulky <c>documents</c> (včetně embeddingu a hashů).
/// </summary>
public sealed record DocumentChunk
{
    public required string SourceType { get; init; }
    public required string SourceUri { get; init; }
    public string? Title { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Content { get; init; }

    /// <summary>Hash obsahu chunku.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Hash celého zdroje (detekce změny zdroje).</summary>
    public required string SourceHash { get; init; }

    public int? TokenCount { get; init; }

    /// <summary>Embedding (1536 dim, text-embedding-3-small).</summary>
    public required float[] Embedding { get; init; }
}
