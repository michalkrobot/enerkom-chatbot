using EnerkomChatbot.Core.Storage;
using Npgsql;
using Pgvector;
using Testcontainers.PostgreSql;
using Xunit;

namespace EnerkomChatbot.Core.Tests;

/// <summary>
/// Starts a pgvector-enabled PostgreSQL container once per test class and runs schema.sql.
/// If Docker is unavailable (container fails to start), <see cref="SkipReason"/> is set and
/// integration tests skip cleanly instead of failing the suite.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public NpgsqlDataSource? DataSource { get; private set; }

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            // pgvector/pgvector ships the `vector` extension required by schema.sql.
            _container = new PostgreSqlBuilder()
                .WithImage("pgvector/pgvector:pg16")
                .Build();

            await _container.StartAsync();

            var connectionString = _container.GetConnectionString();

            // The `vector` type only exists once the extension is created, and Npgsql caches the
            // type catalog when a data source first connects. So: (1) create the extension + schema
            // with a bootstrap data source, then (2) build the real, vector-mapped data source.
            await using (var bootstrap = new NpgsqlDataSourceBuilder(connectionString).Build())
            {
                var initializer = new DatabaseInitializer(bootstrap);
                await initializer.EnsureSchemaAsync();
            }

            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            DataSource = builder.Build();
        }
        catch (Exception ex)
        {
            // Docker not available / image pull failed / startup error → skip, don't fail.
            SkipReason = $"Docker/PostgreSQL container unavailable: {ex.GetType().Name}: {ex.Message}";

            if (DataSource is not null)
            {
                await DataSource.DisposeAsync();
                DataSource = null;
            }

            if (_container is not null)
            {
                try
                {
                    await _container.DisposeAsync();
                }
                catch
                {
                    // ignore cleanup failures during a failed startup
                }

                _container = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
