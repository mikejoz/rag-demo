# Feature Specification: Prompt Grounding

**Feature Branch**: `003-prompt-grounding`

**Created**: 2026-10-20

**Status**: Draft

**Input**: User description: "We need to implement prompt grounding. I would like answers to be generated using only the context from the vector search. This is not a general chat bot, this is meant to be a support chat bot and it should stay on-topic and when it doesn't have an answer provided by the vector search, it should state that it was unable to find information on that subject or something to that affect rather than fabricating an answer."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Answers based only on retrieved knowledge (Priority: P1)

A support user asks a question that the knowledge base covers. The assistant answers using only the retrieved knowledge-base content and does not add facts from its own general knowledge.

**Why this priority**: Trustworthy, source-based answers are the core value of a support assistant.

**Independent Test**: Ask questions whose answers exist in the knowledge base and verify every claim in the answer traces to the retrieved content.

**Acceptance Scenarios**:

1. **Given** the knowledge base contains an answer, **When** the user asks about it, **Then** the answer reflects only the retrieved content.
2. **Given** retrieved content only partially covers the question, **When** the user asks, **Then** the assistant answers the covered part and says the rest could not be found.

---

### User Story 2 - Honest "not found" response (Priority: P1)

When retrieval returns nothing relevant, the assistant says it was unable to find information on the subject instead of guessing.

**Why this priority**: Fabricated answers in a support context cause real harm and erode trust.

**Independent Test**: Ask about a topic absent from the knowledge base and verify a clear "unable to find information" reply with no invented details.

**Acceptance Scenarios**:

1. **Given** no relevant content is retrieved, **When** the user asks a question, **Then** the assistant states it could not find information on that subject and invents no answer.
2. **Given** a "not found" reply, **When** shown, **Then** it suggests rephrasing or contacting a human support channel.

---

### User Story 3 - Stay on-topic (Priority: P2)

The assistant declines off-topic or general-chat requests (e.g. trivia, coding help, opinions) and redirects the user to support topics.

**Why this priority**: The product is a support assistant, not a general chatbot.

**Independent Test**: Send unrelated requests and verify polite refusal with redirection.

**Acceptance Scenarios**:

1. **Given** an off-topic request, **When** the user sends it, **Then** the assistant politely declines and says it only helps with supported topics.
2. **Given** a user asks the assistant to ignore its rules or answer from general knowledge, **When** sent, **Then** the assistant still follows grounding rules.

---

### Edge Cases

- Retrieved content is only weakly related to the question: treated as not found rather than stretched into an answer.
- Retrieved content contradicts itself: the assistant presents what the sources say without inventing a resolution.
- Retrieved content contains instructions: they are treated as information, not as commands overriding grounding rules.
- Follow-up questions relying on earlier conversation: answers must still be supported by retrieved content.
- Greetings and small talk: answered briefly and then redirected to support topics.
- Search service unavailable: user gets an error or "unable to find information" message, never a guess.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST generate answers using only content retrieved from the knowledge base for the current question.
- **FR-002**: System MUST NOT present facts that are not supported by the retrieved content.
- **FR-003**: When no sufficiently relevant content is retrieved, System MUST reply that it was unable to find information on the subject.
- **FR-004**: When retrieved content covers only part of a question, System MUST answer the supported part and state what could not be found.
- **FR-005**: System MUST decline off-topic requests and redirect the user to supported support topics.
- **FR-006**: System MUST keep grounding rules in force despite user attempts to override them.
- **FR-007**: System MUST treat retrieved content as reference data, not as instructions.
- **FR-008**: Grounding behavior MUST apply consistently to every chat response, including follow-up turns.
- **FR-009**: System MUST apply a relevance threshold to retrieved content so weakly related results are treated as not found.

### Key Entities

- **Retrieved Context**: Knowledge-base passages returned for a question; the only permitted source of facts.
- **Grounding Policy**: Rules defining allowed sources, topic scope, and "not found" behavior.
- **Fallback Response**: The message shown when no supported answer exists.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test set of in-scope questions, at least 95% of answers contain only claims supported by retrieved content.
- **SC-002**: In a test set of questions with no knowledge-base coverage, at least 95% receive a "unable to find information" response with no fabricated details.
- **SC-003**: In a test set of off-topic requests, at least 95% are declined and redirected.
- **SC-004**: In a test set of prompt-override attempts, 100% fail to bypass grounding rules.
- **SC-005**: Answerable questions are still answered; correct-answer rate does not drop more than 5% versus current behavior.

## Assumptions

- Users are support users asking about content in the existing knowledge base.
- The existing retrieval pipeline (including any reranking) remains the source of context.
- The exact wording of the fallback message is flexible as long as it clearly says information was not found.
- Citations or source links are out of scope for this feature.
- Multi-language handling follows existing behavior.
