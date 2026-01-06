using RagPipeline.Models;

namespace RagPipeline.Services;

public class DocumentProcessor
{
    private readonly int _chunkSize;
    private readonly int _chunkOverlap;

    public DocumentProcessor(int chunkSize = 500, int chunkOverlap = 50)
    {
        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    public List<DocumentChunk> ChunkDocument(string source, string content)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = SplitIntoSentences(content);
        
        var currentChunk = new List<string>();
        var currentLength = 0;
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            var sentenceLength = sentence.Length;

            if (currentLength + sentenceLength > _chunkSize && currentChunk.Count > 0)
            {
                // Create chunk
                var chunkContent = string.Join(" ", currentChunk);
                chunks.Add(new DocumentChunk
                {
                    Source = source,
                    Content = chunkContent.Trim(),
                    ChunkIndex = chunkIndex++
                });

                // Keep overlap
                var overlapText = chunkContent.Substring(Math.Max(0, chunkContent.Length - _chunkOverlap));
                currentChunk.Clear();
                currentChunk.Add(overlapText);
                currentLength = overlapText.Length;
            }

            currentChunk.Add(sentence);
            currentLength += sentenceLength;
        }

        // Add remaining chunk
        if (currentChunk.Count > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Source = source,
                Content = string.Join(" ", currentChunk).Trim(),
                ChunkIndex = chunkIndex
            });
        }

        return chunks;
    }

    private List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting - can be enhanced with better NLP
        var sentences = new List<string>();
        var current = "";

        foreach (var c in text)
        {
            current += c;
            
            if (c == '.' || c == '!' || c == '?' || c == '\n')
            {
                var trimmed = current.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    sentences.Add(trimmed);
                }
                current = "";
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            sentences.Add(current.Trim());
        }

        return sentences;
    }
}
