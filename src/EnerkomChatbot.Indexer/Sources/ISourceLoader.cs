using EnerkomChatbot.Core.Models;

namespace EnerkomChatbot.Indexer.Sources;

/// <summary>Zdroj surových dokumentů pro indexaci (web nebo lokální dokumenty).</summary>
public interface ISourceLoader
{
    /// <summary>Identifikátor zdroje pro logy / výběr přes argumenty ("web" | "docs").</summary>
    string Name { get; }

    /// <summary>Líně načítá zdroje. Chyba jednoho zdroje nesmí shodit celý běh.</summary>
    IAsyncEnumerable<RawSource> LoadAsync(CancellationToken cancellationToken);
}
