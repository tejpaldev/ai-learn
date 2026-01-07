using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.ChatCompletion;
using RagPipeline.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RagPipeline.Services;

/// <summary>
/// RAG ENGINE: The Heart of Retrieval-Augmented Generation
/// This is the main orchestrator that brings together all RAG components:
/// 1. Takes your documents and breaks them into searchable chunks
/// 2. Converts text into mathematical vectors (embeddings) that AI can understand
/// 3. Stores these vectors in a searchable database
/// 4. When you ask a question, finds the most relevant chunks
/// 5. Feeds those chunks to an LLM (like ChatGPT) to generate an accurate answer
/// 
/// Think of it like a smart librarian who:
/// - Organizes books (indexing)
/// - Quickly finds relevant pages when you ask a question (retrieval)
/// - Reads those pages and gives you a summary (generation)
/// </summary>
public class RagEngine
{
    // KERNEL: The brain that connects to OpenAI's GPT models for chat completion
    // This is what generates the final answer after we retrieve relevant context
    private readonly Kernel _kernel;

    // EMBEDDING GENERATOR: Converts text into vectors (arrays of numbers)
    // Why? Because AI can't directly compare text, but it can compare numbers
    // Similar texts get similar vectors, allowing semantic search
    // Example: "car" and "automobile" have similar vectors even though words differ
    private readonly ITextEmbeddingGenerationService _embeddingGenerator;

    // VECTOR STORE: Database that stores document chunks with their embeddings
    // Think of it as a library where each book page is tagged with its "meaning fingerprint"
    // When you search, it finds pages with similar "fingerprints" to your question
    private readonly InMemoryVectorStore _vectorStore;

    // DOCUMENT PROCESSOR: Breaks large documents into smaller, manageable chunks
    // Why chunk? LLMs have token limits and work better with focused context
    // Like giving someone specific paragraphs instead of an entire book
    private readonly DocumentProcessor _documentProcessor;

    // LOGGER: Tracks performance metrics (how long queries take, success rate, etc.)
    // Essential for production to monitor and optimize your RAG system
    private readonly IRagLogger? _ragLogger;

    // CACHE: Stores frequently accessed data to avoid redundant API calls
    // Why? OpenAI API calls cost money and take time
    // If someone asks the same question twice, use cached result instead
    private readonly ICacheService? _cacheService;

    // RESILIENCE: Handles failures gracefully (retry on errors, circuit breaker, etc.)
    // Network issues, rate limits, timeouts - this handles them automatically
    private readonly IResilienceService? _resilienceService;

    // SIMILARITY THRESHOLD: Minimum score (0-1) for a chunk to be considered relevant
    // 0.7 means "70% similar" - filters out irrelevant chunks
    // Higher = stricter (fewer but more relevant results)
    // Lower = looser (more results but some may be less relevant)
    private readonly double _similarityThreshold;

    public RagEngine(
        Kernel kernel,
        ITextEmbeddingGenerationService embeddingGenerator,
        InMemoryVectorStore vectorStore,
        DocumentProcessor documentProcessor,
        IRagLogger? ragLogger = null,
        ICacheService? cacheService = null,
        IResilienceService? resilienceService = null,
        double similarityThreshold = 0.7)
    {
        _kernel = kernel;
        _embeddingGenerator = embeddingGenerator;
        _vectorStore = vectorStore;
        _documentProcessor = documentProcessor;
        _ragLogger = ragLogger;
        _cacheService = cacheService;
        _resilienceService = resilienceService;
        _similarityThreshold = similarityThreshold;
    }

