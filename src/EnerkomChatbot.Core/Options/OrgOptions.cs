namespace EnerkomChatbot.Core.Options;

/// <summary>Údaje o organizaci pro prompt a fallback (sekce <c>Org</c>).</summary>
public sealed class OrgOptions
{
    public const string SectionName = "Org";

    public string Name { get; set; } = "Enerkom HP";
    public string Contact { get; set; } = "info@enerkomhp.cz";
}
