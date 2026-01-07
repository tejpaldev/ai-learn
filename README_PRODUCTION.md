# Production-Ready RAG Pipeline

A comprehensive, enterprise-grade **Retrieval-Augmented Generation (RAG)** system built with .NET 8, Semantic Kernel, and OpenAI. This project demonstrates production-ready patterns including resilience, caching, monitoring, and both CLI and API interfaces.

## 🚀 Features

### Core RAG Capabilities
- ✅ **Document Indexing** - Intelligent chunking with overlap for better context preservation
- ✅ **Vector Search** - Cosine similarity-based semantic search
- ✅ **LLM Integration** - OpenAI and Azure OpenAI support
- ✅ **Context-Aware Generation** - Grounded answers with source attribution

### Production Features
- 🛡️ **Error Handling & Resilience** - Retry policies, circuit breakers, graceful degradation
- 💾 **Caching** - Embedding and query result caching for performance
- 📊 **Monitoring & Metrics** - Comprehensive logging and performance tracking
- 🔄 **Batch Processing** - Parallel document indexing with progress tracking
- 🌐 **REST API** - Full-featured API with Swagger documentation
- 🏥 **Health Checks** - Kubernetes-ready liveness and readiness probes
- ⚡ **Rate Limiting** - Configurable request throttling
- 📝 **Structured Logging** - Serilog with console and file outputs

### Advanced Document Processing
- 📄 **Multiple Format Support** - Text, Markdown, Code files
- 🧩 **Smart Chunking** - Paragraph-aware, sentence-based splitting
- 🔍 **Metadata Extraction** - Content type detection and enrichment
- 📚 **Batch Operations** - Directory indexing with progress reporting

## 📋 Prerequisites

- **.NET 8.0 SDK** or later
- **OpenAI API Key** or Azure OpenAI credentials
- **Visual Studio 2022** or VS Code (optional)

## 🛠️ Installation

### 1. Clone the Repository
```bash
git clone <repository-url>
cd "rag pipline"
```

### 2. Configure API Keys

Edit `appsettings.Development.json`:
```json
{
  "OpenAI": {
    "ApiKey": "your-openai-api-key-here",
    "EmbeddingModel": "text-embedding-ada-002",
    "ChatModel": "gpt-3.5-turbo"
  }
}
```

Or use environment variables:
```powershell
$env:OPENAI__APIKEY="your-key-here"
```

For Azure OpenAI:
```json
{
  "AzureOpenAI": {
    "Enabled": true,
    "Endpoint": "https://your-resource.openai.azure.com",
    "ApiKey": "your-azure-key",
    "EmbeddingDeployment": "text-embedding-ada-002",
    "ChatDeployment": "gpt-35-turbo"
  }
}
```

### 3. Restore Dependencies
```powershell
dotnet restore
```

### 4. Build the Project
```powershell
dotnet build
```

## 🎯 Usage

### CLI Mode (Interactive)

```powershell
dotnet run
# or
dotnet run -- --cli
```

**Available Options:**
1. **Index Single Document** - Manually enter document content
2. **Index Directory (Batch)** - Process multiple files in parallel
3. **Query (Interactive Q&A)** - Ask questions about indexed documents
4. **View Statistics** - See system stats and memory usage
5. **View Metrics** - Performance metrics and logs
6. **Manage Documents** - Remove specific documents
7. **Clear Cache** - Reset embedding and query caches
8. **Exit**

### API Mode (Web Server)

```powershell
dotnet run -- --api
```

Access the API:
- **Swagger UI**: http://localhost:5000/swagger
- **Health Check**: http://localhost:5000/api/health
- **API Base**: http://localhost:5000/api

#### API Endpoints

**Query**
```bash
POST /api/rag/query
Content-Type: application/json

{
  "query": "What is machine learning?",
  "topK": 3,
  "similarityThreshold": 0.7
}
```

**Index Document**
```bash
POST /api/rag/index
Content-Type: application/json

{
  "source": "ml_basics.txt",
  "content": "Machine learning is..."
}
```

**Upload File**
```bash
POST /api/rag/upload
Content-Type: multipart/form-data

file: [your-file.txt]
```

**Get Statistics**
```bash
GET /api/rag/stats
```

**List Documents**
```bash
GET /api/rag/documents
```

**Remove Document**
```bash
DELETE /api/rag/documents/{source}
```

**Health Check (Detailed)**
```bash
GET /api/health/detailed
```

## ⚙️ Configuration

### RAG Settings
```json
{
  "RAG": {
    "ChunkSize": 500,              // Characters per chunk
    "ChunkOverlap": 50,             // Overlap between chunks
    "TopK": 3,                      // Number of chunks to retrieve
    "SimilarityThreshold": 0.7,     // Minimum similarity score
    "MaxTokens": 1000,              // Max tokens in LLM response
    "Temperature": 0.7              // LLM creativity (0-1)
  }
}
```

