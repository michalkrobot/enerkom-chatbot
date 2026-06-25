using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace EnerkomChatbot.Api.HealthChecks;

/// <summary>Readiness: ověří připojení k DB jednoduchým <c>SELECT 1</c>.</summary>
public sealed class DbHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var cmd = dataSource.CreateCommand("SELECT 1;");
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Databáze není dostupná.", ex);
        }
    }
}
