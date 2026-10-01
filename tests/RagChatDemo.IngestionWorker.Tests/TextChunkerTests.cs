using RagChatDemo.Shared.Chunking;

namespace RagChatDemo.IngestionWorker.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Chunk_EmptyOrWhitespace_ReturnsNoChunks()
    {
        Assert.Empty(TextChunker.Chunk(""));
        Assert.Empty(TextChunker.Chunk("   \n\n  "));
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var result = TextChunker.Chunk("A short paragraph of text.");

        var chunk = Assert.Single(result);
        Assert.Equal("A short paragraph of text.", chunk);
    }

    [Fact]
    public void Chunk_LongText_SplitsIntoMultipleChunksWithinMaxSize()
    {
        var paragraph = string.Concat(Enumerable.Repeat("word ", 50)); // ~250 chars
        var text = string.Join("\n\n", Enumerable.Repeat(paragraph, 20)); // ~5000 chars total

        var result = TextChunker.Chunk(text, maxChunkSize: 1000, overlapRatio: 0.1);

        Assert.True(result.Count > 1);
        Assert.All(result, c => Assert.True(c.Length <= 1000 + 100));
    }

    [Fact]
    public void Chunk_LongText_ConsecutiveChunksOverlap()
    {
        var paragraph = string.Concat(Enumerable.Repeat("word ", 50));
        var text = string.Join("\n\n", Enumerable.Repeat(paragraph, 20));

        var result = TextChunker.Chunk(text, maxChunkSize: 1000, overlapRatio: 0.1);

        Assert.True(result.Count > 1);
        var tailOfFirst = result[0][^50..];
        Assert.Contains(tailOfFirst[..20], result[1]);
    }

    [Fact]
    public void Chunk_SingleOversizedParagraph_IsHardSplit()
    {
        var text = new string('a', 2500);

        var result = TextChunker.Chunk(text, maxChunkSize: 1000, overlapRatio: 0);

        Assert.Equal(3, result.Count);
        Assert.Equal(1000, result[0].Length);
    }
}
