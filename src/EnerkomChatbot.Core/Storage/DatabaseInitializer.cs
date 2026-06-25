using System.Reflection;
using EnerkomChatbot.Core.Abstractions;
using Npgsql;

namespace EnerkomChatbot.Core.Storage;

/// <summary>Spustí idempotentní <c>schema.sql</c> (embedded resource) na startu indexace.</summary>
public sealed class DatabaseInitializer(NpgsqlDataSource dataSource) : IDatabaseInitializer
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        // 1) vector extension. Na Azure Flexible Serveru ji smí vytvořit jen člen azure_pg_admin;
        //    aplikační role dostane 42501 i s IF NOT EXISTS, i když už existuje (předvytvořil admin
        //    v deploy/db-setup.sql). Na superuseru (lokál/Testcontainers) projde. Chybu oprávnění
        //    tolerujeme — znamená, že extension už existuje.
        try
        {
            await using var ext = dataSource.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector;");
            await ext.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            // extension už existuje (vytvořil admin) — pokračujeme tabulkami
        }

        // 2) tabulky + indexy
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
