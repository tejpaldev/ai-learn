using RagPipeline.Models;

namespace RagPipeline.Services;

public class InMemoryVectorStore
{
    private readonly List<DocumentChunk> _chunks = new();

    public Task AddChunkAsync(DocumentChunk chunk)
    {
        _chunks.Add(chunk);
        return Task.CompletedTask;
    }

    public Task<List<RetrievalResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int topK)
    {
        var results = _chunks
            .Select(chunk => new RetrievalResult
            {
                Source = chunk.Source,
                Content = chunk.Content,
                Similarity = CosineSimilarity(queryEmbedding, chunk.Embedding)
            })
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .ToList();

        return Task.FromResult(results);
    }

    public Task ClearAsync()
    {
        _chunks.Clear();
        return Task.CompletedTask;
    }

    public int Count => _chunks.Count;

    private static double CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;

        if (spanA.Length != spanB.Length)
            return 0;

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < spanA.Length; i++)
        {
            dotProduct += spanA[i] * spanB[i];
            magnitudeA += spanA[i] * spanA[i];
            magnitudeB += spanB[i] * spanB[i];
        }

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