### Caching
```json
{
  "Cache": {
    "Enabled": true,
    "EmbeddingCacheMinutes": 60,    // Cache embeddings for 1 hour
    "QueryCacheMinutes": 30,        // Cache queries for 30 mins
    "MaxCacheSize": 1000            // Max cached items
  }
}
```

### Security & Rate Limiting
```json
{
  "Security": {
    "EnableRateLimiting": true,
    "RateLimitPerMinute": 60,
    "AllowedOrigins": ["*"]
  }
}
```

## 📊 Architecture

```
RagPipeline/
├── Program.cs                      # Application entry point (CLI & API)
├── Models/
│   ├── DocumentChunk.cs            # Document chunk with embedding
│   ├── RagResult.cs                # Query result with context
│   ├── RetrievalResult.cs          # Retrieved chunk with similarity
│   └── Configuration/
│       └── RagPipelineSettings.cs  # Comprehensive configuration
├── Services/
│   ├── RagEngine.cs                # Core RAG orchestration
│   ├── DocumentProcessor.cs        # Text chunking & processing
│   ├── AdvancedDocumentProcessor.cs # Format-specific processing
│   ├── InMemoryVectorStore.cs      # Vector storage & search
│   ├── CacheService.cs             # Caching layer
│   ├── ResilienceService.cs        # Retry & circuit breaker
│   ├── RagLogger.cs                # Metrics & logging
│   └── BatchProcessor.cs           # Parallel batch operations
└── Api/
    ├── ApiStartup.cs               # API configuration & DI
    └── Controllers/
        ├── RagController.cs        # Main RAG endpoints
        └── HealthController.cs     # Health check endpoints
```

## 🔧 Advanced Features

### Resilience Patterns
- **Exponential Backoff**: Automatic retry with increasing delays
- **Circuit Breaker**: Fail fast when service is unavailable
- **Timeout Handling**: Graceful timeout management

### Performance Optimization
- **Embedding Caching**: Avoid regenerating embeddings
- **Query Caching**: Store recent query results
- **Parallel Processing**: Batch indexing with configurable parallelism
- **Memory Management**: Automatic cache eviction

### Monitoring
- **Query Metrics**: Track average response time, chunk retrieval
- **Error Tracking**: Comprehensive error logging with context
- **Performance Stats**: Memory usage, cache hit rates
- **Health Endpoints**: Monitor system health in real-time

## 📈 Performance Tips

1. **Adjust Chunk Size**: Smaller chunks (200-300) for precise retrieval, larger (500-1000) for broader context
2. **Enable Caching**: Reduces API calls by 50-80%
3. **Tune TopK**: Start with 3-5, increase for complex queries
4. **Batch Operations**: Use batch indexing for large document sets
5. **Monitor Metrics**: Use `/api/rag/metrics` to identify bottlenecks

## 🐛 Troubleshooting

### "No API key found"
- Set `OpenAI:ApiKey` in `appsettings.Development.json`
- Or use environment variable: `OPENAI__APIKEY`

### High Memory Usage
- Reduce `Cache.MaxCacheSize`
- Clear cache periodically
- Process documents in smaller batches

### Slow Queries
- Enable caching
- Reduce `TopK` value
- Check network latency to OpenAI

### Rate Limit Errors
- Increase `OpenAI.MaxRetries`
- Implement exponential backoff (already built-in)
- Consider batch processing during off-peak hours

## 🧪 Testing

Run the application and test with sample queries:

```
Q: What is artificial intelligence?
Q: Explain machine learning concepts
Q: What is RAG and how does it work?
```

## 📦 Deployment

### Docker
```powershell
docker build -t ragpipeline:latest .
docker run -p 5000:5000 -e OPENAI__APIKEY=your-key ragpipeline:latest
```

### Azure App Service
1. Build for release: `dotnet publish -c Release`
2. Deploy to Azure App Service
3. Set environment variables in Azure Portal
4. Configure health check endpoint: `/api/health/ready`

### Kubernetes
```powershell
kubectl apply -f kubernetes-deployment.yaml
```

## 📄 License

MIT License - See LICENSE file for details

## 🤝 Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Open a Pull Request

## 📞 Support

For issues and questions:
- Create an issue in the repository
- Check existing documentation
- Review configuration examples

## 🎓 Learning Resources

- [Semantic Kernel Documentation](https://learn.microsoft.com/en-us/semantic-kernel/)
- [OpenAI API Reference](https://platform.openai.com/docs/api-reference)
- [RAG Systems Guide](https://www.pinecone.io/learn/retrieval-augmented-generation/)

---

**Built with ❤️ using .NET 8 and Semantic Kernel**
