# .NET RAG Pipeline Example

A complete implementation of a Retrieval-Augmented Generation (RAG) pipeline using .NET and Semantic Kernel.

## Features

- 📄 **Document Processing**: Automatic chunking with configurable size and overlap
- 🔢 **Vector Embeddings**: OpenAI embeddings for semantic search
- 💾 **Vector Storage**: In-memory vector store with cosine similarity search
- 🤖 **RAG Generation**: Context-aware answers using retrieved documents
- ⚙️ **Configurable**: Easy configuration via appsettings.json

## Architecture

```
┌─────────────┐
│  Documents  │
└──────┬──────┘
       │
       ▼
┌──────────────────┐
│ Document Chunker │
└──────┬───────────┘
       │
       ▼
┌──────────────────┐
│ Embed Generator  │
└──────┬───────────┘
       │
       ▼
┌──────────────────┐
│  Vector Store    │
└──────┬───────────┘
       │
       ▼
┌──────────────────┐      ┌─────────┐
│  Query Engine    │◄─────┤  Query  │
└──────┬───────────┘      └─────────┘
       │
       ▼
┌──────────────────┐
│  LLM Generator   │
└──────┬───────────┘
       │
       ▼
    Answer
```

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- OpenAI API Key

### Configuration

1. Open `appsettings.json`
2. Add your OpenAI API key:

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-key-here"
  }
}
```

Or set as environment variable:
```bash
set OPENAI__APIKEY=sk-your-key-here
```

### Running

```bash
dotnet restore
dotnet run
```

### Adding Custom Documents

Create a `documents` folder and add `.txt` files:

```
documents/
  ├── document1.txt
  ├── document2.txt
  └── document3.txt
```

## Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `RAG:ChunkSize` | Maximum characters per chunk | 500 |
| `RAG:ChunkOverlap` | Overlapping characters between chunks | 50 |
| `RAG:TopK` | Number of chunks to retrieve | 3 |
| `OpenAI:EmbeddingModel` | Model for embeddings | text-embedding-ada-002 |
| `OpenAI:ChatModel` | Model for generation | gpt-3.5-turbo |

## Components

### DocumentProcessor
Splits documents into chunks with overlap for better context preservation.

### InMemoryVectorStore
Stores embeddings and performs similarity search using cosine similarity.

### RagEngine
Orchestrates the entire RAG pipeline:
1. Document indexing
2. Query processing
3. Context retrieval
4. Answer generation

## Example Queries

```
Question: What is machine learning?
Question: How does RAG work?
Question: What are types of AI?
```

## Extending

### Using Azure OpenAI

Replace the OpenAI services with Azure OpenAI:

```csharp
.AddAzureOpenAIChatCompletion(
    deploymentName: "your-deployment",
    endpoint: "https://your-resource.openai.azure.com/",
    apiKey: "your-key"
)
```

### Using Persistent Storage

Replace `InMemoryVectorStore` with:
- Azure AI Search
- PostgreSQL with pgvector
- Pinecone
- Qdrant
- Weaviate

### Adding Metadata Filtering

Enhance `DocumentChunk.Metadata` and implement filtering in `SearchAsync`.

## License

MIT License - feel free to use in your projects!
