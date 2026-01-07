using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Microsoft.Extensions.Logging;

namespace RagPipeline.Services;

public interface IResilienceService
{
    Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        string operationName,
        int maxRetries = 3);

    Task<T> ExecuteWithCircuitBreakerAsync<T>(
        Func<Task<T>> action,
        string operationName);
}

public class ResilienceService : IResilienceService
{
    private readonly ILogger<ResilienceService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;

    public ResilienceService(ILogger<ResilienceService> logger, int maxRetries = 3)
    {
        _logger = logger;

        // Exponential backoff retry policy
        _retryPolicy = Policy
            .Handle<Exception>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Retry {RetryCount} after {Delay}s due to: {Exception}",
                        retryCount,
                        timeSpan.TotalSeconds,
                        exception.Message
                    );
                });

        // Circuit breaker: break after 5 consecutive failures, break for 30 seconds
        _circuitBreakerPolicy = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (exception, duration) =>
                {
                    _logger.LogError(
                        "Circuit breaker opened for {Duration}s due to: {Exception}",
                        duration.TotalSeconds,
                        exception.Message
                    );
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker half-open, testing...");
                });
    }

    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        string operationName,
        int maxRetries = 3)
    {
        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                _logger.LogDebug("Executing {Operation}", operationName);
                return await action();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {Operation} after {MaxRetries} retries",
                operationName, maxRetries);
            throw;
        }
    }

    public async Task<T> ExecuteWithCircuitBreakerAsync<T>(
        Func<Task<T>> action,
        string operationName)
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
            {
                return await action();
            });
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError("Circuit breaker is open for {Operation}", operationName);
            throw new InvalidOperationException($"Service temporarily unavailable for {operationName}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {Operation}", operationName);
            throw;
        }
    }

    private bool IsTransientError(Exception exception)
    {
        // Add logic to determine if error is transient (network, timeout, rate limit, etc.)
        return exception is HttpRequestException
            || exception is TaskCanceledException
            || exception is TimeoutException
            || (exception.Message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ?? false)
            || (exception.Message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

public class RagException : Exception
{
    public string Operation { get; }
    public Dictionary<string, object> Context { get; }

    public RagException(string message, string operation, Dictionary<string, object>? context = null)
        : base(message)
    {
        Operation = operation;
        Context = context ?? new Dictionary<string, object>();
    }

    public RagException(string message, string operation, Exception innerException, Dictionary<string, object>? context = null)
        : base(message, innerException)
    {
        Operation = operation;
        Context = context ?? new Dictionary<string, object>();
    }
}

public class IndexingException : RagException
{
    public string DocumentSource { get; }

    public IndexingException(string message, string documentSource, Exception? innerException = null)
        : base(message, "Indexing", innerException, new Dictionary<string, object> { ["DocumentSource"] = documentSource })
    {
        DocumentSource = documentSource;
    }
}

public class QueryException : RagException
{
    public string Query { get; }

    public QueryException(string message, string query, Exception? innerException = null)
        : base(message, "Query", innerException, new Dictionary<string, object> { ["Query"] = query })
    {
        Query = query;
    }
}

public class EmbeddingException : RagException
{
    public EmbeddingException(string message, Exception? innerException = null)
        : base(message, "Embedding", innerException)
    {
    }
}
