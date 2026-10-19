# MCP Tool Contracts

## `search_knowledge_base`

**Input**:
```json
{
  "query": "string, required - natural language question or topic",
  "top_k": "integer, optional, default 5 - max number of chunks to return"
}
```

**Output**:
```json
{
  "results": [
    {
      "chunk_id": "guid",
      "document_title": "string",
      "source_url": "string",
      "score": "number (cosine similarity, 0-1)",
      "text": "string (chunk text)"
    }
  ]
}
```

## `create_support_ticket`

**Input**:
```json
{
  "summary": "string, required",
  "details": "string, required"
}
```

**Output** (stubbed — no real ticketing system):
```json
{
  "ticket_id": "string (e.g. \"TICKET-1042\")",
  "status": "string (always \"created\" in this demo)"
}
```

## Feature 002 update

Feature `002-rerank-search-results` adds a re-ranking stage to `search_knowledge_base`. See
[../../002-rerank-search-results/contracts/mcp-tools.md](../../002-rerank-search-results/contracts/mcp-tools.md)
for the current output contract: `top_k` now caps the final re-ranked count, `score` is the re-rank
relevance (0–1) when re-ranking succeeds or the cosine similarity when it falls back/is disabled, and
an optional `rerank` block (provider, candidate/kept counts, elapsed time, fallback status, before/after
rankings) is added when re-ranking is enabled.
