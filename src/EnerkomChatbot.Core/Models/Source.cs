namespace EnerkomChatbot.Core.Models;

/// <summary>Citace zdroje vracená klientovi (widget z ní udělá klikatelný odkaz).</summary>
public sealed record Source
{
    public required string Title { get; init; }
    public required string Uri { get; init; }
    public required string Type { get; init; }
}
