using RagPipeline.Models;
using System.Collections.Concurrent;

namespace RagPipeline.Services;

/// <summary>
/// VECTOR STORE: The Heart of Semantic Search
/// 
/// WHAT IS A VECTOR STORE?
/// A specialized database that stores:
/// 1. Your document chunks (the actual text)
/// 2. Embeddings (mathematical representations of meaning)
/// 3. Allows FAST similarity search
/// 
/// WHY NOT USE REGULAR DATABASE?
/// Traditional DB: "Find documents containing word 'dog'" → keyword matching
/// Vector DB: "Find documents similar to 'canine pet'" → finds "dog", "puppy", "pet" → semantic matching!
/// 
/// REAL-WORLD ANALOGY:
/// Regular search: Looking for exact words in a dictionary
/// Vector search: Finding similar concepts in a library (even if different words used)
/// 
/// EXAMPLE:
/// Question: "How do I fix my code?"
/// Vector search finds:
/// - "Debugging techniques" (doesn't contain "fix", but semantically similar!)
/// - "Troubleshooting guide" (semantically similar!)
/// - NOT "Fixing dinner recipes" (contains "fix" but different meaning!)
/// </summary>
public class InMemoryVectorStore
{
    // CONCURRENT DICTIONARY: Thread-safe storage for chunks
    // Why ConcurrentDictionary? Allows multiple requests to search simultaneously
    // Key = Unique Chunk ID, Value = Complete chunk with text + embedding
    private readonly ConcurrentDictionary<string, DocumentChunk> _chunks = new();

    // Count property: How many chunks are currently indexed
    // Useful for monitoring and statistics
    public int Count => _chunks.Count;

    public Task AddChunkAsync(DocumentChunk chunk)
    {
        _chunks[chunk.Id] = chunk;
        return Task.CompletedTask;
    }

    public Task AddChunksAsync(IEnumerable<DocumentChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            _chunks[chunk.Id] = chunk;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// SEARCH: The core of vector similarity search
    /// 
    /// THIS IS WHERE THE MAGIC HAPPENS!
    /// This function finds chunks most similar to your query
    /// 
    /// HOW IT WORKS (SIMPLIFIED):
    /// 1. You have a question: "What is machine learning?"
    /// 2. Question is converted to embedding: [0.1, 0.8, 0.3, ..., 0.5] (1536 numbers)
    /// 3. We compare this against EVERY chunk's embedding
    /// 4. Calculate similarity score for each (0 = totally different, 1 = identical)
    /// 5. Sort by similarity (best matches first)
    /// 6. Return top K results (e.g., top 3)
    /// 
    /// MATH BEHIND IT: Cosine Similarity
    /// Measures angle between two vectors in high-dimensional space
    /// - 1.0 = Same direction (very similar meaning)
    /// - 0.0 = Perpendicular (unrelated)
    /// - -1.0 = Opposite direction (opposite meaning)
    /// 
    /// REAL EXAMPLE:
    /// Query: "deep learning techniques"     → [0.2, 0.9, 0.1, ..., 0.6]
    /// Chunk1: "neural network training"     → [0.21, 0.88, 0.12, ..., 0.59] → Similarity: 0.95 ✓
    /// Chunk2: "cooking pasta recipes"       → [-0.5, 0.1, -0.3, ..., -0.2] → Similarity: 0.08 ✗
    /// Chunk3: "CNN and RNN architectures"   → [0.19, 0.91, 0.09, ..., 0.61] → Similarity: 0.92 ✓
    /// 
    /// Result: Return Chunk1 and Chunk3 (most relevant to query)
    /// </summary>
    /// <param name="queryEmbedding">The question converted to vector (1536 numbers)</param>
    /// <param name="topK">How many results to return (e.g., top 3)</param>
    /// <returns>List of most similar chunks with similarity scores</returns>
    public Task<List<RetrievalResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int topK)
    {
        // ═══════════════════════════════════════════════════════════════
        // THE SEARCH PIPELINE
        // ═══════════════════════════════════════════════════════════════
        var results = _chunks.Values
            // ───────────────────────────────────────────────────────────
            // STEP 1: For each chunk, calculate similarity score
            // ───────────────────────────────────────────────────────────
            // CosineSimilarity compares query embedding vs chunk embedding
            // Returns a number between -1 and 1
            // Higher = more similar
            .Select(chunk => new
            {
                Chunk = chunk,
                // THIS IS THE KEY OPERATION!
                // Compare query vector with chunk vector
                // Returns how "close" they are in meaning
                Similarity = CosineSimilarity(queryEmbedding, chunk.Embedding)
            })
            // ───────────────────────────────────────────────────────────
            // STEP 2: Sort by similarity (best matches first)
            // ───────────────────────────────────────────────────────────
            // Descending = highest similarity first
            // This ensures most relevant chunks are returned
            .OrderByDescending(x => x.Similarity)
            // ───────────────────────────────────────────────────────────
            // STEP 3: Take only top K results
            // ───────────────────────────────────────────────────────────
            // topK = 3 means "give me 3 best matches"
            // Why limit? Sending too much context to LLM:
            // - Costs more (more tokens)
            // - Slower processing
            // - May confuse the LLM with too much info
            .Take(topK)
            // ───────────────────────────────────────────────────────────
            // STEP 4: Convert to result format
            // ───────────────────────────────────────────────────────────
            // Package up the results with all needed info:
            // - The actual text content
            // - Where it came from (source document)
            // - How similar it is (score)
            .Select(x => new RetrievalResult
            {
                Source = x.Chunk.Source,           // Original document name
                Content = x.Chunk.Content,         // The actual text
                Similarity = x.Similarity,         // How relevant (0-1)
                ChunkId = x.Chunk.Id,             // Unique identifier
                ChunkIndex = x.Chunk.ChunkIndex,  // Position in original document
                Metadata = x.Chunk.Metadata       // Extra info (length, date, etc.)
            })
            .ToList();

        return Task.FromResult(results);
    }

