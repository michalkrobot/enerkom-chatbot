using EnerkomChatbot.Core.Models;

namespace EnerkomChatbot.Core.Rag;

/// <summary>Vstup do RAG pipeline.</summary>
public sealed record ChatQuery
{
    public required string Question { get; init; }
    public IReadOnlyList<ChatMessage> History { get; init; } = [];
}

/// <summary>
/// Připravený výsledek retrievalu. Při <see cref="Answered"/> == false se negeneruje přes LLM —
/// použije se <see cref="FallbackAnswer"/>.
/// </summary>
public sealed record PreparedChat
{
    public required bool Answered { get; init; }
    public required IReadOnlyList<Source> Sources { get; init; }

    /// <summary>Zprávy pro LLM (prázdné když Answered == false).</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Předpřipravená odpověď, když retrieval nic nenašel.</summary>
    public string? FallbackAnswer { get; init; }
}

/// <summary>Celá (ne-streamovaná) odpověď.</summary>
public sealed record ChatAnswer
{
    public required string Answer { get; init; }
    public required IReadOnlyList<Source> Sources { get; init; }
    public required bool Answered { get; init; }
}
