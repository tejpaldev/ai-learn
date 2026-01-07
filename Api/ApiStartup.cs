using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using RagPipeline.Services;
using RagPipeline.Models.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace RagPipeline.Api;

public class ApiStartup
{
    public IConfiguration Configuration { get; }

    public ApiStartup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Configuration
        var settings = new RagPipelineSettings();
        Configuration.Bind(settings);
        services.AddSingleton(settings);

        // Controllers
        services.AddControllers();

        // CORS
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader();
            });
        });

        // Rate Limiting
        if (settings.Security.EnableRateLimiting)
        {
            services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = settings.Security.RateLimitPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        }));
            });
        }

        // Swagger/OpenAPI
        if (settings.Api.EnableSwagger)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "RAG Pipeline API",
                    Version = "v1",
                    Description = "Production-ready Retrieval-Augmented Generation API",
                    Contact = new OpenApiContact
                    {
                        Name = "RAG Pipeline",
                        Email = "support@example.com"
                    }
                });

                // Add XML comments if available
                var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }
            });
        }

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        // Caching
        services.AddMemoryCache();
        services.AddSingleton<ICacheService>(sp =>
        {
            var cache = sp.GetRequiredService<IMemoryCache>();
            return new CacheService(cache, settings.Cache.MaxCacheSize);
        });

        // Resilience
        services.AddSingleton<IResilienceService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ResilienceService>>();
            return new ResilienceService(logger, settings.OpenAI.MaxRetries);
        });

        // RAG Logger
        services.AddSingleton<IRagLogger>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RagLogger>>();
            return new RagLogger(logger, settings.Logging.LogQueryMetrics);
        });

        // Semantic Kernel & OpenAI
        services.AddSingleton<Kernel>(sp =>
        {
            var builder = Kernel.CreateBuilder();

            if (settings.AzureOpenAI.Enabled)
            {
                builder.AddAzureOpenAIChatCompletion(
                    settings.AzureOpenAI.ChatDeployment,
                    settings.AzureOpenAI.Endpoint,
                    settings.AzureOpenAI.ApiKey
                );
            }
            else
            {
                builder.AddOpenAIChatCompletion(
                    settings.OpenAI.ChatModel,
                    settings.OpenAI.ApiKey
                );
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
                    settings.AzureOpenAI.ApiKey
                );
            }
            else
            {
                return new OpenAITextEmbeddingGenerationService(
                    settings.OpenAI.EmbeddingModel,
                    settings.OpenAI.ApiKey
                );
            }
        });

        // RAG Components
        services.AddSingleton<DocumentProcessor>(sp =>
            new DocumentProcessor(settings.RAG.ChunkSize, settings.RAG.ChunkOverlap));

        services.AddSingleton<InMemoryVectorStore>();

        services.AddSingleton<RagEngine>(sp =>
        {
            var kernel = sp.GetRequiredService<Kernel>();
            var embeddingGen = sp.GetRequiredService<ITextEmbeddingGenerationService>();
            var vectorStore = sp.GetRequiredService<InMemoryVectorStore>();
            var docProcessor = sp.GetRequiredService<DocumentProcessor>();
            var ragLogger = sp.GetRequiredService<IRagLogger>();
            var cacheService = sp.GetRequiredService<ICacheService>();
            var resilienceService = sp.GetRequiredService<IResilienceService>();

            return new RagEngine(
                kernel,
                embeddingGen,
                vectorStore,
                docProcessor,
                ragLogger,
                cacheService,
                resilienceService,
                settings.RAG.SimilarityThreshold
            );
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        var settings = app.ApplicationServices.GetRequiredService<RagPipelineSettings>();

        if (settings.Api.EnableSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "RAG Pipeline API v1");
                c.RoutePrefix = string.Empty; // Serve Swagger UI at root
            });
        }

        app.UseRouting();
        app.UseCors();

        if (settings.Security.EnableRateLimiting)
        {
            app.UseRateLimiter();
        }

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}
