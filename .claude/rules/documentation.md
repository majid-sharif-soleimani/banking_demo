---
paths:
  - "docs/**/*.md"
  - "core_banking/**/docs/**/*.md"
  - "**/README.md"
---

# Documentation structure

Applies to every document Claude writes or rewrites in this workspace, in every stage. Decided by Majid on 2026-10-30. The slice 1.2 learning document (`docs/learning/stage-1/slice-1.2.md`) is the reference example.

## The rule

Every document explains in this order:

1. **The big picture.** What we want to achieve and why it is hard: the goal, the problem it solves, and what goes wrong without it. A reader who stops here knows *what* and *why*, but not yet *how*.
2. **The building blocks and how they connect.** Every project, table, class and tool that was built, what each one does, and how they work together. Draw a diagram for each flow (Mermaid), then explain each part under its own heading with its file path. Nothing that was built may be left unexplained.
3. **Why it is designed this way.** For each decision: why it wins, the alternatives that were rejected and why, and the price we pay. Separate Majid's decisions (D1, with their ADR numbers) from Claude's choices (D2), then name the patterns used.

After these three parts, where they apply:

- **What was tested**: the tests by project and scenario, and the command with its real result.
- **What to watch later**: known limits and open work.
- **Check your understanding**: questions for Majid, without the answers.

## How to apply it

- Go from the general to the specific: never start with a file list or a table of classes.
- Explain how the parts connect, not only what each part is. A reader should be able to follow one request or one message through the diagram and the headings.
- When the design changed while it was being built, say how and why in part 3. Do not silently describe only the final state.
- When code changes, update the document in the same commit, keeping the same structure.
- Keep it in English (workspace rule), with short sentences.

## Documents with their own template

These follow the same order inside their fixed sections:

- **ADRs** (`/new-adr`): Context is the big picture, Decision is the building blocks and how they connect, and Rationale, Alternatives considered and Consequences are the why.
- **Stage plans** (`/start-stage`): the context and slice goals come first, then folders, files and interfaces, then the decisions.

Exempt, because they are short reference notes, not explanations: `CLAUDE.md` files, files in `.claude/`, `docs/progress.md`, and the entries of `docs/learning-log.md` (`/retro` format).
