using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Exceptions;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnerkomChatbot.Core.Rag;

/// <summary>
/// RAG pipeline: validace → embedding dotazu → retrieval → (fallback při prázdnu) → prompt → completion.
/// Transport (SSE/JSON) řeší endpoint; tato služba neví nic o HTTP. Viz docs/04-chat-api.md.
/// </summary>
public sealed class ChatService(
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    IChatClient chatClient,
    PromptBuilder promptBuilder,
    IOptions<RetrievalOptions> retrievalOptions,
    ILogger<ChatService> logger)
{
    public const int MaxQuestionLength = 2000;
    public const int MaxHistoryMessages = 6;

    private readonly RetrievalOptions _retrieval = retrievalOptions.Value;

    /// <summary>
    /// Provede validaci, embedding a retrieval. Vrátí připravené zprávy + citace, nebo fallback
    /// (Answered == false), když nic neprojde prahem podobnosti.
    /// </summary>
    public async Task<PreparedChat> PrepareAsync(ChatQuery query, CancellationToken cancellationToken = default)
    {
        var question = (query.Question ?? "").Trim();
        if (question.Length == 0)
        {
            throw new InvalidQuestionException("Dotaz je prázdný.");
        }

        if (question.Length > MaxQuestionLength)
        {
            throw new InvalidQuestionException($"Dotaz je příliš dlouhý (max {MaxQuestionLength} znaků).");
        }

        var embedding = await embeddingClient.EmbedQueryAsync(question, cancellationToken);
        var hits = await vectorStore.SearchAsync(embedding, _retrieval.TopK, _retrieval.MinSimilarity, cancellationToken);

        if (hits.Count == 0)
        {
            // Prázdný retrieval neznamená konec — LLM s anti-halucinačním promptem zvládne
            // pozdravy a běžnou konverzaci a u faktických dotazů slušně odkáže na kontakt.
            logger.LogInformation("Retrieval prázdný pro dotaz délky {Length} — odpověď bez kontextu.", question.Length);
        }

        var systemPrompt = promptBuilder.BuildSystemPrompt(hits);
        var messages = new List<ChatMessage>(capacity: query.History.Count + 2)
        {
            ChatMessage.System(systemPrompt),
        };
        messages.AddRange(TrimHistory(query.History));
        messages.Add(ChatMessage.User(question));

        return new PreparedChat
        {
            // Answered == true vždy, když odpovídá LLM; rozlišuje se jen přítomnost citací (Sources).
            Answered = true,
            Sources = PromptBuilder.BuildSources(hits),
            Messages = messages,
        };
    }

    /// <summary>Streamuje odpověď LLM (po přípravě). Hláška o limitu se řeší výjimkou výš.</summary>
    public IAsyncEnumerable<string> StreamCompletionAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
        chatClient.CompleteStreamingAsync(messages, cancellationToken);

    /// <summary>Celá odpověď najednou (pro <c>?stream=false</c>).</summary>
    public async Task<ChatAnswer> AnswerAsync(ChatQuery query, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(query, cancellationToken);
        var answer = await chatClient.CompleteAsync(prepared.Messages, cancellationToken);
        return new ChatAnswer { Answer = answer, Sources = prepared.Sources, Answered = prepared.Answered };
    }

    /// <summary>Ořízne historii na posledních <see cref="MaxHistoryMessages"/> zpráv.</summary>
    private static IEnumerable<ChatMessage> TrimHistory(IReadOnlyList<ChatMessage> history) =>
        history.Count <= MaxHistoryMessages
            ? history
            : history.Skip(history.Count - MaxHistoryMessages);
}
