using System.ClientModel;
using Azure.AI.OpenAI;
using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Exceptions;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Options;
using MEAI = Microsoft.Extensions.AI;

namespace EnerkomChatbot.Core.Embeddings;

/// <summary>
/// <see cref="IEmbeddingClient"/> nad Azure OpenAI (text-embedding-3-small, 1536 dim)
/// přes Microsoft.Extensions.AI. Retry/backoff na 429/5xx řeší pipeline Azure SDK
/// (viz <see cref="AzureOpenAIServiceCollectionExtensions"/>).
/// </summary>
public sealed class AzureOpenAIEmbeddingClient : IEmbeddingClient
{
    private readonly MEAI.IEmbeddingGenerator<string, MEAI.Embedding<float>> _generator;

    public AzureOpenAIEmbeddingClient(AzureOpenAIClient client, IOptions<AzureOpenAIOptions> options)
    {
        var o = options.Value;
        _generator = MEAI.OpenAIClientExtensions.AsIEmbeddingGenerator(
            client.GetEmbeddingClient(o.EmbeddingDeployment), o.EmbeddingDimensions);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        try
        {
            var embeddings = await _generator.GenerateAsync(texts, cancellationToken: cancellationToken);
            return [.. embeddings.Select(e => e.Vector.ToArray())];
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw new RateLimitedException("Azure OpenAI embeddings: překročena TPM kvóta (429).", ex);
        }
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await EmbedBatchAsync([text], cancellationToken);
        return result[0];
    }
}
