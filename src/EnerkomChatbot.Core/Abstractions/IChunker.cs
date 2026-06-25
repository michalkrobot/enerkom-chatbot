using EnerkomChatbot.Core.Models;

namespace EnerkomChatbot.Core.Abstractions;

/// <summary>Rozdělí text zdroje na chunky vhodné k embeddingu.</summary>
public interface IChunker
{
    /// <summary>Rozdělí text na chunky (po odstavcích, s overlapem). Prázdný/krátký vstup → prázdný/jediný chunk.</summary>
    IReadOnlyList<Chunk> Chunk(string text);
}
