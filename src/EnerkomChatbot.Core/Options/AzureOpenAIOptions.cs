namespace EnerkomChatbot.Core.Options;

/// <summary>Konfigurace Azure OpenAI (sekce <c>AzureOpenAI</c>).</summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>https://&lt;resource&gt;.openai.azure.com/</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>API klíč; když je prázdný, použije se DefaultAzureCredential (managed identity).</summary>
    public string? ApiKey { get; set; }

    public string ChatDeployment { get; set; } = "gpt-4o-mini";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
    public string ApiVersion { get; set; } = "2024-10-21";
    public float Temperature { get; set; } = 0.2f;
    public int MaxOutputTokens { get; set; } = 800;
}
