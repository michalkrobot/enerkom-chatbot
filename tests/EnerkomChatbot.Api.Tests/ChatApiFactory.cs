using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace EnerkomChatbot.Api.Tests;

/// <summary>
/// WebApplicationFactory s nahrazenými Core službami (embeddings/chat/store) za mocky —
/// testuje transport (SSE/JSON, validace, CORS, rate limit) bez Azure/DB.
/// </summary>
public sealed class ChatApiFactory : WebApplicationFactory<Program>
{
    public IEmbeddingClient EmbeddingClient { get; } = Substitute.For<IEmbeddingClient>();
    public IVectorStore VectorStore { get; } = Substitute.For<IVectorStore>();
    public IChatClient ChatClient { get; } = Substitute.For<IChatClient>();

    public ChatApiFactory()
    {
        EmbeddingClient.EmbedQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[1536]);
        // Multi-query retrieval embeds the whole query list in one batch — one vector per query.
        EmbeddingClient.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<float[]>)[.. ((IReadOnlyList<string>)ci[0]).Select(_ => new float[1536])]);
    }

    public void GivenHits(params SearchResult[] hits) =>
        VectorStore.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(hits);

    public void GivenStreamedAnswer(params string[] tokens) =>
        ChatClient.CompleteStreamingAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync(tokens));

    public void GivenCompleteAnswer(string answer) =>
        ChatClient.CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Host=localhost;Database=test;Username=x;Password=y;Timeout=1",
                ["Cors:AllowedOrigins:0"] = "https://www.enerkomhp.cz",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmbeddingClient>();
            services.RemoveAll<IVectorStore>();
            services.RemoveAll<IChatClient>();

            services.AddSingleton(EmbeddingClient);
            services.AddSingleton(VectorStore);
            services.AddSingleton(ChatClient);
        });
    }

    private static async IAsyncEnumerable<string> ToAsync(string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }
}
