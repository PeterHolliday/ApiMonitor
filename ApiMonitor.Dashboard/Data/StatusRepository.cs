using APIMonitor.Dashboard.Models;
using APIMonitor.Dashboard.Services;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace APIMonitor.Dashboard.Data
{
    public interface IStatusRepository
    {
        Task<IReadOnlyList<EndpointStatusDto>> GetLatestAsync(CancellationToken ct);
        Task<IReadOnlyList<string>> GetEnvironmentsAsync(CancellationToken ct);
        Task<IReadOnlyList<TrendPointDto>> GetTrendAsync(string endpointName, string environment, TimeSpan lookback, CancellationToken ct);
    }

    public sealed class StatusRepository : IStatusRepository
    {
        private readonly IConfiguration _cfg;
        private readonly ISecretResolver _secrets;
        private string? _resolvedConn;

        public StatusRepository(IConfiguration cfg, ISecretResolver secrets)
        {
            _cfg = cfg;
            _secrets = secrets;
        }

        private async Task<IDbConnection> OpenAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_resolvedConn))
            {
                var raw = _cfg.GetConnectionString("MonitorRO");
                _resolvedConn = await _secrets.ResolveAsync(raw, ct)
                               ?? throw new InvalidOperationException("ConnectionStrings:MonitorRO not resolved.");
            }
            var conn = new OracleConnection(_resolvedConn);
            await conn.OpenAsync(ct);
            return conn;
        }

        public async Task<IReadOnlyList<EndpointStatusDto>> GetLatestAsync(CancellationToken ct)
        {
            const string sql = @"
            SELECT endpoint_name   AS EndpointName,
                   environment     AS Environment,
                   url             AS Url,
                   http_method     AS HttpMethod,
                   CAST(checked_at_utc AT TIME ZONE 'UTC' AS DATE) AS CheckedAtUtc,
                   CASE WHEN is_success = 1 THEN 1 ELSE 0 END    AS IsSuccess,
                   http_status     AS HttpStatus,
                   reason          AS Reason,
                   latency_ms      AS LatencyMs,
                   details         AS Details
            FROM api_endpoint_latest
            ORDER BY environment, endpoint_name";

            using var conn = await OpenAsync(ct);
            var rows = await conn.QueryAsync<EndpointStatusDto>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.ToList();
        }

        public async Task<IReadOnlyList<string>> GetEnvironmentsAsync(CancellationToken ct)
        {
            const string sql = @"SELECT DISTINCT environment FROM api_endpoint_latest ORDER BY environment";
            using var conn = await OpenAsync(ct);
            var rows = await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.ToList();
        }

        public async Task<IReadOnlyList<TrendPointDto>> GetTrendAsync(string endpointName, string environment, TimeSpan lookback, CancellationToken ct)
        {
            const string sql = @"
            SELECT CAST(checked_at_utc AT TIME ZONE 'UTC' AS DATE) AS CheckedAtUtc,
                   latency_ms AS LatencyMs,
                   CASE WHEN is_success = 1 THEN 1 ELSE 0 END AS IsSuccess
            FROM api_check_event
            WHERE endpoint_name = :name
              AND environment   = :env
              AND checked_at_utc >= SYSTIMESTAMP - :hours * INTERVAL '1' HOUR
            ORDER BY checked_at_utc";

            var p = new DynamicParameters();
            p.Add(":name", endpointName);
            p.Add(":env", environment);
            p.Add(":hours", lookback.TotalHours);

            using var conn = await OpenAsync(ct);
            var rows = await conn.QueryAsync<TrendPointDto>(new CommandDefinition(sql, p, cancellationToken: ct));
            return rows.ToList();
        }
    }
}
