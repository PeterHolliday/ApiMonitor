namespace APIMonitor.Dashboard.Models
{
    public sealed class EndpointStatusDto
    {
        public string EndpointName { get; init; } = "";
        public string Environment { get; init; } = "";
        public string Url { get; init; } = "";
        public string HttpMethod { get; init; } = "";
        public DateTime CheckedAtUtc { get; init; }
        public bool IsSuccess { get; init; }
        public int? HttpStatus { get; init; }
        public string? Reason { get; init; }
        public int? LatencyMs { get; init; }
        public string? Details { get; init; }
    }

    public sealed class TrendPointDto
    {
        public DateTime CheckedAtUtc { get; init; }
        public int? LatencyMs { get; init; }
        public bool IsSuccess { get; init; }
    }

}
