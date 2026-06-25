namespace EnerkomChatbot.Core.Options;

/// <summary>Parametry chunkeru (sekce <c>Indexer:Chunk</c>).</summary>
public sealed class ChunkOptions
{
    public const string SectionName = "Indexer:Chunk";

    public int MaxTokens { get; set; } = 500;
    public int OverlapTokens { get; set; } = 80;
}
