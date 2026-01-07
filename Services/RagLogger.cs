using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace RagPipeline.Services;

public interface IRagLogger
{
    void LogQuery(string query, int retrievedChunks, double processingTimeMs);
    void LogIndexing(string source, int chunks, double processingTimeMs);
    void LogError(string operation, Exception exception, Dictionary<string, object>? context = null);
    void LogInfo(string message, Dictionary<string, object>? context = null);
    void LogWarning(string message, Dictionary<string, object>? context = null);
    Task<LogMetrics> GetMetricsAsync();
    Task ExportMetricsAsync(string filePath);
}

public class RagLogger : IRagLogger
{
    private readonly ILogger<RagLogger> _logger;
    private readonly ConcurrentBag<QueryLog> _queryLogs = new();
    private readonly ConcurrentBag<IndexingLog> _indexingLogs = new();
    private readonly ConcurrentBag<ErrorLog> _errorLogs = new();
    private readonly bool _enableMetrics;

    public RagLogger(ILogger<RagLogger> logger, bool enableMetrics = true)
    {
        _logger = logger;
        _enableMetrics = enableMetrics;
    }

    public void LogQuery(string query, int retrievedChunks, double processingTimeMs)
    {
        if (_enableMetrics)
        {
            _queryLogs.Add(new QueryLog
            {
                Query = query,
                RetrievedChunks = retrievedChunks,
                ProcessingTimeMs = processingTimeMs,
                Timestamp = DateTime.UtcNow
            });
        }

        _logger.LogInformation(
            "Query processed: {Query} | Chunks: {Chunks} | Time: {Time}ms",
            TruncateString(query, 100),
            retrievedChunks,
            processingTimeMs
        );
    }

    public void LogIndexing(string source, int chunks, double processingTimeMs)
    {
        if (_enableMetrics)
        {
            _indexingLogs.Add(new IndexingLog
            {
                Source = source,
                Chunks = chunks,
                ProcessingTimeMs = processingTimeMs,
                Timestamp = DateTime.UtcNow
            });
        }

        _logger.LogInformation(
            "Document indexed: {Source} | Chunks: {Chunks} | Time: {Time}ms",
            source,
            chunks,
            processingTimeMs
        );
    }

    public void LogError(string operation, Exception exception, Dictionary<string, object>? context = null)
    {
        if (_enableMetrics)
        {
            _errorLogs.Add(new ErrorLog
            {
                Operation = operation,
                Message = exception.Message,
                StackTrace = exception.StackTrace ?? string.Empty,
                Context = context ?? new Dictionary<string, object>(),
                Timestamp = DateTime.UtcNow
            });
        }

        var contextJson = context != null ? JsonSerializer.Serialize(context) : "{}";
        _logger.LogError(
            exception,
            "Error in {Operation} | Context: {Context}",
            operation,
            contextJson
        );
    }

    public void LogInfo(string message, Dictionary<string, object>? context = null)
    {
        var contextJson = context != null ? JsonSerializer.Serialize(context) : null;
        _logger.LogInformation("{Message} {Context}", message, contextJson ?? string.Empty);
    }

    public void LogWarning(string message, Dictionary<string, object>? context = null)
    {
        var contextJson = context != null ? JsonSerializer.Serialize(context) : null;
        _logger.LogWarning("{Message} {Context}", message, contextJson ?? string.Empty);
    }

    public Task<LogMetrics> GetMetricsAsync()
    {
        var queryLogs = _queryLogs.ToList();
        var indexingLogs = _indexingLogs.ToList();
        var errorLogs = _errorLogs.ToList();

        var metrics = new LogMetrics
        {
            TotalQueries = queryLogs.Count,
            TotalIndexOperations = indexingLogs.Count,
            TotalErrors = errorLogs.Count,
            AverageQueryTimeMs = queryLogs.Any() ? queryLogs.Average(q => q.ProcessingTimeMs) : 0,
            AverageIndexTimeMs = indexingLogs.Any() ? indexingLogs.Average(i => i.ProcessingTimeMs) : 0,
            AverageChunksRetrieved = queryLogs.Any() ? queryLogs.Average(q => q.RetrievedChunks) : 0,
            RecentQueries = queryLogs.OrderByDescending(q => q.Timestamp).Take(10).ToList(),
            RecentErrors = errorLogs.OrderByDescending(e => e.Timestamp).Take(10).ToList(),
            GeneratedAt = DateTime.UtcNow
        };

        return Task.FromResult(metrics);
    }

    public async Task ExportMetricsAsync(string filePath)
    {
        var metrics = await GetMetricsAsync();
        var json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(filePath, json);
    }

    private string TruncateString(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "...";
    }
}

public class QueryLog
{
    public string Query { get; set; } = string.Empty;
    public int RetrievedChunks { get; set; }
    public double ProcessingTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
}

public class IndexingLog
{
    public string Source { get; set; } = string.Empty;
    public int Chunks { get; set; }
    public double ProcessingTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ErrorLog
{
    public string Operation { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class LogMetrics
{
    public int TotalQueries { get; set; }
    public int TotalIndexOperations { get; set; }
    public int TotalErrors { get; set; }
    public double AverageQueryTimeMs { get; set; }
    public double AverageIndexTimeMs { get; set; }
    public double AverageChunksRetrieved { get; set; }
    public List<QueryLog> RecentQueries { get; set; } = new();
    public List<ErrorLog> RecentErrors { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}
