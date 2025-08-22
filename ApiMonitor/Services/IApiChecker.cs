using ApiMonitor.Options;

namespace ApiMonitor.Services
{
    public record ApiCheckResult(string Name, bool IsSuccess, string Reason, string Details, TimeSpan Elapsed)
    {
        public static ApiCheckResult Ok(string name, TimeSpan elapsed) => new(name, true, "OK", "", elapsed);
        public static ApiCheckResult Fail(string name, string reason, string details, TimeSpan elapsed) =>
            new(name, false, reason, details, elapsed);
    }

    public interface IApiChecker
    {
        Task<ApiCheckResult> CheckAsync(ApiEndpoint ep, CancellationToken ct);
    }

}
