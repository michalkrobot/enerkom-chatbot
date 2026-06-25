using System.Diagnostics;
using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Exceptions;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using EnerkomChatbot.Indexer.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnerkomChatbot.Indexer;

/// <summary>
/// Orchestrace indexace: load → hash → (nezměněn? mark : chunk → embed → upsert) → mark-and-sweep.
/// Sekvenčně přes zdroje, embeddings v dávkách. Viz docs/03-indexer.md.
/// </summary>
public sealed class IndexingPipeline(
    IEnumerable<ISourceLoader> loaders,
    IChunker chunker,
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    IIndexingRunStore runStore,
    IDatabaseInitializer databaseInitializer,
    IOptions<IndexerOptions> options,
    ILogger<IndexingPipeline> logger)
{
    private readonly IndexerOptions _options = options.Value;

    public async Task RunAsync(IndexingRunOptions runOptions, CancellationToken cancellationToken)
    {
        var selected = SelectLoaders(runOptions).ToList();
        if (selected.Count == 0)
        {
            logger.LogWarning("Žádný zdroj nevybrán (--web/--docs).");
            return;
        }

        if (runOptions.DryRun)
        {
            await DryRunAsync(selected, cancellationToken);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var runId = Guid.NewGuid();
        await databaseInitializer.EnsureSchemaAsync(cancellationToken);
        await runStore.StartAsync(runId, $"loaders: {string.Join("+", selected.Select(l => l.Name))}", cancellationToken);

        var stats = new RunStats();
        try
        {
            foreach (var loader in selected)
            {
                await foreach (var source in loader.LoadAsync(cancellationToken))
                {
                    await ProcessSourceAsync(source, runId, stats, cancellationToken);
                }
            }

            var swept = await vectorStore.SweepAsync(runId, cancellationToken);
            stopwatch.Stop();

            logger.LogInformation(
                "Indexace hotová za {Seconds}s: zdrojů {Sources} (nové {New}, změněné {Changed}, beze změny {Unchanged}), chunků {Chunks}, embed volání {Embed}, smazáno {Swept}.",
                stopwatch.Elapsed.TotalSeconds, stats.Sources, stats.New, stats.Changed, stats.Unchanged, stats.Chunks, stats.EmbedCalls, swept);

            await runStore.FinishAsync(runId, "success", stats.Sources, stats.Chunks,
                $"new={stats.New} changed={stats.Changed} unchanged={stats.Unchanged} swept={swept}", cancellationToken);
        }
        catch (RateLimitedException ex)
        {
            logger.LogError(ex, "Indexace selhala na rate limitu embeddings — běh ukončen (raději spadnout než uložit nekonzistentní index).");
            await runStore.FinishAsync(runId, "failed", stats.Sources, stats.Chunks, ex.Message, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Indexace selhala.");
            await runStore.FinishAsync(runId, "failed", stats.Sources, stats.Chunks, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task ProcessSourceAsync(RawSource source, Guid runId, RunStats stats, CancellationToken cancellationToken)
    {
        stats.Sources++;
        var sourceHash = Hashing.Sha256(source.Text);

        try
        {
            var existing = await vectorStore.GetSourceHashAsync(source.SourceType, source.Uri, cancellationToken);
            if (existing == sourceHash)
            {
                await vectorStore.MarkSourceSeenAsync(source.SourceType, source.Uri, runId, cancellationToken);
                stats.Unchanged++;
                logger.LogDebug("Beze změny: {Type} {Uri}", source.SourceType, source.Uri);
                return;
            }

            var documentChunks = await BuildChunksAsync(source, sourceHash, stats, cancellationToken);
            await vectorStore.UpsertSourceChunksAsync(source.SourceType, source.Uri, documentChunks, runId, cancellationToken);

            stats.Chunks += documentChunks.Count;
            if (existing is null)
            {
                stats.New++;
            }
            else
            {
                stats.Changed++;
            }

            logger.LogInformation("Indexováno: {Type} {Uri} ({Chunks} chunků)", source.SourceType, source.Uri, documentChunks.Count);
        }
        catch (RateLimitedException)
        {
            throw; // rate limit ukončí celý běh
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chyba u zdroje {Type} {Uri} — přeskakuji.", source.SourceType, source.Uri);
        }
    }

    private async Task<IReadOnlyList<DocumentChunk>> BuildChunksAsync(RawSource source, string sourceHash, RunStats stats, CancellationToken cancellationToken)
    {
        var chunks = chunker.Chunk(source.Text);
        if (chunks.Count == 0)
        {
            return [];
        }

        var result = new List<DocumentChunk>(chunks.Count);
        foreach (var batch in chunks.Chunk(_options.EmbeddingBatchSize))
        {
            var contents = batch.Select(c => c.Content).ToArray();
            var embeddings = await embeddingClient.EmbedBatchAsync(contents, cancellationToken);
            stats.EmbedCalls++;

            for (var i = 0; i < batch.Length; i++)
            {
                var chunk = batch[i];
                result.Add(new DocumentChunk
                {
                    SourceType = source.SourceType,
                    SourceUri = source.Uri,
                    Title = source.Title,
                    ChunkIndex = chunk.Index,
                    Content = chunk.Content,
                    ContentHash = Hashing.Sha256(chunk.Content),
                    SourceHash = sourceHash,
                    TokenCount = chunk.TokenCount,
                    Embedding = embeddings[i],
                });
            }
        }

        return result;
    }

    private async Task DryRunAsync(IReadOnlyList<ISourceLoader> loaders, CancellationToken cancellationToken)
    {
        logger.LogInformation("DRY-RUN — nezapisuji do DB, nevolám embeddings.");
        var totalSources = 0;
        var totalChunks = 0;

        foreach (var loader in loaders)
        {
            await foreach (var source in loader.LoadAsync(cancellationToken))
            {
                var chunks = chunker.Chunk(source.Text);
                totalSources++;
                totalChunks += chunks.Count;
                logger.LogInformation("[dry] {Type} {Uri}: {Chars} znaků → {Chunks} chunků",
                    source.SourceType, source.Uri, source.Text.Length, chunks.Count);
            }
        }

        logger.LogInformation("DRY-RUN hotovo: zdrojů {Sources}, chunků {Chunks}.", totalSources, totalChunks);
    }

    private IEnumerable<ISourceLoader> SelectLoaders(IndexingRunOptions runOptions)
    {
        foreach (var loader in loaders)
        {
            if ((runOptions.Web && loader.Name == "web") || (runOptions.Docs && loader.Name == "docs"))
            {
                yield return loader;
            }
        }
    }

    private sealed class RunStats
    {
        public int Sources;
        public int New;
        public int Changed;
        public int Unchanged;
        public int Chunks;
        public int EmbedCalls;
    }
}
