# SignalR Hub Contracts

## Activity Hub — `/hubs/activity`

**Server → Client method**: `ActivityEvent(event: ActivityEventDto)`

```json
{
  "id": "guid",
  "category": "Ingestion | Retrieval | McpTool | Generation",
  "source": "IngestionWorker | McpServer | ChatApi",
  "operation": "string",
  "target": "string | null",
  "status": "Started | Succeeded | Failed",
  "detail": "string | null",
  "timestamp": "ISO-8601 string"
}
```

Clients connect and receive all subsequently broadcast events; on connect, the server replays
the last ~50 buffered events so the panel isn't empty on first load.

## Chat Hub — `/hubs/chat`

**Client → Server method**: `SendMessage(text: string)`

**Server → Client methods**:
- `ResponseToken(messageId: guid, token: string)` — called once per streamed token/fragment
- `ResponseComplete(messageId: guid, citedSources: SourceCitationDto[])`
- `ResponseError(messageId: guid, message: string)`

```json
// SourceCitationDto
{
  "documentTitle": "string",
  "sourceUrl": "string"
}
```

## Internal broadcast endpoint (cluster-internal only, not exposed via Ingress)

`POST /internal/activity-events` — body is an `ActivityEventDto` (see above). Called by
`IngestionWorker` and `McpServer` to have `ChatApi` re-broadcast on the Activity Hub. Protected
by a shared internal-only network policy / cluster-local service, not public auth.
