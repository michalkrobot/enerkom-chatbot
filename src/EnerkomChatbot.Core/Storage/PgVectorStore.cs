using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Models;
using Npgsql;
using Pgvector;

namespace EnerkomChatbot.Core.Storage;

/// <summary>
/// <see cref="IVectorStore"/> nad PostgreSQL + pgvector (čistý Npgsql, bez ORM).
/// Viz docs/02-database.md.
/// </summary>
public sealed class PgVectorStore(NpgsqlDataSource dataSource) : IVectorStore
{
    public async Task<string?> GetSourceHashAsync(string sourceType, string sourceUri, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT source_hash FROM documents
            WHERE source_type = @t AND source_uri = @u
            LIMIT 1;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("t", sourceType);
        cmd.Parameters.AddWithValue("u", sourceUri);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task MarkSourceSeenAsync(string sourceType, string sourceUri, Guid run, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE documents SET indexing_run = @run, indexed_at = now()
            WHERE source_type = @t AND source_uri = @u;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("run", run);
        cmd.Parameters.AddWithValue("t", sourceType);
        cmd.Parameters.AddWithValue("u", sourceUri);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertSourceChunksAsync(string sourceType, string sourceUri, IReadOnlyList<DocumentChunk> chunks, Guid run, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM documents WHERE source_type = @t AND source_uri = @u;", connection, transaction))
        {
            delete.Parameters.AddWithValue("t", sourceType);
            delete.Parameters.AddWithValue("u", sourceUri);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
            INSERT INTO documents
                (source_type, source_uri, title, chunk_index, content, content_hash, source_hash, token_count, embedding, indexing_run)
            VALUES
                (@t, @u, @title, @idx, @content, @chash, @shash, @tokens, @emb, @run);
            """;

        await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
        var pType = insert.Parameters.Add(new NpgsqlParameter("t", NpgsqlTypes.NpgsqlDbType.Text));
        var pUri = insert.Parameters.Add(new NpgsqlParameter("u", NpgsqlTypes.NpgsqlDbType.Text));
        var pTitle = insert.Parameters.Add(new NpgsqlParameter("title", NpgsqlTypes.NpgsqlDbType.Text));
        var pIdx = insert.Parameters.Add(new NpgsqlParameter("idx", NpgsqlTypes.NpgsqlDbType.Integer));
        var pContent = insert.Parameters.Add(new NpgsqlParameter("content", NpgsqlTypes.NpgsqlDbType.Text));
        var pCHash = insert.Parameters.Add(new NpgsqlParameter("chash", NpgsqlTypes.NpgsqlDbType.Text));
        var pSHash = insert.Parameters.Add(new NpgsqlParameter("shash", NpgsqlTypes.NpgsqlDbType.Text));
        var pTokens = insert.Parameters.Add(new NpgsqlParameter("tokens", NpgsqlTypes.NpgsqlDbType.Integer));
        var pEmb = insert.Parameters.Add(new NpgsqlParameter { ParameterName = "emb" }); // typ vector se odvodí z hodnoty
        var pRun = insert.Parameters.Add(new NpgsqlParameter("run", NpgsqlTypes.NpgsqlDbType.Uuid));

        foreach (var chunk in chunks)
        {
            pType.Value = chunk.SourceType;
            pUri.Value = chunk.SourceUri;
            pTitle.Value = (object?)chunk.Title ?? DBNull.Value;
            pIdx.Value = chunk.ChunkIndex;
            pContent.Value = chunk.Content;
            pCHash.Value = chunk.ContentHash;
            pSHash.Value = chunk.SourceHash;
            pTokens.Value = (object?)chunk.TokenCount ?? DBNull.Value;
            pEmb.Value = new Vector(chunk.Embedding);
            pRun.Value = run;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> SweepAsync(Guid run, CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand("DELETE FROM documents WHERE indexing_run <> @run;");
        cmd.Parameters.AddWithValue("run", run);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(float[] embedding, int k, double minSimilarity, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, source_type, source_uri, title, content,
                   1 - (embedding <=> @q) AS similarity
            FROM documents
            ORDER BY embedding <=> @q
            LIMIT @k;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("q", new Vector(embedding));
        cmd.Parameters.AddWithValue("k", k);

        var results = new List<SearchResult>(k);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var similarity = reader.GetDouble(5);
            if (similarity < minSimilarity)
            {
                continue;
            }

            results.Add(new SearchResult
            {
                Id = reader.GetInt64(0),
                SourceType = reader.GetString(1),
                SourceUri = reader.GetString(2),
                Title = reader.IsDBNull(3) ? null : reader.GetString(3),
                Content = reader.GetString(4),
                Similarity = similarity,
            });
        }

        return results;
    }
}
