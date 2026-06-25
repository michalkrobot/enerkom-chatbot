namespace EnerkomChatbot.Core.Abstractions;

/// <summary>Spustí idempotentní <c>schema.sql</c> (extension + tabulky + indexy).</summary>
public interface IDatabaseInitializer
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
}
