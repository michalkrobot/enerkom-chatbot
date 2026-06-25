using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Storage;
using Npgsql;
using Xunit;

namespace EnerkomChatbot.Core.Tests;

public sealed class PgVectorStoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const int Dim = 1536;

    /// <summary>A 1536-dim unit vector with a single non-zero component (cosine-friendly).</summary>
    private static float[] UnitVector(int hot)
    {
        var v = new float[Dim];
        v[hot] = 1f;
        return v;
    }

    private NpgsqlDataSource RequireDataSource()
    {
        Assert.SkipUnless(fixture.SkipReason is null, fixture.SkipReason ?? "skipped");
        return fixture.DataSource!;
    }

    /// <summary>The container is shared across the class; start each test from an empty table.</summary>
    private static async Task TruncateAsync(NpgsqlDataSource dataSource)
    {
        await using var cmd = dataSource.CreateCommand("TRUNCATE documents;");
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static DocumentChunk Chunk(string uri, int index, string content, float[] embedding, string sourceHash) =>
        new()
        {
            SourceType = "web",
            SourceUri = uri,
            Title = "Titulek",
            ChunkIndex = index,
            Content = content,
            ContentHash = $"chash-{uri}-{index}",
            SourceHash = sourceHash,
            TokenCount = 10,
            Embedding = embedding,
        };

    [Fact]
    public async Task SearchAsync_ReturnsNearestChunkFirst()
    {
        // Arrange
        var dataSource = RequireDataSource();
        await TruncateAsync(dataSource);
        var store = new PgVectorStore(dataSource);
        var run = Guid.NewGuid();
        const string uri = "https://enerkomhp.cz/search-nearest";

        var near = UnitVector(0);    // matches the query direction
        var far = UnitVector(500);   // orthogonal to the query

        await store.UpsertSourceChunksAsync("web", uri,
        [
            Chunk(uri, 0, "Vzdálený obsah.", far, "hash-1"),
            Chunk(uri, 1, "Blízký obsah.", near, "hash-1"),
        ], run);

        // Act — query along the "near" direction.
        var results = await store.SearchAsync(UnitVector(0), k: 5, minSimilarity: 0.0);

        // Assert
        Assert.NotEmpty(results);
        Assert.Equal("Blízký obsah.", results[0].Content);
    }

    [Fact]
    public async Task GetSourceHashAsync_ReturnsStoredHash()
    {
        // Arrange
        var dataSource = RequireDataSource();
        await TruncateAsync(dataSource);
        var store = new PgVectorStore(dataSource);
        var run = Guid.NewGuid();
        const string uri = "https://enerkomhp.cz/hash-test";
        const string expectedHash = "source-hash-xyz";

        await store.UpsertSourceChunksAsync("web", uri,
        [
            Chunk(uri, 0, "Obsah.", UnitVector(1), expectedHash),
        ], run);

        // Act
        var hash = await store.GetSourceHashAsync("web", uri);

        // Assert
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public async Task GetSourceHashAsync_UnknownSource_ReturnsNull()
    {
        // Arrange
        var dataSource = RequireDataSource();
        await TruncateAsync(dataSource);
        var store = new PgVectorStore(dataSource);

        // Act
        var hash = await store.GetSourceHashAsync("web", "https://enerkomhp.cz/does-not-exist");

        // Assert
        Assert.Null(hash);
    }

    [Fact]
    public async Task MarkSourceSeenAsync_UpdatesRun()
    {
        // Arrange
        var dataSource = RequireDataSource();
        await TruncateAsync(dataSource);
        var store = new PgVectorStore(dataSource);
        var oldRun = Guid.NewGuid();
        var newRun = Guid.NewGuid();
        const string uri = "https://enerkomhp.cz/mark-seen";

        await store.UpsertSourceChunksAsync("web", uri,
        [
            Chunk(uri, 0, "Obsah.", UnitVector(2), "hash-1"),
        ], oldRun);

        // Act — mark the source as seen by the new run, then sweep the old run.
        await store.MarkSourceSeenAsync("web", uri, newRun);

        // Assert — a sweep targeting the new run must NOT delete this source (it carries newRun now).
        var deleted = await store.SweepAsync(newRun);
        Assert.Equal(0, deleted);
        Assert.Equal("hash-1", await store.GetSourceHashAsync("web", uri));
    }

    [Fact]
    public async Task SweepAsync_DeletesChunksFromOtherRuns_AndReturnsCount()
    {
        // Arrange
        var dataSource = RequireDataSource();
        await TruncateAsync(dataSource);
        var store = new PgVectorStore(dataSource);
        var staleRun = Guid.NewGuid();
        var currentRun = Guid.NewGuid();

        const string staleUri = "https://enerkomhp.cz/sweep-stale";
        const string keepUri = "https://enerkomhp.cz/sweep-keep";

        await store.UpsertSourceChunksAsync("web", staleUri,
        [
            Chunk(staleUri, 0, "Staré 1.", UnitVector(3), "hash-stale"),
            Chunk(staleUri, 1, "Staré 2.", UnitVector(4), "hash-stale"),
        ], staleRun);

        await store.UpsertSourceChunksAsync("web", keepUri,
        [
            Chunk(keepUri, 0, "Nové.", UnitVector(5), "hash-keep"),
        ], currentRun);

        // Act — sweep everything not belonging to the current run.
        var deleted = await store.SweepAsync(currentRun);

        // Assert
        Assert.Equal(2, deleted);
        Assert.Null(await store.GetSourceHashAsync("web", staleUri));
        Assert.Equal("hash-keep", await store.GetSourceHashAsync("web", keepUri));
    }

    [Fact]
    public async Task UpsertSourceChunksAsync_ReplacesExistingChunksForSource()
    {
        // Arrange
        var dataSource = RequireDataSource();
        await TruncateAsync(dataSource);
        var store = new PgVectorStore(dataSource);
        var run = Guid.NewGuid();
        const string uri = "https://enerkomhp.cz/upsert-replace";

        await store.UpsertSourceChunksAsync("web", uri,
        [
            Chunk(uri, 0, "Verze 1.", UnitVector(6), "hash-v1"),
            Chunk(uri, 1, "Verze 1 pokr.", UnitVector(7), "hash-v1"),
        ], run);

        // Act — re-upsert with a single chunk and a new source hash.
        await store.UpsertSourceChunksAsync("web", uri,
        [
            Chunk(uri, 0, "Verze 2.", UnitVector(6), "hash-v2"),
        ], run);

        // Assert
        Assert.Equal("hash-v2", await store.GetSourceHashAsync("web", uri));
        var results = await store.SearchAsync(UnitVector(6), k: 10, minSimilarity: 0.0);
        var forSource = results.Where(r => r.SourceUri == uri).ToList();
        Assert.Single(forSource);
        Assert.Equal("Verze 2.", forSource[0].Content);
    }
}
