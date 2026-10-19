# Feature Specification: Re-rank Vector Search Results

**Feature Branch**: `002-rerank-search-results`

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "I want to implement re-ranking on the vector search results. The candidate chunks that the vector search returns should be reranked using a cross encoder tool like Cohere to further ensure that the results provided to the LLM are consistent with the original question that was asked."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - More relevant answers via re-ranked context (Priority: P1)

A support user asks a question. The system first gathers a broad set of candidate knowledge-base chunks by vector similarity, then re-scores each candidate directly against the question and keeps only the best-matching chunks as context for the answer. The answer and its cited sources therefore reflect the chunks that most closely address the question, not merely those that are semantically near.

**Why this priority**: This is the core value of the feature: better grounding and fewer off-topic sources in answers.

**Independent Test**: Ask a question for which vector similarity alone ranks a loosely related chunk above the truly relevant one; verify the relevant chunk is used and cited, and the loosely related one is dropped.

**Acceptance Scenarios**:

1. **Given** a question and a candidate set larger than the final context size, **When** the system prepares context, **Then** candidates are re-ordered by relevance to the question and only the top-ranked chunks are supplied to the answer generator.
2. **Given** a re-ranked result, **When** the answer is shown, **Then** the cited sources correspond to the chunks actually supplied after re-ranking.
3. **Given** the candidate set is smaller than or equal to the final context size, **When** the question is processed, **Then** the chunks are still re-ordered by relevance and none are lost.

---

### User Story 2 - Visible re-ranking step in the activity log (Priority: P2)

A demo viewer watching the live activity log sees re-ranking as its own step, including how many candidates went in, how many were kept, and how the ordering changed.

**Why this priority**: The project requires every pipeline step to be observable; it also makes the feature's effect demonstrable.

**Independent Test**: Ask any question and confirm a re-ranking event appears between retrieval and answer generation with candidate count, kept count, and timing.

**Acceptance Scenarios**:

1. **Given** a question is asked, **When** re-ranking runs, **Then** an activity event reports candidates received, chunks kept, and elapsed time.
2. **Given** re-ranking changed the order, **When** the event is viewed, **Then** it shows the before/after ranking of the kept chunks (source and score).

---

### User Story 3 - Graceful behavior when re-ranking is unavailable or disabled (Priority: P3)

If the re-ranking service is unreachable, times out, or is turned off by configuration, the chat still answers using the original vector-similarity order, and the activity log notes the fallback.

**Why this priority**: Chat must never break because of an optional quality enhancement.

**Independent Test**: Disable or break the re-ranking service and ask a question; an answer is still produced and the log shows a fallback event.

**Acceptance Scenarios**:

1. **Given** the re-ranker is unreachable or times out, **When** a question is asked, **Then** the answer is produced from the top vector-similarity chunks and a warning event is logged.
2. **Given** re-ranking is disabled by configuration, **When** a question is asked, **Then** no re-ranking is attempted and behavior matches the pre-feature pipeline.

---

### Edge Cases

- Vector search returns zero candidates: re-ranking is skipped and the existing "no relevant content" behavior applies.
- All re-ranked scores fall below a minimum relevance threshold: the system treats this as no relevant content rather than passing weak context to the answer generator.
- Very long chunks exceed the re-ranker's input limit: chunks are truncated for scoring only, while the full chunk is still used as context.
- Duplicate or near-identical candidates: they do not occupy multiple final slots.
- Re-ranker returns a malformed or partial response: treated as unavailable (fallback).
- The MCP knowledge-base search tool also returns re-ranked results so both entry points are consistent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST retrieve a configurable number of candidate chunks (larger than the final context size) by vector similarity for each question.
- **FR-002**: The system MUST re-score every candidate against the original user question using a cross-encoder style relevance model, and order candidates by that score.
- **FR-003**: The system MUST supply only the top N re-ranked chunks (N configurable) as context to the answer generator.
- **FR-004**: Cited sources in the answer MUST correspond to the re-ranked chunks supplied as context.
- **FR-005**: The system MUST support a configurable minimum relevance score; chunks below it MUST be excluded, and if none remain the system MUST follow the existing no-relevant-content behavior.
- **FR-006**: The re-ranking provider MUST be swappable by configuration. A hosted provider (e.g., Cohere) MUST be supported, and a local re-ranker or stub implementing the same contract MUST be available and be the default so the system runs fully offline.
- **FR-007**: Any hosted-provider credentials MUST be supplied only via secret references, never committed literal values.
- **FR-008**: If the re-ranker fails, times out (configurable), or returns an invalid response, the system MUST fall back to vector-similarity order and still answer.
- **FR-009**: The system MUST be able to disable re-ranking through configuration, restoring the prior behavior.
- **FR-010**: The system MUST emit activity events for the re-ranking step (candidates in, chunks kept, elapsed time, before/after ordering with scores) and for any fallback.
- **FR-011**: The MCP knowledge-base search tool MUST apply the same re-ranking so results are consistent with the chat pipeline.
- **FR-012**: Re-ranking MUST preserve each chunk's link to its originating document and source reference.

### Key Entities

- **Candidate Chunk**: A knowledge-base chunk returned by vector search, with its source reference and similarity score.
- **Re-ranked Chunk**: A candidate with a question-specific relevance score and final rank position.
- **Re-ranking Settings**: Candidate count, final context size, minimum relevance score, timeout, provider choice, and enabled flag.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a curated set of at least 20 test questions, the proportion of answers whose cited sources include the expected source document improves compared with vector-search-only ordering, with no question regressing to zero relevant sources.
- **SC-002**: Re-ranking adds no more than 2 seconds to time-to-first-streamed-token on the local environment (hosted provider included under normal network conditions).
- **SC-003**: With the re-ranker disabled or unavailable, 100% of test questions still receive an answer.
- **SC-004**: 100% of chat questions show a re-ranking (or fallback) event in the activity log.
- **SC-005**: The full demo runs with zero required external accounts or network calls when the default local re-ranker is used.

## Assumptions

- Applies to the existing knowledge-base retrieval used by chat and the MCP search tool; document ingestion and chunking are unchanged.
- Default candidate count is larger than the final context size (e.g., about 20 candidates narrowed to about 5), tunable by configuration.
- The default provider is local/offline per the project constitution; Cohere is an optional, opt-in provider.
- Re-ranking operates per question with no persistence of scores.
- The curated question set for SC-001 will be created as part of this feature.
