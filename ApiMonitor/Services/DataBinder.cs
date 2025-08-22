using ApiMonitor.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ApiMonitor.Services
{
    public class DataBinder : IDataBinder
    {
        private static readonly Regex TokenRx = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        private readonly IDbQueryService _db;

        public DataBinder(IDbQueryService db) => _db = db;

        public async Task<(string url, HttpContent? body, Action<HttpRequestHeaders>? headers)>
            BindAsync(ApiEndpoint ep, CancellationToken ct)
        {
            if (ep.Binding is null)
                return (ep.Url, null, null);

            var tokens = ep.Binding.Queries.Count == 0 || string.IsNullOrWhiteSpace(ep.Binding.DataSource)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : await _db.RunAsync(ep.Binding.DataSource!, ep.Binding.Queries, ct);

            string Resolve(string? template, string fallback) =>
                string.IsNullOrWhiteSpace(template) ? fallback : TokenRx.Replace(template!, m =>
                {
                    var key = m.Groups[1].Value;
                    return tokens.TryGetValue(key, out var v) ? Uri.EscapeDataString(v ?? "") : "";
                });

            var url = Resolve(ep.Binding.UrlTemplate, ep.Url);

            HttpContent? content = null;
            if (!string.IsNullOrWhiteSpace(ep.Binding.BodyTemplate))
            {
                var bodyText = TokenRx.Replace(ep.Binding.BodyTemplate!, m =>
                {
                    var key = m.Groups[1].Value;
                    return tokens.TryGetValue(key, out var v) ? v ?? "" : "";
                });
                content = new StringContent(bodyText, Encoding.UTF8, "application/json");
            }

            Action<HttpRequestHeaders>? hdrSetter = null;
            if (ep.Binding.HeaderTemplates is { Count: > 0 })
            {
                hdrSetter = headers =>
                {
                    foreach (var kv in ep.Binding.HeaderTemplates!)
                    {
                        var val = TokenRx.Replace(kv.Value, m =>
                        {
                            var key = m.Groups[1].Value;
                            return tokens.TryGetValue(key, out var v) ? v ?? "" : "";
                        });
                        headers.TryAddWithoutValidation(kv.Key, val);
                    }
                };
            }

            return (url, content, hdrSetter);
        }
    }
}
