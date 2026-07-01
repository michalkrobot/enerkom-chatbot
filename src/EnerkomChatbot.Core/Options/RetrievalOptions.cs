namespace EnerkomChatbot.Core.Options;

/// <summary>Konfigurace retrievalu (sekce <c>Retrieval</c>).</summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    public int TopK { get; set; } = 5;
    public double MinSimilarity { get; set; } = 0.5;

    /// <summary>
    /// Před embeddingem nechá model vygenerovat víc formulací dotazu (přepis, synonyma, doplnění
    /// kontextu z historie, oprava překlepů); všechny se dávkově embednou, vyhledají a výsledky se
    /// sloučí. Výrazně zlepšuje retrieval u stručných, vágních a navazujících dotazů. Jedno volání
    /// chat modelu navíc na dotaz — lze vypnout pro úsporu volání/TPM.
    /// </summary>
    public bool MultiQuery { get; set; } = true;

    /// <summary>Horní mez počtu vyhledávacích dotazů generovaných při <see cref="MultiQuery"/>.</summary>
    public int MaxQueries { get; set; } = 5;
}