    /// <summary>
    /// INDEXING: The process of preparing documents for RAG
    /// This is like creating an index in the back of a book, but much smarter
    /// 
    /// What happens step by step:
    /// 1. Break document into chunks (smaller pieces)
    /// 2. Convert each chunk into a vector embedding (array of numbers representing meaning)
    /// 3. Store chunks + embeddings in vector database for later retrieval
    /// 
    /// Why indexing matters:
    /// - You do this ONCE per document
    /// - Then you can query it MANY times
    /// - Like building a search index for Google - upfront work enables fast searches
    /// </summary>
    public async Task<IndexingResult> IndexDocumentAsync(string source, string content)
    {
        // Start a timer to measure how long indexing takes (for performance monitoring)
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // ═══════════════════════════════════════════════════════════════
            // STEP 1: CHUNKING - Break document into smaller pieces
            // ═══════════════════════════════════════════════════════════════
            // WHY? LLMs have token limits (e.g., GPT-3.5 = 4096 tokens)
            // A full document might be too large to process at once
            // Chunks allow us to find EXACTLY the relevant sections
            // Example: Instead of searching entire Wikipedia, search specific paragraphs
            var chunks = _documentProcessor.ChunkDocument(source, content);

            // Safety check: Make sure we got at least one chunk
            // If document is empty or invalid, stop here
            if (!chunks.Any())
            {
                throw new IndexingException("No chunks generated from document", source);
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 2: EMBEDDING GENERATION - Convert text to vectors
            // ═══════════════════════════════════════════════════════════════
            // This is the MAGIC of RAG! We convert text into numbers
            // 
            // WHAT IS AN EMBEDDING?
            // - A list of floating-point numbers (typically 1536 numbers for OpenAI)
            // - Represents the "meaning" of text in mathematical space
            // - Similar meanings = similar numbers
            // 
            // EXAMPLE:
            // "dog" might be [0.2, 0.8, 0.1, ..., 0.5] (1536 numbers)
            // "puppy" might be [0.21, 0.79, 0.11, ..., 0.49] (very similar!)
            // "car" might be [-0.5, 0.1, 0.9, ..., -0.2] (very different!)
            //
            // WHY PROCESS IN PARALLEL?
            // Each chunk needs an API call to OpenAI (slow!)
            // Processing all at once = much faster than one-by-one
            var tasks = chunks.Select(async chunk =>
            {
                try
                {
                    // ─────────────────────────────────────────────────────
                    // OPTIMIZATION 1: Check Cache First
                    // ─────────────────────────────────────────────────────
                    // If we already computed embedding for this text, reuse it!
                    // Why? OpenAI embeddings cost $0.0001 per 1K tokens
                    // Cache hit = FREE + INSTANT instead of API call
                    string? cacheKey = null;
                    if (_cacheService != null)
                    {
                        // Generate unique key based on content
                        // Same content = same key = cache hit!
                        cacheKey = CacheService.GenerateCacheKey("embedding", chunk.Content);
                        ReadOnlyMemory<float>? cachedEmbedding = await _cacheService.GetAsync<ReadOnlyMemory<float>>(cacheKey);

                        // If found in cache and valid, use it and skip API call
                        // ReadOnlyMemory<float> default has Length 0, so checking Length > 0 means cache hit
                        if (cachedEmbedding.HasValue && cachedEmbedding.Value.Length > 0)
                        {
                            chunk.Embedding = cachedEmbedding.Value;
                            return; // Done! No API call needed
                        }
                    }

                    // ─────────────────────────────────────────────────────
                    // GENERATE NEW EMBEDDING: Call OpenAI API
                    // ─────────────────────────────────────────────────────
                    // This is the expensive operation:
                    // 1. Sends text to OpenAI
                    // 2. OpenAI's model converts it to vector
                    // 3. Returns array of 1536 floating-point numbers
                    //
                    // WITH RESILIENCE: If it fails (network issue, rate limit),
                    // automatically retry with exponential backoff
                    // Try 1: immediate
                    // Try 2: wait 2 seconds
                    // Try 3: wait 4 seconds
                    if (_resilienceService != null)
                    {
                        chunk.Embedding = await _resilienceService.ExecuteWithRetryAsync(
                            async () => await _embeddingGenerator.GenerateEmbeddingAsync(chunk.Content),
                            $"Generate embedding for chunk {chunk.Id}"
                        );
                    }
                    else
                    {
                        // Without resilience, just try once
                        chunk.Embedding = await _embeddingGenerator.GenerateEmbeddingAsync(chunk.Content);
                    }

                    // ─────────────────────────────────────────────────────
                    // OPTIMIZATION 2: Cache the Result
                    // ─────────────────────────────────────────────────────
                    // Store embedding in cache for 1 hour
                    // If document is re-indexed within 1 hour, use cache
                    if (_cacheService != null && cacheKey != null)
                    {
                        await _cacheService.SetAsync(cacheKey, chunk.Embedding, TimeSpan.FromHours(1));
                    }
                }
                catch (Exception ex)
                {
                    // If embedding generation fails, throw specific error
                    throw new EmbeddingException($"Failed to generate embedding for chunk {chunk.Id}", ex);
                }
            });

            // Wait for ALL embeddings to complete
            // This runs in parallel, so much faster than sequential
            await Task.WhenAll(tasks);

            // ═══════════════════════════════════════════════════════════════
            // STEP 3: STORE IN VECTOR DATABASE
            // ═══════════════════════════════════════════════════════════════
            // Now that chunks have both TEXT and EMBEDDINGS, store them
            // Vector store enables FAST similarity search later
            // It's like Google's index but for semantic meaning, not keywords
            await _vectorStore.AddChunksAsync(chunks);

            stopwatch.Stop();
            _ragLogger?.LogIndexing(source, chunks.Count, stopwatch.ElapsedMilliseconds);

            return new IndexingResult
            {
                Source = source,
                ChunksCreated = chunks.Count,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Success = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _ragLogger?.LogError("IndexDocument", ex, new Dictionary<string, object>
            {
                ["Source"] = source,
                ["ContentLength"] = content.Length
            });

            return new IndexingResult
            {
                Source = source,
                Success = false,
                Error = ex.Message,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// QUERY: The core of RAG - Answer questions using your indexed documents
    /// 
    /// THE RAG PROCESS (3 STEPS):
    /// 1. RETRIEVAL: Find relevant chunks from vector database
    /// 2. AUGMENTATION: Build context from retrieved chunks
    /// 3. GENERATION: Use LLM to generate answer based on context
    /// 
    /// WHY RAG IS POWERFUL:
    /// - Regular LLM: Only knows what it was trained on (can hallucinate)
    /// - RAG: Grounds answers in YOUR documents (accurate, verifiable)
    /// 
    /// EXAMPLE:
    /// Without RAG: "When was our company founded?" → LLM guesses (wrong!)
    /// With RAG: Retrieves your company docs → "Founded in 2020" (correct!)
    /// </summary>
    public async Task<RagResult> QueryAsync(string query, int topK = 3, double? customThreshold = null)
    {
        // Start timer for performance monitoring
        var stopwatch = Stopwatch.StartNew();

        // Use custom threshold if provided, otherwise use default (0.7)
        // Threshold filters out chunks that aren't similar enough
        var threshold = customThreshold ?? _similarityThreshold;

        try
        {
            // Safety check: Don't process empty questions
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new QueryException("Query cannot be empty", query);
            }

            // ═══════════════════════════════════════════════════════════════
            // OPTIMIZATION: Check if we answered this exact question before
            // ═══════════════════════════════════════════════════════════════
            // If user asks "What is AI?" twice, return cached answer instantly
            // Saves API calls, money, and time (30ms vs 2000ms)
            string? queryCacheKey = null;
            if (_cacheService != null)
            {
                // Create unique cache key from query + topK parameter
                // Different topK = different cache entry
                queryCacheKey = CacheService.GenerateCacheKey("query", query, topK);
                var cachedResult = await _cacheService.GetAsync<RagResult>(queryCacheKey);

                // If found, return immediately (cache hit!)
                if (cachedResult != null)
                {
                    stopwatch.Stop();
                    cachedResult.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                    cachedResult.FromCache = true; // Mark as cached for transparency
                    return cachedResult;
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 1: CONVERT QUESTION TO VECTOR (EMBEDDING)
            // ═══════════════════════════════════════════════════════════════
            // To find relevant chunks, we need question in same format (vector)
            // 
            // CONCEPT: Similarity Search
            // Question: "How do I train a model?"
            // → Embedding: [0.1, 0.8, 0.3, ..., 0.5]
            // 
            // Compare against all chunks:
            // Chunk A: "Model training requires data" → [0.11, 0.79, 0.29, ...] → 95% similar ✓
            // Chunk B: "Pizza is delicious" → [-0.3, 0.2, -0.8, ...] → 12% similar ✗
            // 
            // We'll retrieve Chunk A because it's semantically similar!
            ReadOnlyMemory<float> queryEmbedding;
            if (_resilienceService != null)
            {
                // With resilience: Retry if API fails
                queryEmbedding = await _resilienceService.ExecuteWithRetryAsync(
                    async () => await _embeddingGenerator.GenerateEmbeddingAsync(query),
                    "Generate query embedding"
                );
            }
            else
            {
                // Without resilience: Try once
                queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(query);
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 2: RETRIEVAL - Find most relevant chunks
            // ═══════════════════════════════════════════════════════════════
            // This is where the "R" in RAG happens!
            // 
            // VECTOR SEARCH EXPLAINED:
            // 1. Compare query embedding with ALL chunk embeddings
            // 2. Calculate similarity score (cosine similarity: -1 to 1)
            // 3. Sort by similarity (highest first)
            // 4. Take top K results (e.g., top 3)
            // 
            // topK = 3 means "give me 3 most relevant chunks"
            // Why not all chunks? Too much context confuses the LLM!
            var retrievedChunks = await _vectorStore.SearchAsync(queryEmbedding, topK);

            // ═══════════════════════════════════════════════════════════════
            // FILTERING: Apply Similarity Threshold
            // ═══════════════════════════════════════════════════════════════
            // Even in top K, some chunks might not be relevant enough
            // Threshold = 0.7 means "only keep chunks with 70%+ similarity"
            // 
            // EXAMPLE:
            // Question: "What is machine learning?"
            // Chunk 1: "Machine learning is..." → Similarity: 0.92 ✓ Keep
            // Chunk 2: "ML algorithms include..." → Similarity: 0.78 ✓ Keep
            // Chunk 3: "The weather is nice" → Similarity: 0.35 ✗ Discard
            retrievedChunks = retrievedChunks.Where(c => c.Similarity >= threshold).ToList();

            // ═══════════════════════════════════════════════════════════════
            // EDGE CASE: No relevant chunks found
            // ═══════════════════════════════════════════════════════════════
            // If all chunks below threshold, be honest - don't make up answer!
            if (!retrievedChunks.Any())
            {
                return new RagResult
                {
                    Query = query,
                    Answer = "I couldn't find any relevant information to answer your question.",
                    RetrievedChunks = new List<RetrievalResult>(),
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    Success = true
                };
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 3: AUGMENTATION - Build context for LLM
            // ═══════════════════════════════════════════════════════════════
            // Combine retrieved chunks into one big context string
            // This is the "A" in RAG - Augmented with retrieved knowledge!
            // 
            // FORMAT:
            // [Source: doc1.txt | Relevance: 0.95]
            // Machine learning is a subset of AI...
            //
            // [Source: doc2.txt | Relevance: 0.88]
            // Neural networks are ML models...
            var context = string.Join("\n\n", retrievedChunks.Select(r =>
                $"[Source: {r.Source} | Relevance: {r.Similarity:F2}]\n{r.Content}"));

            // ═══════════════════════════════════════════════════════════════
            // STEP 4: GENERATION - Get LLM to answer based on context
            // ═══════════════════════════════════════════════════════════════
            // This is the "G" in RAG - Generation!
            // Send context + question to GPT to generate grounded answer
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();

            // ═══════════════════════════════════════════════════════════════
            // THE PROMPT: How we instruct the LLM
            // ═══════════════════════════════════════════════════════════════
            // This prompt is CRITICAL - it tells GPT:
            // 1. Your role (helpful assistant)
            // 2. The context (retrieved chunks)
            // 3. The question
            // 4. Rules (don't hallucinate, cite sources, be honest)
            var prompt = $@"You are a helpful AI assistant that answers questions based on the provided context.

Context:
{context}

Question: {query}

Instructions:
- Answer the question based solely on the context provided above
- If the context doesn't contain enough information to answer the question, clearly state that
- Be concise, accurate, and professional
- When relevant, reference which source(s) your answer comes from
- If the answer requires information not in the context, explain what information is missing

Answer:";

            string answer;
            if (_resilienceService != null)
            {
                var response = await _resilienceService.ExecuteWithCircuitBreakerAsync(
                    async () => await chatCompletion.GetChatMessageContentAsync(prompt),
                    "Generate answer"
                );
                answer = response.Content ?? "No answer generated.";
            }
            else
            {
                var response = await chatCompletion.GetChatMessageContentAsync(prompt);
                answer = response.Content ?? "No answer generated.";
            }

            stopwatch.Stop();

            var result = new RagResult
            {
                Query = query,
                Answer = answer,
                RetrievedChunks = retrievedChunks,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Success = true
            };

            // Cache the result
            if (_cacheService != null && queryCacheKey != null)
            {
                await _cacheService.SetAsync(queryCacheKey, result, TimeSpan.FromMinutes(30));
            }

            _ragLogger?.LogQuery(query, retrievedChunks.Count, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _ragLogger?.LogError("Query", ex, new Dictionary<string, object>
            {
                ["Query"] = query,
                ["TopK"] = topK
            });

            return new RagResult
            {
                Query = query,
                Answer = "An error occurred while processing your query.",
                Error = ex.Message,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Success = false
            };
        }
    }

    public async Task<bool> RemoveDocumentAsync(string source)
    {
        try
        {
            return await _vectorStore.RemoveDocumentAsync(source);
        }
        catch (Exception ex)
        {
            _ragLogger?.LogError("RemoveDocument", ex, new Dictionary<string, object>
            {
                ["Source"] = source
            });
            return false;
        }
    }

    public async Task ClearIndexAsync()
    {
        await _vectorStore.ClearAsync();
        if (_cacheService != null)
        {
            await _cacheService.ClearAsync();
        }
    }

    public int GetIndexedChunksCount() => _vectorStore.Count;

    public async Task<List<string>> GetIndexedDocumentsAsync()
    {
        return await _vectorStore.GetAllSourcesAsync();
    }

    public async Task<VectorStoreStats> GetStatsAsync()
    {
        return await _vectorStore.GetStatsAsync();
    }
}

public class IndexingResult
{
    public string Source { get; set; } = string.Empty;
    public int ChunksCreated { get; set; }
    public double ProcessingTimeMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
