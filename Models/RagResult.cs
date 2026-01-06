namespace RagPipeline.Models;

public class RagResult
{
    public string Answer { get; set; } = string.Empty;
    public List<RetrievalResult> RetrievedChunks { get; set; } = new();
    public string Query { get; set; } = string.Empty;
}
