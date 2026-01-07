namespace RagPipeline.Models;

public class RagResult
{
    public string Answer { get; set; } = string.Empty;
    public List<RetrievalResult> RetrievedChunks { get; set; } = new();
    public string Query { get; set; } = string.Empty;
    public double ProcessingTimeMs { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
    public bool FromCache { get; set; } = false;
}
