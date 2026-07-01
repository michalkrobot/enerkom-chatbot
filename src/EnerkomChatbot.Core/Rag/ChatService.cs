using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Exceptions;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnerkomChatbot.Core.Rag;

/// <summary>
/// RAG pipeline: validace → rozšíření dotazu → embedding → retrieval + sloučení → (fallback při prázdnu)
/// → prompt → completion. Transport (SSE/JSON) řeší endpoint; tato služba neví nic o HTTP. Viz docs/04-chat-api.md.
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
    public const int MaxHistoryMessages = 10;

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

        // Retrieval hledá podle víc formulací dotazu (přepis + synonyma + doplněný kontext), aby si
        // poradil se stručnými, vágními a navazujícími dotazy. Do zpráv pro LLM jde původní znění otázky.
        var searchQueries = await BuildSearchQueriesAsync(question, query.History, cancellationToken);
        var hits = await RetrieveAsync(searchQueries, cancellationToken);

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

    /// <summary>
    /// Vrátí vyhledávací dotazy pro retrieval. Když je <see cref="RetrievalOptions.MultiQuery"/> vypnuté,
    /// je to jen původní otázka; jinak model vygeneruje víc formulací (přepis, synonyma, doplněný kontext
    /// z historie, oprava překlepů). Selhání generování retrieval neshodí — degraduje na původní otázku.
    /// Limit (429) a zrušení se propagují dál.
    /// </summary>
    private async Task<IReadOnlyList<string>> BuildSearchQueriesAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        if (!_retrieval.MultiQuery || _retrieval.MaxQueries <= 1)
        {
            return [question];
        }

        try
        {
            var messages = PromptBuilder.BuildExpansionMessages([.. TrimHistory(history)], question, _retrieval.MaxQueries);
            var raw = await chatClient.CompleteAsync(messages, cancellationToken);

            var queries = (raw ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(q => q.Trim('"', '-', '•', ' ', '\t'))
                .Where(q => q.Length is > 0 and <= MaxQuestionLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(_retrieval.MaxQueries)
                .ToArray();

            if (queries.Length == 0)
            {
                return [question];
            }

            logger.LogDebug("Dotaz rozšířen na {Count} vyhledávacích dotazů.", queries.Length);
            return queries;
        }
        catch (RateLimitedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rozšíření dotazu selhalo — použije se původní otázka.");
            return [question];
        }
    }

    /// <summary>
    /// Embedne všechny dotazy jednou dávkou, vyhledá pro každý a sloučí výsledky podle Id chunku
    /// (u duplicit ponechá vyšší podobnost). Vrátí top-k seřazené podle podobnosti.
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> RetrieveAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken)
    {
        var embeddings = await embeddingClient.EmbedBatchAsync(queries, cancellationToken);

        var best = new Dictionary<long, SearchResult>();
        foreach (var embedding in embeddings)
        {
            var hits = await vectorStore.SearchAsync(embedding, _retrieval.TopK, _retrieval.MinSimilarity, cancellationToken);
            foreach (var hit in hits)
            {
                if (!best.TryGetValue(hit.Id, out var existing) || hit.Similarity > existing.Similarity)
                {
                    best[hit.Id] = hit;
                }
            }
        }

        return [.. best.Values.OrderByDescending(h => h.Similarity).Take(_retrieval.TopK)];
    }

    /// <summary>Ořízne historii na posledních <see cref="MaxHistoryMessages"/> zpráv.</summary>
    private static IEnumerable<ChatMessage> TrimHistory(IReadOnlyList<ChatMessage> history) =>
        history.Count <= MaxHistoryMessages
            ? history
            : history.Skip(history.Count - MaxHistoryMessages);
}
