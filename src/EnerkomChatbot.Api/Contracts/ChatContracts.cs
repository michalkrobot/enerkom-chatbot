namespace EnerkomChatbot.Api.Contracts;

/// <summary>Tělo požadavku <c>POST /api/chat</c>.</summary>
public sealed record ChatRequestDto
{
    public string? Question { get; init; }
    public List<ChatMessageDto>? History { get; init; }
}

public sealed record ChatMessageDto
{
    public string? Role { get; init; }
    public string? Content { get; init; }
}

public sealed record SourceDto(string Title, string Uri, string Type);

/// <summary>JSON odpověď (<c>?stream=false</c>).</summary>
public sealed record ChatResponseDto
{
    public required string Answer { get; init; }
    public required IReadOnlyList<SourceDto> Sources { get; init; }
    public required bool Answered { get; init; }
}
