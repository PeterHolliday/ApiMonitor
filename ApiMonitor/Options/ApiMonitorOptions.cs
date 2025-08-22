namespace ApiMonitor.Options
{
    public class ApiMonitorOptions
    {
        public int DefaultTimeoutSeconds { get; set; } = 10;
        
        public List<ApiEndpoint> Endpoints { get; set; } = new();

        public Dictionary<string, DbConnectionConfig> DataSources { get; set; }
            = new Dictionary<string, DbConnectionConfig>(StringComparer.OrdinalIgnoreCase);


        public Dictionary<string, AuthConfig> AuthProfiles { get; set; } 
            = new Dictionary<string, AuthConfig>(StringComparer.OrdinalIgnoreCase);

        public string? DefaultAuthRef { get; set; }

        public string DefaultEnvironment { get; set; } = "Prod";
    }

    public class DbConnectionConfig
    {
        public string Provider { get; set; } = "Oracle"; // "Oracle" | "SqlServer" (future)
        
        public string ConnectionString { get; set; } = ""; // put env/KeyVault ref here
    }

    public class ApiEndpoint
    {
        public string Name { get; set; } = "";
        
        public string Url { get; set; } = "";
        
        public string Method { get; set; } = "GET";
        
        public int IntervalSeconds { get; set; } = 300; // per-endpoint frequency

        public string? Environment { get; set; }

        public AuthConfig Auth { get; set; } = new();
        
        public string? AuthRef { get; set; }
        
        public SuccessRule Success { get; set; } = new();

        public DataBinding? Binding { get; set; } // optional

        public Dictionary<string, string>? Tags { get; set; } // optional JSON string with tags, e.g. {"env": "prod", "team": "api"}
    }

    public class AuthConfig
    {
        public string? Type { get; set; } // None|ApiKey|OAuth2

        public string? Header { get; set; }

        public string? Value { get; set; }

        public string? TokenUrl { get; set; }

        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public string? Scope { get; set; }

        public string? Resource { get; set; }
    }

    public class SuccessRule
    {
        public int[] StatusCodes { get; set; } = new[] { 200 };
        
        public string? JsonPointer { get; set; } // e.g. "/status"
        
        public string? ExpectedValue { get; set; } // e.g. "OK"
        
        public int? MaxLatencyMs { get; set; } // e.g. 2000
        
        public bool CaseInsensitiveCompare { get; set; } = true; // NEW: "ok" vs "OK"
    }

    public class DataBinding
    {
        public string? DataSource { get; set; }                 // e.g. "oracle-main"
        
        public List<QuerySpec> Queries { get; set; } = new();   // results become {tokens}
        
        public string? UrlTemplate { get; set; }                // e.g. .../orders/{LatestOrderNo}
        
        public string? BodyTemplate { get; set; }               // optional JSON with {tokens}
        
        public Dictionary<string, string>? HeaderTemplates { get; set; } // e.g. "X-Test-Id": "{LatestOrderNo}"
    }

    public class QuerySpec
    {
        public string Name { get; set; } = "";  // token name, e.g. "LatestOrderNo"
        
        public string Sql { get; set; } = "";  // e.g. SELECT MAX(order_no) FROM orders
    }

}
