namespace RagPipeline.Models;

public class DocumentChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Source { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ReadOnlyMemory<float> Embedding { get; set; }
    public int ChunkIndex { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
