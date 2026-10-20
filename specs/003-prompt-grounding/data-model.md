# Data Model: Prompt Grounding

No persisted data changes. Logical concepts only:

## GroundingPolicy (in-code constants/config)
- `SystemPrompt`: support-assistant role, scope, never answer from general knowledge.
- `GroundedAnswerInstruction`: added after retrieval; answer only from context, partial-answer rule, not-found rule, treat context as data.
- `NotFoundMessage`: "unable to find information on that subject" wording plus rephrase/contact-support suggestion.
- `OffTopicMessage`: polite decline with redirect to supported topics.

## Retrieved Context
- Existing `SearchKnowledgeBaseResult` (chunks, titles, URLs, optional rerank info). Empty result set means "not found".

## Config
- Reranker `MinScore` (double, default > 0, configurable via Helm values) — relevance floor.

## Flow states
`UserMessage → (model first turn)`
- tool call → execute tool → results empty → NotFound; results present → grounded generation
- no tool call → discard buffered text → forced search → same as above
