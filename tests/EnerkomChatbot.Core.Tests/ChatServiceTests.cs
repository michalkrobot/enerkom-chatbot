using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Exceptions;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using EnerkomChatbot.Core.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EnerkomChatbot.Core.Tests;

public sealed class ChatServiceTests
{
    private readonly IEmbeddingClient _embeddingClient = Substitute.For<IEmbeddingClient>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();

    private ChatService CreateService(RetrievalOptions? retrieval = null)
    {
        var promptBuilder = new PromptBuilder(Microsoft.Extensions.Options.Options.Create(new OrgOptions
        {
            Name = "Enerkom HP",
            ContactUrl = "https://www.enerkomhp.cz/kontakt/",
        }));

        return new ChatService(
            _embeddingClient,
            _vectorStore,
            _chatClient,
            promptBuilder,
            Microsoft.Extensions.Options.Options.Create(retrieval ?? new RetrievalOptions()),
            NullLogger<ChatService>.Instance);
    }

    private static SearchResult Hit(string content = "Obsah.", string uri = "https://enerkomhp.cz", long id = 1) =>
        new()
        {
            Id = id,
            SourceType = "web",
            SourceUri = uri,
            Title = "Titulek",
            Content = content,
            Similarity = 0.9,
        };

    private void SetupEmbedding() =>
        _embeddingClient
            .EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<float[]>)[.. ((IReadOnlyList<string>)ci[0]).Select(_ => new float[1536])]);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n  ")]
    public async Task PrepareAsync_EmptyOrWhitespaceQuestion_ThrowsInvalidQuestionException(string question)
    {
        // Arrange
        var service = CreateService();
        var query = new ChatQuery { Question = question };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidQuestionException>(() => service.PrepareAsync(query));
    }

    [Fact]
    public async Task PrepareAsync_QuestionLongerThanMaxLength_ThrowsInvalidQuestionException()
    {
        // Arrange
        var service = CreateService();
        var query = new ChatQuery { Question = new string('a', ChatService.MaxQuestionLength + 1) };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidQuestionException>(() => service.PrepareAsync(query));
    }

    [Fact]
    public async Task PrepareAsync_NoHits_StillBuildsMessagesWithEmptyContextAndNoSources()
    {
        // Arrange
        var service = CreateService();
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SearchResult>());

        const string question = "Ahoj!";
        var query = new ChatQuery { Question = question };

        // Act
        var prepared = await service.PrepareAsync(query);

        // Assert — i bez hitů se sestaví prompt pro LLM (pozdravy musí projít), jen bez citací.
        Assert.True(prepared.Answered);
        Assert.Empty(prepared.Sources);
        Assert.Equal(ChatRoles.System, prepared.Messages[0].Role);
        Assert.Contains(PromptBuilder.EmptyContext, prepared.Messages[0].Content, StringComparison.Ordinal);

        var last = prepared.Messages[^1];
        Assert.Equal(ChatRoles.User, last.Role);
        Assert.Equal(question, last.Content);
    }

    [Fact]
    public async Task PrepareAsync_WithHits_BuildsSystemAndUserMessagesAndSources()
    {
        // Arrange
        var service = CreateService();
        SetupEmbedding();
        IReadOnlyList<SearchResult> hits = [Hit(uri: "https://a.cz", id: 1), Hit(uri: "https://b.cz", id: 2)];
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(hits);

        const string question = "Co je zateplení?";
        var query = new ChatQuery { Question = question };

        // Act
        var prepared = await service.PrepareAsync(query);

        // Assert
        Assert.True(prepared.Answered);
        Assert.Equal(hits.Count, prepared.Sources.Count);

        Assert.Equal(ChatRoles.System, prepared.Messages[0].Role);

        var last = prepared.Messages[^1];
        Assert.Equal(ChatRoles.User, last.Role);
        Assert.Equal(question, last.Content);
    }

    [Fact]
    public async Task PrepareAsync_HistoryLongerThanMax_TrimsToLastMaxMessages()
    {
        // Arrange
        var service = CreateService();
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit()]);

        // More messages than the limit, tagged with their index so we can identify which survived.
        var total = ChatService.MaxHistoryMessages + 4;
        var history = Enumerable
            .Range(0, total)
            .Select(i => ChatMessage.User($"hist-{i}"))
            .ToArray();

        var query = new ChatQuery { Question = "Aktuální dotaz?", History = history };

        // Act
        var prepared = await service.PrepareAsync(query);

        // Assert — messages = system + trimmed history + user question (posledních MaxHistoryMessages).
        var historyInMessages = prepared.Messages
            .Where(m => m.Content.StartsWith("hist-", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(ChatService.MaxHistoryMessages, historyInMessages.Count);
        Assert.Equal($"hist-{total - ChatService.MaxHistoryMessages}", historyInMessages[0].Content);
        Assert.Equal($"hist-{total - 1}", historyInMessages[^1].Content);
    }

    [Fact]
    public async Task PrepareAsync_HistoryWithinMax_KeepsAllHistory()
    {
        // Arrange
        var service = CreateService();
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit()]);

        var history = new[] { ChatMessage.User("hist-0"), ChatMessage.Assistant("hist-1") };
        var query = new ChatQuery { Question = "Dotaz?", History = history };

        // Act
        var prepared = await service.PrepareAsync(query);

        // Assert
        var historyInMessages = prepared.Messages
            .Where(m => m.Content.StartsWith("hist-", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, historyInMessages.Count);
    }

    [Fact]
    public async Task PrepareAsync_MultiQuery_EmbedsAllGeneratedQueriesAndMergesHits()
    {
        // Arrange — model rozšíří dotaz na víc formulací (každá na řádku).
        var service = CreateService();
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit()]);
        _chatClient
            .CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("kolik stojí sdílení elektřiny EDC?\ncena EDC\npoplatky za sdílení elektřiny");

        var history = new[]
        {
            ChatMessage.User("Co je EDC?"),
            ChatMessage.Assistant("EDC je sdílení elektřiny."),
        };
        var query = new ChatQuery { Question = "a kolik to stojí?", History = history };

        // Act
        var prepared = await service.PrepareAsync(query);

        // Assert — embedne se dávka všech vygenerovaných dotazů; zpráva pro LLM nese původní znění.
        await _embeddingClient.Received(1).EmbedBatchAsync(
            Arg.Is<IReadOnlyList<string>>(q =>
                q.Count == 3 &&
                q.Contains("kolik stojí sdílení elektřiny EDC?") &&
                q.Contains("cena EDC")),
            Arg.Any<CancellationToken>());
        Assert.Equal("a kolik to stojí?", prepared.Messages[^1].Content);
    }

    [Fact]
    public async Task PrepareAsync_MultiQuery_CapsQueriesAtMaxQueries()
    {
        // Arrange — model vrátí víc řádků než MaxQueries; ořízne se.
        var service = CreateService(new RetrievalOptions { MaxQueries = 2 });
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit()]);
        _chatClient
            .CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("dotaz jedna\ndotaz dva\ndotaz tři\ndotaz čtyři");

        var query = new ChatQuery { Question = "cokoli" };

        // Act
        await service.PrepareAsync(query);

        // Assert
        await _embeddingClient.Received(1).EmbedBatchAsync(
            Arg.Is<IReadOnlyList<string>>(q => q.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrepareAsync_ExpansionReturnsEmpty_FallsBackToRawQuestion()
    {
        // Arrange — model vrátí prázdno; retrieval musí degradovat na původní otázku, ne spadnout.
        var service = CreateService();
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit()]);
        _chatClient
            .CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("   ");

        var query = new ChatQuery { Question = "a co dál?" };

        // Act
        await service.PrepareAsync(query);

        // Assert
        await _embeddingClient.Received(1).EmbedBatchAsync(
            Arg.Is<IReadOnlyList<string>>(q => q.Count == 1 && q[0] == "a co dál?"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrepareAsync_MultiQueryDisabled_EmbedsRawQuestionWithoutCallingChatClient()
    {
        // Arrange
        var service = CreateService(new RetrievalOptions { MultiQuery = false });
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit()]);

        var query = new ChatQuery
        {
            Question = "a kolik to stojí?",
            History = [ChatMessage.User("Co je EDC?")],
        };

        // Act
        await service.PrepareAsync(query);

        // Assert — přepínač vypnutý → žádné volání chat modelu, embedne se jen holá otázka.
        await _chatClient.DidNotReceive().CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>());
        await _embeddingClient.Received(1).EmbedBatchAsync(
            Arg.Is<IReadOnlyList<string>>(q => q.Count == 1 && q[0] == "a kolik to stojí?"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrepareAsync_MergesDuplicateHitsKeepingHighestSimilarity()
    {
        // Arrange — dvě formulace vrátí týž chunk (Id=1) s různou podobností → jeden zdroj.
        var service = CreateService(new RetrievalOptions { MaxQueries = 2 });
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit(id: 1), Hit(uri: "https://b.cz", id: 2)]);
        _chatClient
            .CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("dotaz jedna\ndotaz dva");

        var query = new ChatQuery { Question = "cokoli" };

        // Act
        var prepared = await service.PrepareAsync(query);

        // Assert — Id 1 a 2 se objeví jen jednou, i když je vrátila obě hledání.
        Assert.Equal(2, prepared.Sources.Count);
    }

    [Fact]
    public async Task AnswerAsync_WithHits_CallsChatClientAndReturnsItsText()
    {
        // Arrange — MultiQuery vypnuté, ať se počítá jen finální volání odpovědi.
        var service = CreateService(new RetrievalOptions { MultiQuery = false });
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([Hit()]);

        const string llmAnswer = "Zateplení snižuje náklady na vytápění. [1]";
        _chatClient
            .CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(llmAnswer);

        var query = new ChatQuery { Question = "Co je zateplení?" };

        // Act
        var answer = await service.AnswerAsync(query);

        // Assert
        Assert.True(answer.Answered);
        Assert.Equal(llmAnswer, answer.Answer);
        Assert.Single(answer.Sources);
        await _chatClient.Received(1).CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnswerAsync_NoHits_StillCallsChatClientWithoutSources()
    {
        // Arrange — MultiQuery vypnuté, ať se počítá jen finální volání odpovědi.
        var service = CreateService(new RetrievalOptions { MultiQuery = false });
        SetupEmbedding();
        _vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SearchResult>());

        const string llmAnswer = "Dobrý den, rád pomůžu. 🙂";
        _chatClient
            .CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(llmAnswer);

        var query = new ChatQuery { Question = "Ahoj" };

        // Act
        var answer = await service.AnswerAsync(query);

        // Assert — pozdrav obslouží LLM, jen bez citací.
        Assert.True(answer.Answered);
        Assert.Equal(llmAnswer, answer.Answer);
        Assert.Empty(answer.Sources);
        await _chatClient.Received(1).CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>());
    }
}
