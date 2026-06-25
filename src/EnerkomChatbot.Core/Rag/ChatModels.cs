using EnerkomChatbot.Core.Models;

namespace EnerkomChatbot.Core.Rag;

/// <summary>Vstup do RAG pipeline.</summary>
public sealed record ChatQuery
{
    public required string Question { get; init; }
    public IReadOnlyList<ChatMessage> History { get; init; } = [];
}

/// <summary>
/// Připravený výsledek retrievalu. Odpověď vždy generuje LLM (i pro pozdravy bez kontextu);
/// <see cref="Answered"/> je true vždy, když odpovídá model. Citace se odvozují od <see cref="Sources"/>.
/// </summary>
public sealed record PreparedChat
{
    public required bool Answered { get; init; }
    public required IReadOnlyList<Source> Sources { get; init; }

    /// <summary>Zprávy pro LLM (systémový prompt + historie + dotaz).</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
}

/// <summary>Celá (ne-streamovaná) odpověď.</summary>
public sealed record ChatAnswer
{
    public required string Answer { get; init; }
    public required IReadOnlyList<Source> Sources { get; init; }
    public required bool Answered { get; init; }
}