    public Task<List<DocumentChunk>> GetChunksBySourceAsync(string source)
    {
        var chunks = _chunks.Values
            .Where(c => c.Source == source)
            .OrderBy(c => c.ChunkIndex)
            .ToList();

        return Task.FromResult(chunks);
    }

    public Task<bool> RemoveChunkAsync(string chunkId)
    {
        return Task.FromResult(_chunks.TryRemove(chunkId, out _));
    }

    public Task<bool> RemoveDocumentAsync(string source)
    {
        var removed = false;
        var chunksToRemove = _chunks.Values
            .Where(c => c.Source == source)
            .Select(c => c.Id)
            .ToList();

        foreach (var id in chunksToRemove)
        {
            if (_chunks.TryRemove(id, out _))
                removed = true;
        }

        return Task.FromResult(removed);
    }

    public Task ClearAsync()
    {
        _chunks.Clear();
        return Task.CompletedTask;
    }

    public Task<List<string>> GetAllSourcesAsync()
    {
        var sources = _chunks.Values
            .Select(c => c.Source)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        return Task.FromResult(sources);
    }

    public Task<VectorStoreStats> GetStatsAsync()
    {
        var sources = _chunks.Values
            .GroupBy(c => c.Source)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult(new VectorStoreStats
        {
            TotalChunks = _chunks.Count,
            UniqueDocuments = sources.Count,
            ChunksByDocument = sources,
            MemoryUsageEstimate = EstimateMemoryUsage()
        });
    }

