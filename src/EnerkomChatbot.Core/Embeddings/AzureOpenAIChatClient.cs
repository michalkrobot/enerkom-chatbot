using System.ClientModel;
using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Exceptions;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Options;
using MEAI = Microsoft.Extensions.AI;

namespace EnerkomChatbot.Core.Embeddings;

/// <summary>
/// Náš <see cref="IChatClient"/> nad Azure OpenAI (gpt-4o-mini) přes Microsoft.Extensions.AI.
/// Streaming i celá odpověď; 429 → <see cref="RateLimitedException"/>.
/// </summary>
public sealed class AzureOpenAIChatClient : IChatClient
{
    private readonly MEAI.IChatClient _client;
    private readonly AzureOpenAIOptions _options;

    public AzureOpenAIChatClient(AzureOpenAIClient client, IOptions<AzureOpenAIOptions> options)
    {
        _options = options.Value;
        _client = MEAI.OpenAIClientExtensions.AsIChatClient(client.GetChatClient(_options.ChatDeployment));
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stream = _client.GetStreamingResponseAsync(Map(messages), BuildOptions(), cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            string? text;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                text = enumerator.Current.Text;
            }
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                throw new RateLimitedException("Azure OpenAI chat: překročena TPM kvóta (429).", ex);
            }

            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetResponseAsync(Map(messages), BuildOptions(), cancellationToken);
            return response.Text;
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw new RateLimitedException("Azure OpenAI chat: překročena TPM kvóta (429).", ex);
        }
    }

    private MEAI.ChatOptions BuildOptions() => new()
    {
        Temperature = _options.Temperature,
        MaxOutputTokens = _options.MaxOutputTokens,
    };

    private static IEnumerable<MEAI.ChatMessage> Map(IReadOnlyList<ChatMessage> messages) =>
        messages.Select(m => new MEAI.ChatMessage(MapRole(m.Role), m.Content));

    private static MEAI.ChatRole MapRole(string role) => role switch
    {
        ChatRoles.System => MEAI.ChatRole.System,
        ChatRoles.Assistant => MEAI.ChatRole.Assistant,
        _ => MEAI.ChatRole.User,
    };
}
