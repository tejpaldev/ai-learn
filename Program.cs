using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using RagPipeline.Services;
using RagPipeline.Models;

namespace RagPipeline;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== .NET RAG Pipeline Example ===\n");

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var apiKey = configuration["OpenAI:ApiKey"]!;
        var embeddingModel = configuration["OpenAI:EmbeddingModel"]!;
        var chatModel = configuration["OpenAI:ChatModel"]!;

        if (apiKey == "your-openai-api-key-here")
        {
            Console.WriteLine("⚠️  Please set your OpenAI API key in appsettings.json or environment variable");
            Console.WriteLine("   You can also use Azure OpenAI by modifying the configuration\n");
        }

        // Initialize services
        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(chatModel, apiKey)
            .Build();

        var embeddingGenerator = new OpenAITextEmbeddingGenerationService(
            embeddingModel, 
            apiKey
        );

        var documentProcessor = new DocumentProcessor(
            chunkSize: int.Parse(configuration["RAG:ChunkSize"]!),
            chunkOverlap: int.Parse(configuration["RAG:ChunkOverlap"]!)
        );

        var vectorStore = new InMemoryVectorStore();
        var ragEngine = new RagEngine(kernel, embeddingGenerator, vectorStore, documentProcessor);

        // Step 1: Load and index documents
        Console.WriteLine("📚 Step 1: Loading and indexing documents...\n");
        await LoadSampleDocuments(ragEngine);

        // Step 2: Interactive Q&A
        Console.WriteLine("\n💬 Step 2: Interactive Q&A (type 'exit' to quit)\n");
        await InteractiveQA(ragEngine, int.Parse(configuration["RAG:TopK"]!));
    }

    static async Task LoadSampleDocuments(RagEngine ragEngine)
    {
        var documentsPath = Path.Combine(Directory.GetCurrentDirectory(), "documents");
        
        if (Directory.Exists(documentsPath))
        {
            var files = Directory.GetFiles(documentsPath, "*.txt");
            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file);
                var fileName = Path.GetFileName(file);
                await ragEngine.IndexDocumentAsync(fileName, content);
                Console.WriteLine($"   ✓ Indexed: {fileName}");
            }
        }
        else
        {
            // Load sample in-memory documents
            Console.WriteLine("   No documents folder found. Using sample documents...\n");
            
            await ragEngine.IndexDocumentAsync(
                "ai_basics.txt",
                @"Artificial Intelligence (AI) is the simulation of human intelligence by machines. 
                It includes machine learning, where systems learn from data, and deep learning, 
                which uses neural networks. AI is used in various applications like image recognition, 
                natural language processing, and autonomous vehicles."
            );

            await ragEngine.IndexDocumentAsync(
                "machine_learning.txt",
                @"Machine Learning is a subset of AI that enables systems to learn and improve from 
                experience without being explicitly programmed. There are three main types: 
                supervised learning (labeled data), unsupervised learning (unlabeled data), 
                and reinforcement learning (reward-based). Popular algorithms include decision trees, 
                neural networks, and support vector machines."
            );

            await ragEngine.IndexDocumentAsync(
                "rag_systems.txt",
                @"Retrieval-Augmented Generation (RAG) is a technique that combines information retrieval 
                with text generation. It retrieves relevant documents from a knowledge base and uses them 
                as context for generating responses. This approach reduces hallucinations and provides 
                more accurate, grounded answers. RAG systems typically use vector embeddings to find 
                similar documents."
            );

            Console.WriteLine("   ✓ Indexed: ai_basics.txt");
            Console.WriteLine("   ✓ Indexed: machine_learning.txt");
            Console.WriteLine("   ✓ Indexed: rag_systems.txt");
        }
    }

    static async Task InteractiveQA(RagEngine ragEngine, int topK)
    {
        while (true)
        {
            Console.Write("Question: ");
            var question = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(question) || question.ToLower() == "exit")
            {
                Console.WriteLine("\n👋 Goodbye!");
                break;
            }

            Console.WriteLine("\n🔍 Retrieving relevant context...");
            var result = await ragEngine.QueryAsync(question, topK);

            Console.WriteLine($"\n📄 Found {result.RetrievedChunks.Count} relevant chunks:");
            for (int i = 0; i < result.RetrievedChunks.Count; i++)
            {
                var chunk = result.RetrievedChunks[i];
                Console.WriteLine($"   {i + 1}. {chunk.Source} (similarity: {chunk.Similarity:F3})");
            }

            Console.WriteLine($"\n🤖 Answer:\n{result.Answer}\n");
            Console.WriteLine(new string('-', 80) + "\n");
        }
    }
}
