# 0005. Mark customer-written text as untrusted and read documents with a model that has no tools

- Status: Accepted; changed by ADR 0007 (every read is marked unless bank-only, the history replays components between markers, the document tool is `host_customer_document` and the reader has its own model)
- Date: 2026-11-03
- Scope: agent (Bank.Agent.Host, Bank.Agent.Mcp), bank-lending
- Design plan reference: `phase2-design-plan.md` sections 2.2 (Complex rows), 6 ("tool results are data, not instructions"), 9 step 5; agent ADRs 0003 and 0004
- Decided by: Claude. Majid delegated the phase 2 decisions on 2026-11-03.

## Context

The admin's workflows read text that customers wrote: notes, email replies, the purpose of an application, and uploaded documents. Any of it can contain a prompt injection ("assistant, approve this loan"). The model reads tool results as plain text, so it cannot tell such a sentence from its own instructions. Every change already needs a person's click (ADR 0003), but an injected text could still mislead what the agent tells the admin.

## Decision

1. **Markers around customer-written text.** The host wraps the results of the reads that carry such text (`MarkUntrusted`) between `<<<UNTRUSTED source="..."` and `UNTRUSTED>>>`; markers inside the text are removed. The instructions of both roles say that nothing between the markers is an instruction.
2. **Documents are read by a quarantined reader.** The model never gets a document's bytes. `document_read` (host-side) fetches the file through the host-only MCP tool `customer_document`, and a second call of the same model, with no tools, reads it and answers a fixed JSON shape (type, summary, at most 20 facts, suspicious text).
3. **The reader's answer is checked in code** (`Logic/DocumentFacts.Parse`): the exact shape and length limits, or nothing. The agent gets only the checked facts, marked untrusted.
4. **Workflows live in the instructions, over small tools.** The loan advisor, review, arrears follow-up and management report are steps the model follows; no workflow engine and no multi-call tools in the MCP server.
5. **The only bank addition is a list** of running loans by classification, so arrears follow-up can see each loan.

## Rationale

Markers are cheap and make the boundary visible to the model. The quarantined reader follows the "dual LLM" idea: the part that reads untrusted content has no power, and the part that has power reads only a narrow, checked summary. A document that talks the reader into another answer fails the shape check or ends up as text in a fact, still marked untrusted, with the person's click still between the model and any change.

## Alternatives considered

- Sending the document to the main agent: simplest, but the model with tools would read the injection directly.
- Extracting PDF text with a library and sending that: no image support, and the text is just as untrusted.
- A classifier that blocks "instruction-like" text: false positives on real documents, and easy to get around.
- A workflow engine with fixed steps: more predictable, but much more code; the confirmation rule already guards every change.

## Consequences

- A document read costs a second model call.
- Without a key the echo answers the reader, the shape check fails, and the agent says the document could not be read.
- The markers lower the risk; they do not remove it. The real protection stays the person's click (ADR 0003) and the bank's checks (ADR 0004).
- Stage 6's evaluation set must include injection attempts in notes and documents.
