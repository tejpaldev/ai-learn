namespace RagPipeline.Models;

public class RetrievalResult
{
    public string Source { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Similarity { get; set; }
}
