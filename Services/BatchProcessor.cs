using RagPipeline.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RagPipeline.Services;

public class BatchProcessor
{
    private readonly RagEngine _ragEngine;
    private readonly IRagLogger? _logger;
    private readonly int _maxDegreeOfParallelism;

    public BatchProcessor(RagEngine ragEngine, IRagLogger? logger = null, int maxDegreeOfParallelism = 4)
    {
        _ragEngine = ragEngine;
        _logger = logger;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    public async Task<BatchIndexingResult> IndexDocumentsAsync(
        Dictionary<string, string> documents,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new ConcurrentBag<IndexingResult>();
        var completed = 0;
        var total = documents.Count;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        try
        {
            await Parallel.ForEachAsync(documents, options, async (doc, ct) =>
            {
                try
                {
                    var result = await _ragEngine.IndexDocumentAsync(doc.Key, doc.Value);
                    results.Add(result);

                    Interlocked.Increment(ref completed);
                    progress?.Report(new BatchProgress
                    {
                        Completed = completed,
                        Total = total,
                        CurrentDocument = doc.Key
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError("BatchIndexing", ex, new Dictionary<string, object>
                    {
                        ["Document"] = doc.Key
                    });

                    results.Add(new IndexingResult
                    {
                        Source = doc.Key,
                        Success = false,
                        Error = ex.Message
                    });
                }
            });

            stopwatch.Stop();

            var resultsList = results.ToList();
            return new BatchIndexingResult
            {
                TotalDocuments = total,
                SuccessfulIndexes = resultsList.Count(r => r.Success),
                FailedIndexes = resultsList.Count(r => !r.Success),
                Results = resultsList,
                TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new BatchIndexingResult
            {
                TotalDocuments = total,
                SuccessfulIndexes = results.Count(r => r.Success),
                FailedIndexes = results.Count(r => !r.Success),
                Results = results.ToList(),
                TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Cancelled = true
            };
        }
    }

    public async Task<BatchIndexingResult> IndexDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.txt",
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var files = Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories);
        var documents = new Dictionary<string, string>();

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var relativePath = Path.GetRelativePath(directoryPath, file);
            documents[relativePath] = content;
        }

        return await IndexDocumentsAsync(documents, progress, cancellationToken);
    }

    public async Task<BatchQueryResult> QueryBatchAsync(
        List<string> queries,
        int topK = 3,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new ConcurrentBag<RagResult>();
        var completed = 0;
        var total = queries.Count;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(queries, options, async (query, ct) =>
        {
            try
            {
                var result = await _ragEngine.QueryAsync(query, topK);
                results.Add(result);

                Interlocked.Increment(ref completed);
                progress?.Report(new BatchProgress
                {
                    Completed = completed,
                    Total = total,
                    CurrentDocument = query
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError("BatchQuery", ex, new Dictionary<string, object>
                {
                    ["Query"] = query
                });

                results.Add(new RagResult
                {
                    Query = query,
                    Success = false,
                    Error = ex.Message
                });
            }
        });

        stopwatch.Stop();

        var resultsList = results.ToList();
        return new BatchQueryResult
        {
            TotalQueries = total,
            SuccessfulQueries = resultsList.Count(r => r.Success),
            FailedQueries = resultsList.Count(r => !r.Success),
            Results = resultsList,
            TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds,
            AverageProcessingTimeMs = resultsList.Any() ? resultsList.Average(r => r.ProcessingTimeMs) : 0
        };
    }
}

public class BatchProgress
{
    public int Completed { get; set; }
    public int Total { get; set; }
    public string CurrentDocument { get; set; } = string.Empty;
    public double PercentComplete => Total > 0 ? (double)Completed / Total * 100 : 0;
}

public class BatchIndexingResult
{
    public int TotalDocuments { get; set; }
    public int SuccessfulIndexes { get; set; }
    public int FailedIndexes { get; set; }
    public List<IndexingResult> Results { get; set; } = new();
    public double TotalProcessingTimeMs { get; set; }
    public bool Cancelled { get; set; }
}

public class BatchQueryResult
{
    public int TotalQueries { get; set; }
    public int SuccessfulQueries { get; set; }
    public int FailedQueries { get; set; }
    public List<RagResult> Results { get; set; } = new();
    public double TotalProcessingTimeMs { get; set; }
    public double AverageProcessingTimeMs { get; set; }
}
