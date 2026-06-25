using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Azure.Identity;
using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnerkomChatbot.Core.Embeddings;

/// <summary>DI registrace Azure OpenAI klientů (embeddings + chat).</summary>
public static class AzureOpenAIServiceCollectionExtensions
{
    /// <summary>
    /// Zaregistruje <see cref="AzureOpenAIClient"/> + naše wrappery <see cref="IEmbeddingClient"/> a
    /// <see cref="IChatClient"/>. Autentizace API klíčem, nebo <see cref="DefaultAzureCredential"/>
    /// když klíč chybí (managed identity). Retry/backoff na 429/5xx přes pipeline Azure SDK.
    /// </summary>
    public static IServiceCollection AddAzureOpenAIClients(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
            var clientOptions = new AzureOpenAIClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(maxRetries: 5),
                NetworkTimeout = TimeSpan.FromSeconds(120),
            };
            var endpoint = new Uri(o.Endpoint);

            return string.IsNullOrWhiteSpace(o.ApiKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential(), clientOptions)
                : new AzureOpenAIClient(endpoint, new ApiKeyCredential(o.ApiKey), clientOptions);
        });

        services.AddSingleton<IEmbeddingClient, AzureOpenAIEmbeddingClient>();
        services.AddSingleton<IChatClient, AzureOpenAIChatClient>();

        return services;
    }
}
