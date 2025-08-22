using ApiMonitor.Options;

namespace ApiMonitor.Services
{
    public interface IDbQueryService
    {
        Task<Dictionary<string, string>> RunAsync(string dataSourceKey, IEnumerable<QuerySpec> queries, CancellationToken ct);
    }

}
