namespace EnerkomChatbot.Core.Models;

/// <summary>Část textu zdroje připravená k embeddingu.</summary>
public sealed record Chunk
{
    /// <summary>Pořadí chunku v rámci zdroje (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>Text chunku.</summary>
    public required string Content { get; init; }

    /// <summary>Přibližný počet tokenů (heuristika).</summary>
    public required int TokenCount { get; init; }
}
