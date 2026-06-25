namespace EnerkomChatbot.Core.Abstractions;

/// <summary>
/// Tenký wrapper nad embeddingovým modelem (Azure OpenAI text-embedding-3-small, 1536 dim).
/// Indexer i API musí používat stejný model/rozměr, jinak retrieval nefunguje.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>Spočítá embeddingy pro dávku textů (pořadí výstupu odpovídá vstupu).</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

    /// <summary>Spočítá embedding pro jediný dotaz (online cesta v API).</summary>
    Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default);
}
