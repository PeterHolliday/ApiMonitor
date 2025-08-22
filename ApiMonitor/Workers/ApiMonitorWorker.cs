using ApiMonitor.Notifications;
using ApiMonitor.Options;
using ApiMonitor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace ApiMonitor.Workers
{
    public class ApiMonitorWorker : BackgroundService
    {
        private readonly ApiMonitorOptions _opts;
        private readonly IApiChecker _checker;
        private readonly INotifier _notifier;
        private readonly ILogger _log = Log.ForContext<ApiMonitorWorker>();
        private readonly IResultSink _resultSink;

        public ApiMonitorWorker(IOptions<ApiMonitorOptions> opts, IApiChecker checker, INotifier notifier, IResultSink resultSink)
        {
            _opts = opts.Value;
            _checker = checker;
            _notifier = notifier;
            _resultSink = resultSink;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            var tasks = _opts.Endpoints.Select(ep => RunEndpointLoop(ep, ct));
            var count = _opts.Endpoints?.Count ?? 0;
            _log.Information("API monitor starting. Loaded {Count} endpoint(s).", count);
                if (count == 0)
                    {
                _log.Warning("No endpoints configured under 'ApiMonitor:Endpoints'. Nothing to do.");
                        // keep the worker alive so you can update config without the process exiting
                await Task.Delay(Timeout.Infinite, ct);
                        return;
                    }
            
            await Task.WhenAll(tasks);
        }

        private async Task RunEndpointLoop(ApiEndpoint ep, CancellationToken ct)
        {
            var delay = TimeSpan.FromSeconds(Math.Max(5, ep.IntervalSeconds));
            var rnd = new Random();
            
            _log.Information("Starting monitor for '{Name}' every {Interval}s (method: {Method}, url: {Url})",
            ep.Name, ep.IntervalSeconds, ep.Method, ep.Binding?.UrlTemplate ?? ep.Url);
            
            while (!ct.IsCancellationRequested)
            {
                var started = DateTimeOffset.UtcNow;
                ApiCheckResult result;

                try
                {
                    result = await _checker.CheckAsync(ep, ct);
                }
                catch (Exception ex)
                {
                    result = ApiCheckResult.Fail(ep.Name, "Exception", ex.Message, TimeSpan.Zero);
                }

                if (!result.IsSuccess)
                {
                    _log.Warning("API check failed: {Name} Reason={Reason} Details={Details}", ep.Name, result.Reason, result.Details);
                    await _notifier.NotifyFailureAsync(result, ct);
                }
                else
                {
                    _log.Information("API OK: {Name} in {Elapsed}ms", ep.Name, (int)result.Elapsed.TotalMilliseconds);
                }

                var httpStatus = result.IsSuccess ? 200 : (int?)null;
                if (result.Reason == "BadStatus" && result is { Details: var d } && int.TryParse(d.AsSpan(4, 3), out var parsed))
                    httpStatus = parsed;

                await _resultSink.RecordAsync(ep, result, httpStatus, ct);

                // jitter to avoid thundering herd
                var jitterMs = rnd.Next(250, 1000);
                var elapsed = DateTimeOffset.UtcNow - started;
                var wait = delay - elapsed + TimeSpan.FromMilliseconds(jitterMs);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
        }
    }
}
