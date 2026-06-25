namespace EnerkomChatbot.Core.Models;

/// <summary>Jedna zpráva konverzace (role + obsah).</summary>
public sealed record ChatMessage
{
    /// <summary>Role: viz <see cref="ChatRoles"/>.</summary>
    public required string Role { get; init; }

    public required string Content { get; init; }

    public static ChatMessage User(string content) => new() { Role = ChatRoles.User, Content = content };
    public static ChatMessage Assistant(string content) => new() { Role = ChatRoles.Assistant, Content = content };
    public static ChatMessage System(string content) => new() { Role = ChatRoles.System, Content = content };
}

/// <summary>Povolené hodnoty role zprávy.</summary>
public static class ChatRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
}
