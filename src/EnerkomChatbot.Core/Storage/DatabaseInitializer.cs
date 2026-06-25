using System.Reflection;
using EnerkomChatbot.Core.Abstractions;
using Npgsql;

namespace EnerkomChatbot.Core.Storage;

/// <summary>Spustí idempotentní <c>schema.sql</c> (embedded resource) na startu indexace.</summary>
public sealed class DatabaseInitializer(NpgsqlDataSource dataSource) : IDatabaseInitializer
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var script = LoadSchemaScript();
        await using var cmd = dataSource.CreateCommand(script);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string LoadSchemaScript()
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith("Storage.schema.sql", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded resource schema.sql nenalezen.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Nelze otevřít resource {resourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
