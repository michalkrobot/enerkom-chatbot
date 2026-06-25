namespace EnerkomChatbot.Api.Options;

/// <summary>Povolené originy widgetu (sekce <c>Cors</c>). Žádné "*".</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
