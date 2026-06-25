namespace EnerkomChatbot.Core.Options;

/// <summary>Konfigurace retrievalu (sekce <c>Retrieval</c>).</summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    public int TopK { get; set; } = 5;
    public double MinSimilarity { get; set; } = 0.5;
}
