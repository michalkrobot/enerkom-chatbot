namespace EnerkomChatbot.Core.Options;

/// <summary>Údaje o organizaci pro prompt a fallback (sekce <c>Org</c>).</summary>
public sealed class OrgOptions
{
    public const string SectionName = "Org";

    public string Name { get; set; } = "Enerkom HP";

    /// <summary>Odkaz na sekci Kontakty webu, kam bot odkáže, když odpověď nezná.</summary>
    public string ContactUrl { get; set; } = "https://www.enerkomhp.cz/kontakt/";
}
