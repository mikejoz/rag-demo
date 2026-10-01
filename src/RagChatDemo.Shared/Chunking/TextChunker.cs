using System.Text;

namespace RagChatDemo.Shared.Chunking;

/// <summary>Paragraph-aware fixed-size chunking with overlap, used to prep text for embedding.</summary>
public static class TextChunker
{
    public const int DefaultMaxChunkSize = 2000;
    public const double DefaultOverlapRatio = 0.1;

    public static IReadOnlyList<string> Chunk(
        string text, int maxChunkSize = DefaultMaxChunkSize, double overlapRatio = DefaultOverlapRatio)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxChunkSize, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapRatio);

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var overlapSize = (int)(maxChunkSize * overlapRatio);
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in SplitIntoParagraphs(text))
        {
            foreach (var piece in SplitOversizedParagraph(paragraph, maxChunkSize))
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > maxChunkSize)
                {
                    chunks.Add(current.ToString().Trim());
                    var overlapText = TakeTail(current.ToString(), overlapSize);
                    current.Clear();
                    current.Append(overlapText);
                }

                if (current.Length > 0)
                {
                    current.Append("\n\n");
                }

                current.Append(piece);
            }
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks;
    }

    private static IEnumerable<string> SplitIntoParagraphs(string text) =>
        text.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> SplitOversizedParagraph(string paragraph, int maxChunkSize)
    {
        if (paragraph.Length <= maxChunkSize)
        {
            yield return paragraph;
            yield break;
        }

        for (var offset = 0; offset < paragraph.Length; offset += maxChunkSize)
        {
            yield return paragraph.Substring(offset, Math.Min(maxChunkSize, paragraph.Length - offset));
        }
    }

    private static string TakeTail(string text, int overlapSize)
    {
        if (overlapSize <= 0 || text.Length <= overlapSize)
        {
            return string.Empty;
        }

        return text[^overlapSize..];
    }
}
