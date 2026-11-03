# Phase 2, stage 5: Complex workflows

Goal: the agent does the multi-step work of section 2.2's "Complex" rows: the customer's loan advisor, the admin's loan application review with the customer's documents, arrears follow-up and the management report. Documents and customer-written text are handled as untrusted content.

Read first: `phase2-design-plan.md` sections 2.2 (the "Complex" rows), 6, 7.3 (question 1), 9 (step 5); agent ADRs 0003 and 0004.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Prompt injection, untrusted content markers, the quarantined reader (a model without tools reads the document), output checked against a fixed shape, workflows as instructions over small tools | None asked (delegated, 2026-11-03). Claude's choices: agent ADR 0005. | Nothing |

Slices: (1) bank: running loans by classification, (2) MCP: the tools the workflows still missed, (3) host: untrusted markers and the document reader, (4) the workflows in the instructions.

## Bank

- Lending `GET /loans?classification=Current|Overdue|Default` without `customerId`: the staff list of running loans of that classification, oldest first, at most 100.

## MCP server

- Admin reads: `loans_by_classification`; `appointment_slots` moves to the shared tools; host-only `customer_document` (an upload's bytes as base64).
- Admin changes: `customer_note_add` (a note in the contact history), `appointment_book_for_customer`.

## Agent host

- `MarkUntrusted`: the results of reads that carry customer-written text (`customer_interactions`, applications, information requests) reach the model between `<<<UNTRUSTED` and `UNTRUSTED>>>` (`Logic/UntrustedContent`).
- `DocumentReader` (`document_read`, admin): the host fetches the file, a second model call without tools reads it and answers a fixed JSON shape, `Logic/DocumentFacts` checks it, and the agent gets only the checked facts, marked untrusted.
- Instructions: loan advisor (form, several simulations, comparison table, only the "can pay" check), information requests, document review, arrears follow-up (draft message in the customer's language, note or meeting on confirmation), management report (three reports, trends as a chart, in the admin's language).

## Tests (minimum)

- Unit: the markers (and a fake end marker inside the text), the document answer's shape and limits, the workflow instructions.
- MCP: the new tools listed and routed; a document as base64; a note as multipart.
- Integration: customer text between markers; a document read by a call without tools, the agent gets only facts; a reader answer outside the shape is not passed on.
- Bank: the classification list for staff, 403 for a customer, 400 for an unknown classification.

## Done when

```bash
(cd agent && dotnet build && dotnet test)
(cd core_banking/bank-lending && dotnet test)
```
