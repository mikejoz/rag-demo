using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using Pgvector.EntityFrameworkCore;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.McpServer.Tools;

[McpServerToolType]
public sealed class SearchKnowledgeBaseTool(RagChatDemoDbContext db, IOllamaClient ollama)
{
    [McpServerTool(Name = "search_knowledge_base")]
    [Description("Searches the knowledge base for chunks of text relevant to a natural language query.")]
    public async Task<SearchKnowledgeBaseResult> SearchKnowledgeBaseAsync(
        [Description("Natural language question or topic to search for.")] string query,
        [Description("Maximum number of chunks to return.")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await ollama.GetEmbeddingAsync(query, cancellationToken);

        var hits = await db.Chunks
            .OrderBy(c => c.Embedding.CosineDistance(queryEmbedding))
            .Take(topK)
            .Select(c => new
            {
                c.Id,
                c.Text,
                c.SourceDocument!.Title,
                c.SourceDocument.SourceUrl,
                Distance = c.Embedding.CosineDistance(queryEmbedding),
            })
            .ToListAsync(cancellationToken);

        var results = hits
            .Select(h => new KnowledgeBaseSearchHit(h.Id, h.Title, h.SourceUrl, 1 - h.Distance, h.Text))
            .ToArray();

        return new SearchKnowledgeBaseResult(results);
    }
}
