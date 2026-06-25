namespace EnerkomChatbot.Core.Abstractions;

/// <summary>Evidence běhů indexace (tabulka <c>indexing_runs</c>) — monitoring.</summary>
public interface IIndexingRunStore
{
    /// <summary>Zapíše začátek běhu (status = running).</summary>
    Task StartAsync(Guid runId, string? note, CancellationToken cancellationToken = default);

    /// <summary>Uzavře běh (status success|failed + počty).</summary>
    Task FinishAsync(Guid runId, string status, int sources, int chunks, string? note, CancellationToken cancellationToken = default);
}
