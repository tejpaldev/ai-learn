using RagPipeline.Models;
using System.Text.RegularExpressions;

namespace RagPipeline.Services;

/// <summary>
/// DOCUMENT PROCESSOR: The Chunking Expert
/// 
/// WHY DO WE NEED CHUNKING?
/// Imagine you have a 50-page manual and someone asks "How do I reset the password?"
/// - Without chunking: Feed entire 50 pages to AI → expensive, slow, may hit token limits
/// - With chunking: Break into paragraphs → only feed relevant 2-3 paragraphs → fast, cheap, accurate
/// 
/// CHUNKING IS CRUCIAL FOR RAG BECAUSE:
/// 1. LLMs have token limits (e.g., GPT-3.5 = 4K tokens, GPT-4 = 8K-32K tokens)
/// 2. Smaller chunks = more precise retrieval (find exact paragraph, not entire document)
/// 3. Better context quality (focused information vs. noise)
/// 4. Cost optimization (only embed/process what's needed)
/// 
/// THINK OF IT LIKE:
/// A book's table of contents + index → you quickly find the exact page you need
/// Instead of reading the entire book every time
/// </summary>
public class DocumentProcessor
{
    // CHUNK SIZE: Maximum characters per chunk (default: 500)
    // 
    // WHY 500? Balance between:
    // - Too small (100): Context is fragmented, may lose meaning
    // - Too large (2000): Too much irrelevant info, expensive embeddings
    // - Just right (500): ~1 paragraph = complete thought
    //
    // RULE OF THUMB:
    // 1 token ≈ 4 characters
    // 500 chars ≈ 125 tokens ≈ $0.0000125 to embed
    private readonly int _chunkSize;

    // CHUNK OVERLAP: Characters shared between consecutive chunks (default: 50)
    // 
    // WHY OVERLAP?
    // Prevents losing context at chunk boundaries
    // 
    // EXAMPLE WITHOUT OVERLAP:
    // Chunk 1: "The machine learning model"
    // Chunk 2: "requires training data and compute"
    // → Split mid-sentence! Loses meaning
    // 
    // EXAMPLE WITH 50-CHAR OVERLAP:
    // Chunk 1: "The machine learning model requires training"
    // Chunk 2: "requires training data and compute resources"
    // → Overlap preserves sentence structure
    // 
    // TRADE-OFF:
    // More overlap = better context preservation BUT more storage & cost
    private readonly int _chunkOverlap;

