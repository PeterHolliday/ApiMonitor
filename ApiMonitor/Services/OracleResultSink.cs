using ApiMonitor.Options;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApiMonitor.Services
{
    public class OracleResultSink : IResultSink
    {
        private readonly ApiMonitorOptions _opts;
        public OracleResultSink(IOptions<ApiMonitorOptions> opts) => _opts = opts.Value;

        public async Task RecordAsync(ApiEndpoint ep, ApiCheckResult result, int? httpStatus, CancellationToken ct)
        {
            var cfg = _opts.DataSources["oracle-main"]; // or a dedicated "telemetry" DSN
            await using var conn = new OracleConnection(cfg.ConnectionString);
            await conn.OpenAsync(ct);

            const string sql = @"
          INSERT INTO api_check_event
            (endpoint_name, environment, url, http_method, checked_at_utc,
             is_success, http_status, reason, details, latency_ms, json_pointer, expected_value)
          VALUES
            (:name, :env, :url, :method, SYSTIMESTAMP,
             :ok, :status, :reason, :details, :latency, :ptr, :exp)";

            await using var cmd = new OracleCommand(sql, conn);
            cmd.BindByName = true;
            cmd.Parameters.Add(":name", ep.Name);
            cmd.Parameters.Add(":env", (object?)(ep.Environment ?? _opts.DefaultEnvironment) ?? DBNull.Value);
            cmd.Parameters.Add(":url", ep.Binding?.UrlTemplate ?? ep.Url);
            cmd.Parameters.Add(":method", ep.Method);
            cmd.Parameters.Add(":ok", result.IsSuccess ? 1 : 0);
            cmd.Parameters.Add(":status", (object?)httpStatus ?? DBNull.Value);
            cmd.Parameters.Add(":reason", result.Reason);
            cmd.Parameters.Add(":details", (object?)result.Details ?? DBNull.Value);
            cmd.Parameters.Add(":latency", (int)result.Elapsed.TotalMilliseconds);
            cmd.Parameters.Add(":ptr", (object?)ep.Success.JsonPointer ?? DBNull.Value);
            cmd.Parameters.Add(":exp", (object?)ep.Success.ExpectedValue ?? DBNull.Value);
            
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