    /// <summary>
    /// COSINE SIMILARITY: The Math Behind Semantic Search
    /// 
    /// THIS IS THE CORE ALGORITHM!
    /// Calculates how similar two vectors (embeddings) are
    /// 
    /// WHAT IS COSINE SIMILARITY?
    /// Measures the angle between two vectors in high-dimensional space
    /// - Think of each embedding as an arrow pointing in 1536-dimensional space
    /// - Similar meanings = arrows point in similar directions
    /// - Different meanings = arrows point in different directions
    /// 
    /// FORMULA:
    /// similarity = (A · B) / (||A|| × ||B||)
    /// 
    /// WHERE:
    /// - A · B = dot product (sum of element-wise multiplication)
    /// - ||A|| = magnitude of vector A (length of the arrow)
    /// - ||B|| = magnitude of vector B
    /// 
    /// RESULT RANGE:
    /// -1.0 = Opposite meanings (very rare in practice)
    ///  0.0 = Unrelated (perpendicular vectors)
    ///  1.0 = Identical meanings (same vector)
    /// 
    /// REAL EXAMPLE (simplified to 3D):
    /// Vector A (query): "machine learning" = [0.8, 0.5, 0.1]
    /// Vector B (chunk): "AI algorithms"     = [0.7, 0.6, 0.2]
    /// 
    /// Step 1 - Dot Product: (0.8×0.7) + (0.5×0.6) + (0.1×0.2) = 0.56 + 0.30 + 0.02 = 0.88
    /// Step 2 - Magnitude A: √(0.8² + 0.5² + 0.1²) = √0.90 = 0.949
    /// Step 3 - Magnitude B: √(0.7² + 0.6² + 0.2²) = √0.89 = 0.943
    /// Step 4 - Result: 0.88 / (0.949 × 0.943) = 0.88 / 0.895 = 0.98
    /// 
    /// Interpretation: 0.98 = HIGHLY SIMILAR! ✓
    /// </summary>
    private double CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        // Convert ReadOnlyMemory to arrays for easier processing
        var arrayA = a.ToArray();
        var arrayB = b.ToArray();

        // Safety check: Both vectors must be same length
        // OpenAI embeddings are always 1536 dimensions
        if (arrayA.Length != arrayB.Length)
            throw new ArgumentException("Vectors must have the same length");

        // ═══════════════════════════════════════════════════════════════
        // CALCULATE THREE VALUES
        // ═══════════════════════════════════════════════════════════════
        double dotProduct = 0;    // Sum of element-wise multiplication
        double magnitudeA = 0;    // Length of vector A
        double magnitudeB = 0;    // Length of vector B

        // ───────────────────────────────────────────────────────────────
        // LOOP THROUGH ALL 1536 DIMENSIONS
        // ───────────────────────────────────────────────────────────────
        // This is where we compare every single number in the embeddings
        for (int i = 0; i < arrayA.Length; i++)
        {
            // DOT PRODUCT: Multiply corresponding elements and sum
            // This measures how much vectors point in same direction
            // Example: A[0]=0.5, B[0]=0.6 → 0.5×0.6 = 0.3 (adds to total)
            dotProduct += arrayA[i] * arrayB[i];

            // MAGNITUDE A: Sum of squares (will take sqrt later)
            // This calculates the "length" of vector A
            // Like measuring length of arrow using Pythagoras theorem
            magnitudeA += arrayA[i] * arrayA[i];

            // MAGNITUDE B: Sum of squares for vector B
            magnitudeB += arrayB[i] * arrayB[i];
        }

        // ═══════════════════════════════════════════════════════════════
        // EDGE CASE: Zero vectors
        // ═══════════════════════════════════════════════════════════════
        // If either magnitude is 0, we can't divide (would be division by zero)
        // This should never happen with real embeddings, but safety first!
        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        // ═══════════════════════════════════════════════════════════════
        // FINAL CALCULATION
        // ═══════════════════════════════════════════════════════════════
        // 1. Take square root of magnitudes (complete the Pythagoras calculation)
        // 2. Divide dot product by product of magnitudes
        // 3. Result = similarity score between 0 and 1
        return dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }

    private long EstimateMemoryUsage()
    {
        // Rough estimate: each chunk with 1536-dim embedding ~= 6KB + content size
        long totalSize = 0;
        foreach (var chunk in _chunks.Values)
        {
            totalSize += chunk.Content.Length * 2; // chars are 2 bytes
            totalSize += chunk.Embedding.Length * 4; // floats are 4 bytes
            totalSize += 1024; // overhead
        }
        return totalSize;
    }
}

public class VectorStoreStats
{
    public int TotalChunks { get; set; }
    public int UniqueDocuments { get; set; }
    public Dictionary<string, int> ChunksByDocument { get; set; } = new();
    public long MemoryUsageEstimate { get; set; }
}
