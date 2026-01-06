using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.ChatCompletion;
using RagPipeline.Models;

namespace RagPipeline.Services;

public class RagEngine
{
    private readonly Kernel _kernel;
    private readonly ITextEmbeddingGenerationService _embeddingGenerator;
    private readonly InMemoryVectorStore _vectorStore;
    private readonly DocumentProcessor _documentProcessor;

    public RagEngine(
        Kernel kernel,
        ITextEmbeddingGenerationService embeddingGenerator,
        InMemoryVectorStore vectorStore,
        DocumentProcessor documentProcessor)
    {
        _kernel = kernel;
        _embeddingGenerator = embeddingGenerator;
        _vectorStore = vectorStore;
        _documentProcessor = documentProcessor;
    }

    public async Task IndexDocumentAsync(string source, string content)
    {
        // Step 1: Chunk the document
        var chunks = _documentProcessor.ChunkDocument(source, content);

        // Step 2: Generate embeddings for each chunk
        foreach (var chunk in chunks)
        {
            var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(chunk.Content);
            chunk.Embedding = embedding;
            
            // Step 3: Store in vector store
            await _vectorStore.AddChunkAsync(chunk);
        }
    }

    public async Task<RagResult> QueryAsync(string query, int topK = 3)
    {
        // Step 1: Generate query embedding
        var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(query);

        // Step 2: Retrieve relevant chunks
        var retrievedChunks = await _vectorStore.SearchAsync(queryEmbedding, topK);

        // Step 3: Build context from retrieved chunks
        var context = string.Join("\n\n", retrievedChunks.Select(r => 
            $"[From {r.Source}]\n{r.Content}"));

        // Step 4: Generate answer using LLM with context
        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
        
        var prompt = $@"You are a helpful assistant that answers questions based on the provided context.

Context:
{context}

Question: {query}

Instructions:
- Answer the question based on the context provided above
- If the context doesn't contain enough information to answer the question, say so
- Be concise and accurate
- Reference specific information from the context when relevant

Answer:";

        var response = await chatCompletion.GetChatMessageContentAsync(prompt);

        return new RagResult
        {
            Query = query,
            Answer = response.Content ?? "No answer generated.",
            RetrievedChunks = retrievedChunks
        };
    }

    public async Task ClearIndexAsync()
    {
        await _vectorStore.ClearAsync();
    }

    public int GetIndexedChunksCount() => _vectorStore.Count;
}
