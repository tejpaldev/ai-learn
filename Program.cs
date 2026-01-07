using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using RagPipeline.Services;
using RagPipeline.Models;
using RagPipeline.Models.Configuration;
using RagPipeline.Api;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace RagPipeline;

class Program
{
    static async Task Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/ragpipeline-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var mode = args.Length > 0 ? args[0].ToLower() : "--cli";

            if (mode == "--api" || mode == "api")
            {
                await RunApiMode(args);
            }
            else
            {
                await RunCliMode();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    static async Task RunApiMode(string[] args)
    {
        Console.WriteLine("🚀 Starting RAG Pipeline API...\n");

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog();

        var startup = new ApiStartup(builder.Configuration);
        startup.ConfigureServices(builder.Services);

        var app = builder.Build();
        startup.Configure(app, app.Environment);

        var settings = app.Services.GetRequiredService<RagPipelineSettings>();
        var port = settings.Api.Port;

        Console.WriteLine($"📡 API Server running on: http://localhost:{port}");
        Console.WriteLine($"📚 Swagger UI: http://localhost:{port}/swagger");
        Console.WriteLine($"🏥 Health Check: http://localhost:{port}/api/health");
        Console.WriteLine("\nPress Ctrl+C to stop\n");

        await app.RunAsync();
    }

    static async Task RunCliMode()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     Production-Ready RAG Pipeline - CLI Mode          ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = new RagPipelineSettings();
        configuration.Bind(settings);

        // Validate API key
        if (settings.OpenAI.ApiKey == "your-openai-api-key-here" && !settings.AzureOpenAI.Enabled)
        {
            Console.WriteLine("⚠️  Please set your OpenAI API key in appsettings.json");
            Console.WriteLine("   or use environment variable: OPENAI__APIKEY\n");
            return;
        }

        // Setup dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddSerilog();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(settings);

        // Register services
        RegisterServices(services, settings);

        var serviceProvider = services.BuildServiceProvider();

        // Get services
        var ragEngine = serviceProvider.GetRequiredService<RagEngine>();
        var ragLogger = serviceProvider.GetRequiredService<IRagLogger>();
        var batchProcessor = serviceProvider.GetRequiredService<BatchProcessor>();

        // Display menu
        while (true)
        {
            DisplayMenu();
            var choice = Console.ReadLine()?.Trim();

            try
            {
                switch (choice)
                {
                    case "1":
                        await IndexSingleDocument(ragEngine);
                        break;
                    case "2":
                        await IndexDirectory(batchProcessor);
                        break;
                    case "3":
                        await InteractiveQuery(ragEngine, settings.RAG.TopK);
                        break;
                    case "4":
                        await ViewStats(ragEngine);
                        break;
                    case "5":
                        await ViewMetrics(ragLogger);
                        break;
                    case "6":
                        await ManageDocuments(ragEngine);
                        break;
                    case "7":
                        await ClearCache(serviceProvider.GetService<ICacheService>());
                        break;
                    case "8":
                        Console.WriteLine("\n👋 Goodbye!");
                        return;
                    default:
                        Console.WriteLine("❌ Invalid choice. Please try again.\n");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error: {ex.Message}\n");
                ragLogger?.LogError("CLI Operation", ex);
            }
        }
    }

    static void RegisterServices(IServiceCollection services, RagPipelineSettings settings)
    {
        // Semantic Kernel
        services.AddSingleton<Kernel>(sp =>
        {
            var builder = Kernel.CreateBuilder();
            if (settings.AzureOpenAI.Enabled)
            {
                builder.AddAzureOpenAIChatCompletion(
                    settings.AzureOpenAI.ChatDeployment,
                    settings.AzureOpenAI.Endpoint,
                    settings.AzureOpenAI.ApiKey);
            }
            else
            {
                builder.AddOpenAIChatCompletion(settings.OpenAI.ChatModel, settings.OpenAI.ApiKey);
            }
            return builder.Build();
        });

        services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
        {
            if (settings.AzureOpenAI.Enabled)
            {
                return new AzureOpenAITextEmbeddingGenerationService(
                    settings.AzureOpenAI.EmbeddingDeployment,
                    settings.AzureOpenAI.Endpoint,
                    settings.AzureOpenAI.ApiKey);
            }
            return new OpenAITextEmbeddingGenerationService(settings.OpenAI.EmbeddingModel, settings.OpenAI.ApiKey);
        });

        // Core services
        services.AddSingleton<DocumentProcessor>(sp =>
            new DocumentProcessor(settings.RAG.ChunkSize, settings.RAG.ChunkOverlap));
        services.AddSingleton<InMemoryVectorStore>();

        services.AddSingleton<ICacheService>(sp =>
        {
            var cache = sp.GetRequiredService<IMemoryCache>();
            return new CacheService(cache, settings.Cache.MaxCacheSize);
        });

        services.AddSingleton<IResilienceService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ResilienceService>>();
            return new ResilienceService(logger, settings.OpenAI.MaxRetries);
        });

        services.AddSingleton<IRagLogger>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RagLogger>>();
            return new RagLogger(logger, settings.Logging.LogQueryMetrics);
        });

        services.AddSingleton<RagEngine>(sp =>
        {
            var kernel = sp.GetRequiredService<Kernel>();
            var embeddingGen = sp.GetRequiredService<ITextEmbeddingGenerationService>();
            var vectorStore = sp.GetRequiredService<InMemoryVectorStore>();
            var docProcessor = sp.GetRequiredService<DocumentProcessor>();
            var ragLogger = sp.GetRequiredService<IRagLogger>();
            var cacheService = sp.GetRequiredService<ICacheService>();
            var resilienceService = sp.GetRequiredService<IResilienceService>();

            return new RagEngine(kernel, embeddingGen, vectorStore, docProcessor,
                ragLogger, cacheService, resilienceService, settings.RAG.SimilarityThreshold);
        });

        services.AddSingleton<BatchProcessor>(sp =>
        {
            var engine = sp.GetRequiredService<RagEngine>();
            var logger = sp.GetRequiredService<IRagLogger>();
            return new BatchProcessor(engine, logger);
        });
    }

    static void DisplayMenu()
    {
        Console.WriteLine("\n" + new string('═', 60));
        Console.WriteLine("                    MAIN MENU");
        Console.WriteLine(new string('═', 60));
        Console.WriteLine("  1. Index Single Document");
        Console.WriteLine("  2. Index Directory (Batch)");
        Console.WriteLine("  3. Query (Interactive Q&A)");
        Console.WriteLine("  4. View Statistics");
        Console.WriteLine("  5. View Metrics");
        Console.WriteLine("  6. Manage Documents");
        Console.WriteLine("  7. Clear Cache");
        Console.WriteLine("  8. Exit");
        Console.WriteLine(new string('═', 60));
        Console.Write("\nSelect option (1-8): ");
    }

    static async Task IndexSingleDocument(RagEngine ragEngine)
    {
        Console.WriteLine("\n📄 Index Single Document");
        Console.WriteLine(new string('-', 60));

        Console.Write("Document name/source: ");
        var source = Console.ReadLine();

        Console.WriteLine("Enter content (type 'END' on a new line to finish):");
        var lines = new List<string>();
        string? line;
        while ((line = Console.ReadLine()) != "END")
        {
            lines.Add(line ?? string.Empty);
        }
        var content = string.Join("\n", lines);

        Console.WriteLine("\n⏳ Indexing...");
        var result = await ragEngine.IndexDocumentAsync(source ?? "unknown", content);

        if (result.Success)
        {
            Console.WriteLine($"✅ Success! Created {result.ChunksCreated} chunks in {result.ProcessingTimeMs:F2}ms");
        }
        else
        {
            Console.WriteLine($"❌ Failed: {result.Error}");
        }
    }

    static async Task IndexDirectory(BatchProcessor batchProcessor)
    {
        Console.WriteLine("\n📁 Index Directory (Batch)");
        Console.WriteLine(new string('-', 60));

        Console.Write("Directory path: ");
        var path = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Console.WriteLine("❌ Directory not found");
            return;
        }

        Console.Write("Search pattern (*.txt): ");
        var pattern = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "*.txt";

        Console.WriteLine("\n⏳ Processing...");

        var progress = new Progress<BatchProgress>(p =>
        {
            Console.Write($"\r  Progress: {p.Completed}/{p.Total} ({p.PercentComplete:F1}%) - {p.CurrentDocument}     ");
        });

        var result = await batchProcessor.IndexDirectoryAsync(path, pattern, progress);

        Console.WriteLine($"\n\n✅ Completed!");
        Console.WriteLine($"   Total: {result.TotalDocuments} | Success: {result.SuccessfulIndexes} | Failed: {result.FailedIndexes}");
        Console.WriteLine($"   Time: {result.TotalProcessingTimeMs:F2}ms");
    }

    static async Task InteractiveQuery(RagEngine ragEngine, int defaultTopK)
    {
        Console.WriteLine("\n💬 Interactive Q&A (type 'back' to return to menu)");
        Console.WriteLine(new string('-', 60));

        while (true)
        {
            Console.Write("\n❓ Question: ");
            var question = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(question) || question.ToLower() == "back")
            {
                break;
            }

            Console.WriteLine("\n⏳ Processing...");
            var result = await ragEngine.QueryAsync(question, defaultTopK);

            if (result.Success)
            {
                Console.WriteLine($"\n📊 Retrieved {result.RetrievedChunks.Count} chunks ({result.ProcessingTimeMs:F2}ms){(result.FromCache ? " [CACHED]" : "")}:");
                for (int i = 0; i < result.RetrievedChunks.Count; i++)
                {
                    var chunk = result.RetrievedChunks[i];
                    Console.WriteLine($"   {i + 1}. [{chunk.Source}] Similarity: {chunk.Similarity:F3}");
                }

                Console.WriteLine($"\n🤖 Answer:\n{result.Answer}\n");
            }
            else
            {
                Console.WriteLine($"❌ Error: {result.Error}");
            }
        }
    }

    static async Task ViewStats(RagEngine ragEngine)
    {
        Console.WriteLine("\n📊 System Statistics");
        Console.WriteLine(new string('-', 60));

        var stats = await ragEngine.GetStatsAsync();

        Console.WriteLine($"  Total Chunks: {stats.TotalChunks}");
        Console.WriteLine($"  Unique Documents: {stats.UniqueDocuments}");
        Console.WriteLine($"  Memory Usage: {FormatBytes(stats.MemoryUsageEstimate)}");

        if (stats.ChunksByDocument.Any())
        {
            Console.WriteLine("\n  Documents:");
            foreach (var doc in stats.ChunksByDocument.OrderByDescending(d => d.Value))
            {
                Console.WriteLine($"    • {doc.Key}: {doc.Value} chunks");
            }
        }
    }

    static async Task ViewMetrics(IRagLogger ragLogger)
    {
        Console.WriteLine("\n📈 Performance Metrics");
        Console.WriteLine(new string('-', 60));

        var metrics = await ragLogger.GetMetricsAsync();

        Console.WriteLine($"  Queries: {metrics.TotalQueries}");
        Console.WriteLine($"  Indexing Operations: {metrics.TotalIndexOperations}");
        Console.WriteLine($"  Errors: {metrics.TotalErrors}");
        Console.WriteLine($"  Avg Query Time: {metrics.AverageQueryTimeMs:F2}ms");
        Console.WriteLine($"  Avg Index Time: {metrics.AverageIndexTimeMs:F2}ms");
        Console.WriteLine($"  Avg Chunks Retrieved: {metrics.AverageChunksRetrieved:F2}");

        if (metrics.RecentErrors.Any())
        {
            Console.WriteLine("\n  Recent Errors:");
            foreach (var error in metrics.RecentErrors.Take(3))
            {
                Console.WriteLine($"    • [{error.Timestamp:HH:mm:ss}] {error.Operation}: {error.Message}");
            }
        }
    }

    static async Task ManageDocuments(RagEngine ragEngine)
    {
        Console.WriteLine("\n📚 Manage Documents");
        Console.WriteLine(new string('-', 60));

        var documents = await ragEngine.GetIndexedDocumentsAsync();

        if (!documents.Any())
        {
            Console.WriteLine("  No documents indexed.");
            return;
        }

        Console.WriteLine("  Indexed Documents:");
        for (int i = 0; i < documents.Count; i++)
        {
            Console.WriteLine($"    {i + 1}. {documents[i]}");
        }

        Console.Write("\nEnter document number to remove (or 0 to cancel): ");
        if (int.TryParse(Console.ReadLine(), out int choice) && choice > 0 && choice <= documents.Count)
        {
            var source = documents[choice - 1];
            var removed = await ragEngine.RemoveDocumentAsync(source);
            Console.WriteLine(removed ? $"✅ Removed '{source}'" : $"❌ Failed to remove '{source}'");
        }
    }

    static async Task ClearCache(ICacheService? cacheService)
    {
        if (cacheService == null)
        {
            Console.WriteLine("❌ Cache service not available");
            return;
        }

        Console.Write("\n⚠️  Clear cache? This will remove all cached embeddings and queries (y/n): ");
        if (Console.ReadLine()?.ToLower() == "y")
        {
            await cacheService.ClearAsync();
            Console.WriteLine("✅ Cache cleared");
        }
    }

    static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
