namespace RagPipeline.Models;

public class RetrievalResult
{
    public string Source { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
