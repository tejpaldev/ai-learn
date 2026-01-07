using RagPipeline.Models;
using System.Text.RegularExpressions;

namespace RagPipeline.Services;

public class AdvancedDocumentProcessor
{
    private readonly DocumentProcessor _baseProcessor;

    public AdvancedDocumentProcessor(DocumentProcessor baseProcessor)
    {
        _baseProcessor = baseProcessor;
    }

    /// <summary>
    /// Process Markdown documents with structure awareness
    /// </summary>
    public List<DocumentChunk> ProcessMarkdown(string source, string content)
    {
        var chunks = new List<DocumentChunk>();
        var sections = SplitMarkdownSections(content);
        var chunkIndex = 0;

        foreach (var section in sections)
        {
            var sectionChunks = _baseProcessor.ChunkDocument(source, section.Content);

            foreach (var chunk in sectionChunks)
            {
                chunk.ChunkIndex = chunkIndex++;
                chunk.Metadata["section_title"] = section.Title;
                chunk.Metadata["section_level"] = section.Level.ToString();
                chunk.Metadata["content_type"] = "markdown";
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    /// <summary>
    /// Process code files with syntax awareness
    /// </summary>
    public List<DocumentChunk> ProcessCode(string source, string content, string language)
    {
        var chunks = new List<DocumentChunk>();
        var functions = ExtractCodeBlocks(content, language);
        var chunkIndex = 0;

        foreach (var function in functions)
        {
            var chunk = new DocumentChunk
            {
                Source = source,
                Content = function.Content,
                ChunkIndex = chunkIndex++,
                Metadata = new Dictionary<string, string>
                {
                    ["content_type"] = "code",
                    ["language"] = language,
                    ["function_name"] = function.Name,
                    ["start_line"] = function.StartLine.ToString(),
                    ["end_line"] = function.EndLine.ToString()
                }
            };
            chunks.Add(chunk);
        }

        // If no functions found, fall back to standard chunking
        if (!chunks.Any())
        {
            chunks = _baseProcessor.ChunkDocument(source, content);
            foreach (var chunk in chunks)
            {
                chunk.Metadata["content_type"] = "code";
                chunk.Metadata["language"] = language;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Process structured data (CSV, JSON)
    /// </summary>
    public List<DocumentChunk> ProcessStructuredData(string source, string content, string format)
    {
        var chunks = _baseProcessor.ChunkDocument(source, content);

        foreach (var chunk in chunks)
        {
            chunk.Metadata["content_type"] = "structured_data";
            chunk.Metadata["format"] = format;
        }

        return chunks;
    }

    /// <summary>
    /// Extract metadata from document
    /// </summary>
    public DocumentMetadata ExtractMetadata(string content, string fileExtension)
    {
        var metadata = new DocumentMetadata
        {
            FileExtension = fileExtension,
            CharacterCount = content.Length,
            WordCount = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
            LineCount = content.Split('\n').Length
        };

        // Detect language
        if (fileExtension == ".md" || fileExtension == ".markdown")
        {
            metadata.ContentType = "markdown";
            metadata.Headers = ExtractMarkdownHeaders(content);
        }
        else if (IsCodeFile(fileExtension))
        {
            metadata.ContentType = "code";
            metadata.ProgrammingLanguage = GetLanguageFromExtension(fileExtension);
        }
        else if (fileExtension == ".txt")
        {
            metadata.ContentType = "text";
        }

        return metadata;
    }

    private List<MarkdownSection> SplitMarkdownSections(string content)
    {
        var sections = new List<MarkdownSection>();
        var lines = content.Split('\n');
        var currentSection = new MarkdownSection { Title = "Introduction", Level = 0 };
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");

            if (headerMatch.Success)
            {
                // Save previous section
                if (currentContent.Any())
                {
                    currentSection.Content = string.Join("\n", currentContent);
                    sections.Add(currentSection);
                    currentContent.Clear();
                }

                // Start new section
                var level = headerMatch.Groups[1].Value.Length;
                var title = headerMatch.Groups[2].Value;
                currentSection = new MarkdownSection
                {
                    Title = title,
                    Level = level,
                    Content = line
                };
            }
            else
            {
                currentContent.Add(line);
            }
        }

        // Add last section
        if (currentContent.Any())
        {
            currentSection.Content = string.Join("\n", currentContent);
            sections.Add(currentSection);
        }

        return sections;
    }

    private List<CodeBlock> ExtractCodeBlocks(string content, string language)
    {
        var blocks = new List<CodeBlock>();

        // Simple function extraction for common languages
        if (language == "csharp" || language == "cs")
        {
            var functionPattern = @"(public|private|protected|internal)?\s*(static)?\s*\w+\s+(\w+)\s*\([^)]*\)\s*\{";
            ExtractBlocksWithPattern(content, functionPattern, blocks);
        }
        else if (language == "python" || language == "py")
        {
            var functionPattern = @"def\s+(\w+)\s*\([^)]*\):";
            ExtractBlocksWithPattern(content, functionPattern, blocks);
        }

        return blocks;
    }

    private void ExtractBlocksWithPattern(string content, string pattern, List<CodeBlock> blocks)
    {
        var lines = content.Split('\n');
        var matches = Regex.Matches(content, pattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            var startLine = content.Substring(0, match.Index).Count(c => c == '\n');
            var functionName = match.Groups.Count > 3 ? match.Groups[3].Value : match.Groups[1].Value;

            // Find matching closing brace (simplified)
            var endLine = FindBlockEnd(lines, startLine);

            blocks.Add(new CodeBlock
            {
                Name = functionName,
                StartLine = startLine,
                EndLine = endLine,
                Content = string.Join("\n", lines.Skip(startLine).Take(endLine - startLine + 1))
            });
        }
    }

    private int FindBlockEnd(string[] lines, int startLine)
    {
        var braceCount = 0;
        var started = false;

        for (int i = startLine; i < lines.Length; i++)
        {
            foreach (var c in lines[i])
            {
                if (c == '{') { braceCount++; started = true; }
                if (c == '}') braceCount--;
            }

            if (started && braceCount == 0)
                return i;
        }

        return Math.Min(startLine + 50, lines.Length - 1);
    }

    private List<string> ExtractMarkdownHeaders(string content)
    {
        return Regex.Matches(content, @"^#{1,6}\s+(.+)$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private bool IsCodeFile(string extension)
    {
        var codeExtensions = new[] { ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".c", ".go", ".rs", ".rb" };
        return codeExtensions.Contains(extension.ToLower());
    }

    private string GetLanguageFromExtension(string extension)
    {
        return extension.ToLower() switch
        {
            ".cs" => "csharp",
            ".py" => "python",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".java" => "java",
            ".cpp" or ".cc" => "cpp",
            ".c" => "c",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            _ => "unknown"
        };
    }
}

public class MarkdownSection
{
    public string Title { get; set; } = string.Empty;
    public int Level { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class CodeBlock
{
    public string Name { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class DocumentMetadata
{
    public string FileExtension { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? ProgrammingLanguage { get; set; }
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public int LineCount { get; set; }
    public List<string> Headers { get; set; } = new();
}
