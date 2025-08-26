using Azure.Security.KeyVault.Secrets;
using System.Collections.Concurrent;

namespace APIMonitor.Dashboard.Services
{
    public interface ISecretResolver
    {
        Task<string?> ResolveAsync(string? value, CancellationToken ct);
    }

    public class KeyVaultSecretResolver : ISecretResolver
    {
        private readonly SecretClient? _kv;
        private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

        public KeyVaultSecretResolver(SecretClient? kv = null) => _kv = kv;

        public async Task<string?> ResolveAsync(string? value, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            if (value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                return Environment.GetEnvironmentVariable(value[4..]);

            if (value.StartsWith("kv:", StringComparison.OrdinalIgnoreCase))
            {
                if (_kv is null) return null;
                var id = value[3..];
                if (_cache.TryGetValue(id, out var cached)) return cached;

                var parts = id.Split('#', 2);
                var name = parts[0];
                var version = parts.Length == 2 ? parts[1] : null;

                var resp = version is null
                    ? await _kv.GetSecretAsync(name, cancellationToken: ct)
                    : await _kv.GetSecretAsync(name, version, cancellationToken: ct);

                var secretVal = resp.Value.Value;
                _cache[id] = secretVal;
                return secretVal;
            }

            return value; // literal
        }
    }
}
