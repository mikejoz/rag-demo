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
