using EnerkomChatbot.Core.Models;

namespace EnerkomChatbot.Core.Abstractions;

/// <summary>
/// Tenký wrapper nad chat completion modelem (Azure OpenAI gpt-4o-mini).
/// Pozn.: záměrně se liší od <c>Microsoft.Extensions.AI.IChatClient</c> — zde je doménové rozhraní
/// pracující s naším <see cref="ChatMessage"/>, aby šlo providera vyměnit.
/// </summary>
public interface IChatClient
{
    /// <summary>Streamuje odpověď po částech (tokenech).</summary>
    IAsyncEnumerable<string> CompleteStreamingAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Vrátí celou odpověď najednou (pro <c>?stream=false</c>).</summary>
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
}
