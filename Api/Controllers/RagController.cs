using Microsoft.AspNetCore.Mvc;
using RagPipeline.Services;
using RagPipeline.Models;
using System.ComponentModel.DataAnnotations;

namespace RagPipeline.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RagController : ControllerBase
{
    private readonly RagEngine _ragEngine;
    private readonly IRagLogger _logger;

    public RagController(RagEngine ragEngine, IRagLogger logger)
    {
        _ragEngine = ragEngine;
        _logger = logger;
    }

    /// <summary>
    /// Query the RAG system with a question
    /// </summary>
    [HttpPost("query")]
    [ProducesResponseType(typeof(RagResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RagResult>> Query([FromBody] QueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "Query cannot be empty" });
        }

        var result = await _ragEngine.QueryAsync(
            request.Query,
            request.TopK ?? 3,
            request.SimilarityThreshold
        );

        return Ok(result);
    }

    /// <summary>
    /// Index a new document into the RAG system
    /// </summary>
    [HttpPost("index")]
    [ProducesResponseType(typeof(IndexingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IndexingResult>> IndexDocument([FromBody] IndexRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
        {
            return BadRequest(new { error = "Source cannot be empty" });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { error = "Content cannot be empty" });
        }

        var result = await _ragEngine.IndexDocumentAsync(request.Source, request.Content);
        return Ok(result);
    }

    /// <summary>
    /// Upload and index a document file
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(IndexingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IndexingResult>> UploadDocument(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded" });
        }

        if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only .txt files are supported" });
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        var result = await _ragEngine.IndexDocumentAsync(file.FileName, content);
        return Ok(result);
    }

    /// <summary>
    /// Get statistics about the indexed documents
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(VectorStoreStats), StatusCodes.Status200OK)]
    public async Task<ActionResult<VectorStoreStats>> GetStats()
    {
        var stats = await _ragEngine.GetStatsAsync();
        return Ok(stats);
    }

    /// <summary>
    /// Get list of all indexed documents
    /// </summary>
    [HttpGet("documents")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<string>>> GetDocuments()
    {
        var documents = await _ragEngine.GetIndexedDocumentsAsync();
        return Ok(documents);
    }

    /// <summary>
    /// Remove a specific document from the index
    /// </summary>
    [HttpDelete("documents/{source}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveDocument(string source)
    {
        var removed = await _ragEngine.RemoveDocumentAsync(source);
        if (!removed)
        {
            return NotFound(new { error = $"Document '{source}' not found" });
        }

        return Ok(new { message = $"Document '{source}' removed successfully" });
    }

    /// <summary>
    /// Clear all indexed documents
    /// </summary>
    [HttpDelete("clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> ClearIndex()
    {
        await _ragEngine.ClearIndexAsync();
        return Ok(new { message = "Index cleared successfully" });
    }

    /// <summary>
    /// Get system metrics and logs
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(LogMetrics), StatusCodes.Status200OK)]
    public async Task<ActionResult<LogMetrics>> GetMetrics()
    {
        var metrics = await _logger.GetMetricsAsync();
        return Ok(metrics);
    }
}

public class QueryRequest
{
    [Required]
    public string Query { get; set; } = string.Empty;

    public int? TopK { get; set; }

    public double? SimilarityThreshold { get; set; }
}

public class IndexRequest
{
    [Required]
    public string Source { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;
}
