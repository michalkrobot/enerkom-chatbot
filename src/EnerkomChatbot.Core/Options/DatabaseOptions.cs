namespace EnerkomChatbot.Core.Options;

/// <summary>Konfigurace databáze (sekce <c>Database</c>).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "";
}
