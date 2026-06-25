using EnerkomChatbot.Core.Abstractions;
using Npgsql;

namespace EnerkomChatbot.Core.Storage;

/// <summary>Eviduje běhy indexace v tabulce <c>indexing_runs</c>.</summary>
public sealed class IndexingRunStore(NpgsqlDataSource dataSource) : IIndexingRunStore
{
    public async Task StartAsync(Guid runId, string? note, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO indexing_runs (id, status, note)
            VALUES (@id, 'running', @note);
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", runId);
        cmd.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FinishAsync(Guid runId, string status, int sources, int chunks, string? note, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE indexing_runs
            SET finished_at = now(), status = @status, sources = @sources, chunks = @chunks,
                note = COALESCE(@note, note)
            WHERE id = @id;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", runId);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("sources", sources);
        cmd.Parameters.AddWithValue("chunks", chunks);
        cmd.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