    public DocumentProcessor(int chunkSize = 500, int chunkOverlap = 50)
    {
        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    /// <summary>
    /// SMART CHUNKING: Break document into semantic units
    /// 
    /// NOT JUST SPLITTING AT 500 CHARACTERS!
    /// We respect document structure:
    /// - Split at paragraphs (natural boundaries)
    /// - Split at sentences (if paragraph too large)
    /// - Never split mid-word
    /// 
    /// THIS IS WHAT MAKES RAG WORK WELL:
    /// Bad chunking: "The capital of France is Pa[CHUNK]ris and it's known for..."
    /// Good chunking: "The capital of France is Paris and it's known for the Eiffel Tower."
    /// </summary>
    public List<DocumentChunk> ChunkDocument(string source, string content)
    {
        // List to store all chunks we create
        var chunks = new List<DocumentChunk>();

        // ═══════════════════════════════════════════════════════════════
        // STEP 1: NORMALIZE CONTENT - Clean up the text
        // ═══════════════════════════════════════════════════════════════
        // WHY? Raw documents have inconsistent formatting:
        // - Windows line breaks: \r\n
        // - Unix line breaks: \n
        // - Multiple spaces, tabs, etc.
        // 
        // Normalization ensures consistent processing
        content = NormalizeContent(content);

        // Edge case: Don't process empty documents
        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        // ═══════════════════════════════════════════════════════════════
        // STEP 2: SEMANTIC CHUNKING - Split by paragraphs first
        // ═══════════════════════════════════════════════════════════════
        // SEMANTIC = respecting meaning boundaries
        // Paragraphs usually contain complete thoughts
        // Better than arbitrary character cuts!
        var paragraphs = SplitIntoParagraphs(content);

        // Accumulate paragraphs until we hit chunk size
        var currentChunk = new List<string>();     // Paragraphs in current chunk
        var currentLength = 0;                      // Total characters in current chunk
        var chunkIndex = 0;                         // Counter for chunk numbering

        // ═══════════════════════════════════════════════════════════════
        // STEP 3: BUILD CHUNKS - Combine paragraphs intelligently
        // ═══════════════════════════════════════════════════════════════
        foreach (var paragraph in paragraphs)
        {
            var paraLength = paragraph.Length;

            // ─────────────────────────────────────────────────────────────
            // DECISION POINT: Would adding this paragraph exceed chunk size?
            // ─────────────────────────────────────────────────────────────
            if (currentLength + paraLength > _chunkSize && currentChunk.Any())
            {
                // YES, it would exceed → Create chunk from what we have so far

                // Join paragraphs with double newlines (preserve paragraph breaks)
                var chunkContent = string.Join("\n\n", currentChunk).Trim();

                // Only create chunk if it has content
                if (!string.IsNullOrWhiteSpace(chunkContent))
                {
                    chunks.Add(CreateChunk(source, chunkContent, chunkIndex++));
                }

                // ─────────────────────────────────────────────────────────
                // OVERLAP IMPLEMENTATION
                // ─────────────────────────────────────────────────────────
                // Keep last paragraph from previous chunk in new chunk
                // This ensures context continuity across chunk boundaries
                // 
                // VISUAL:
                // Chunk 1: [Para A, Para B, Para C]
                // Chunk 2: [Para C, Para D, Para E] ← Para C is overlap
                if (_chunkOverlap > 0 && currentChunk.Any())
                {
                    // Start new chunk with last paragraph from previous chunk
                    var overlapText = currentChunk.Last();
                    currentChunk = new List<string> { overlapText };
                    currentLength = overlapText.Length;
                }
                else
                {
                    // No overlap - start fresh
                    currentChunk.Clear();
                    currentLength = 0;
                }
            }

            // Add current paragraph to accumulator
            currentChunk.Add(paragraph);
            currentLength += paraLength;
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 4: DON'T FORGET THE LAST CHUNK!
        // ═══════════════════════════════════════════════════════════════
        // After loop ends, we might have paragraphs in currentChunk
        // that haven't been converted to a chunk yet
        if (currentChunk.Any())
        {
            var chunkContent = string.Join("\n\n", currentChunk).Trim();
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                chunks.Add(CreateChunk(source, chunkContent, chunkIndex));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // EDGE CASE: Very small documents
        // ═══════════════════════════════════════════════════════════════
        // If document is smaller than chunk size, we get zero chunks
        // Solution: Create single chunk with entire content
        if (!chunks.Any() && !string.IsNullOrWhiteSpace(content))
        {
            chunks.Add(CreateChunk(source, content, 0));
        }

        return chunks;
    }

    private string NormalizeContent(string content)
    {
        // Remove excessive whitespace
        content = Regex.Replace(content, @"\r\n|\r|\n", "\n");
        content = Regex.Replace(content, @"\n{3,}", "\n\n");
        content = Regex.Replace(content, @"[ \t]+", " ");
        return content.Trim();
    }

    private List<string> SplitIntoParagraphs(string content)
    {
        // Split by double newlines (paragraphs)
        var paragraphs = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        // If paragraphs are too large, split by sentences
        var result = new List<string>();
        foreach (var para in paragraphs)
        {
            if (para.Length <= _chunkSize)
            {
                result.Add(para);
            }
            else
            {
                result.AddRange(SplitIntoSentences(para));
            }
        }

        return result;
    }

    private List<string> SplitIntoSentences(string text)
    {
        // Split by common sentence endings
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return sentences;
    }

    private DocumentChunk CreateChunk(string source, string content, int index)
    {
        return new DocumentChunk
        {
            Source = source,
            Content = content,
            ChunkIndex = index,
            Metadata = new Dictionary<string, string>
            {
                ["length"] = content.Length.ToString(),
                ["created"] = DateTime.UtcNow.ToString("O")
            }
        };
    }

    public DocumentMetrics AnalyzeDocument(string content)
    {
        return new DocumentMetrics
        {
            TotalCharacters = content.Length,
            TotalWords = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
            EstimatedChunks = (int)Math.Ceiling((double)content.Length / _chunkSize)
        };
    }
}

public class DocumentMetrics
{
    public int TotalCharacters { get; set; }
    public int TotalWords { get; set; }
    public int EstimatedChunks { get; set; }
}
