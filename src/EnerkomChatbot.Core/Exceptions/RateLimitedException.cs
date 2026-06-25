namespace EnerkomChatbot.Core.Exceptions;

/// <summary>
/// Vyhazuje se, když Azure OpenAI vrátí HTTP 429 (překročení TPM kvóty) i po vyčerpání retry.
/// API ji mapuje na 429 s přívětivou hláškou; indexer na ní běh ukončí.
/// </summary>
public sealed class RateLimitedException : Exception
{
    public RateLimitedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
