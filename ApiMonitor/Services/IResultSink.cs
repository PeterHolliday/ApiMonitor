using ApiMonitor.Options;

namespace ApiMonitor.Services
{
    public interface IResultSink
    {
        Task RecordAsync(ApiEndpoint ep, ApiCheckResult result, int? httpStatus, CancellationToken ct);
    }
}
