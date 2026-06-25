using EnerkomChatbot.Core.Models;

namespace EnerkomChatbot.Core.Abstractions;

/// <summary>Úložiště vektorů (PostgreSQL + pgvector). Sdílí indexer (zápis) i API (čtení).</summary>
public interface IVectorStore
{
    /// <summary>Vrátí <c>source_hash</c> existujících chunků daného zdroje, nebo <c>null</c> když zdroj v DB není.</summary>
    Task<string?> GetSourceHashAsync(string sourceType, string sourceUri, CancellationToken cancellationToken = default);

    /// <summary>Označí chunky nezměněného zdroje aktuálním během (mark-and-sweep) bez přepočtu embeddingů.</summary>
    Task MarkSourceSeenAsync(string sourceType, string sourceUri, Guid run, CancellationToken cancellationToken = default);

    /// <summary>Smaže staré chunky zdroje a vloží nové (nový/změněný zdroj).</summary>
    Task UpsertSourceChunksAsync(string sourceType, string sourceUri, IReadOnlyList<DocumentChunk> chunks, Guid run, CancellationToken cancellationToken = default);

    /// <summary>Smaže chunky, které nebyly viděny v daném běhu (zmizelé zdroje). Vrací počet smazaných.</summary>
    Task<int> SweepAsync(Guid run, CancellationToken cancellationToken = default);

    /// <summary>Vektorové hledání top-k chunků (cosine), zahodí výsledky pod prahem <paramref name="minSimilarity"/>.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(float[] embedding, int k, double minSimilarity, CancellationToken cancellationToken = default);
}
