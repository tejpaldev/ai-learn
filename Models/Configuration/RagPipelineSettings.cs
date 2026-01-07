namespace RagPipeline.Models.Configuration;

public class RagPipelineSettings
{
    public OpenAISettings OpenAI { get; set; } = new();
    public AzureOpenAISettings AzureOpenAI { get; set; } = new();
    public RagSettings RAG { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public ApiSettings Api { get; set; } = new();
}

public class OpenAISettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-ada-002";
    public string ChatModel { get; set; } = "gpt-3.5-turbo";
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 30;
    public string? OrganizationId { get; set; }
}

public class AzureOpenAISettings
{
    public bool Enabled { get; set; } = false;
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingDeployment { get; set; } = string.Empty;
    public string ChatDeployment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-02-01";
}

public class RagSettings
{
    public int ChunkSize { get; set; } = 500;
    public int ChunkOverlap { get; set; } = 50;
    public int TopK { get; set; } = 3;
    public double SimilarityThreshold { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.7;
    public bool EnableReranking { get; set; } = false;
    public bool EnableHybridSearch { get; set; } = false;
    public int MaxContextLength { get; set; } = 4000;
}

public class CacheSettings
{
    public bool Enabled { get; set; } = true;
    public int EmbeddingCacheMinutes { get; set; } = 60;
    public int QueryCacheMinutes { get; set; } = 30;
    public int MaxCacheSize { get; set; } = 1000;
}

public class LoggingSettings
{
    public string Level { get; set; } = "Information";
    public bool EnableConsole { get; set; } = true;
    public bool EnableFile { get; set; } = true;
    public string LogDirectory { get; set; } = "logs";
    public bool EnableStructuredLogging { get; set; } = true;
    public bool LogQueryMetrics { get; set; } = true;
}

public class SecuritySettings
{
    public bool EnableAuthentication { get; set; } = false;
    public bool EnableRateLimiting { get; set; } = true;
    public int RateLimitPerMinute { get; set; } = 60;
    public List<string> AllowedOrigins { get; set; } = new() { "*" };
    public bool EnableApiKey { get; set; } = false;
    public string ApiKeyHeader { get; set; } = "X-API-Key";
}

public class DatabaseSettings
{
    public string Provider { get; set; } = "InMemory"; // InMemory, PostgreSQL, MongoDB
    public string ConnectionString { get; set; } = string.Empty;
    public bool EnableAutoMigration { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

public class ApiSettings
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 5000;
    public bool EnableSwagger { get; set; } = true;
    public string[] Endpoints { get; set; } = new[] { "http://localhost:5000" };
    public int MaxRequestSizeInMB { get; set; } = 10;
}
