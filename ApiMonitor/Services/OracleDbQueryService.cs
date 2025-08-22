using ApiMonitor.Options;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace ApiMonitor.Services
{
    public class OracleDbQueryService : IDbQueryService
    {
        private readonly ApiMonitorOptions _opts;
        public OracleDbQueryService(IOptions<ApiMonitorOptions> opts) => _opts = opts.Value;

        public async Task<Dictionary<string, string>> RunAsync(string dataSourceKey, IEnumerable<QuerySpec> queries, CancellationToken ct)
        {
            if (!_opts.DataSources.TryGetValue(dataSourceKey, out var cfg))
                throw new InvalidOperationException($"DataSource '{dataSourceKey}' not found.");

            if (!string.Equals(cfg.Provider, "Oracle", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Provider '{cfg.Provider}' not supported by OracleDbQueryService.");

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var conn = new OracleConnection(cfg.ConnectionString);
            await conn.OpenAsync(ct);

            foreach (var q in queries)
            {
                using var cmd = new OracleCommand(q.Sql, conn);
                var scalar = await cmd.ExecuteScalarAsync(ct);
                result[q.Name] = scalar?.ToString() ?? "";
            }

            return result;
        }
    }
}
