---
name: new-adr
description: Record an architecture or business decision as an ADR. Use whenever a non-trivial decision is made or a design plan decision changes.
argument-hint: [title]
---

Create an ADR for: $ARGUMENTS

1. Decide the scope: workspace-wide decisions go in `docs/adr/`; decisions that affect one service go in that service folder's `docs/adr/`.
2. Find the next free number in that folder (four digits). File name: `NNNN-kebab-case-title.md`, with the structure of `docs/adr/0000-template.md`, in English. Inside that structure, follow the order of `.claude/rules/documentation.md`: Context gives the big picture, Decision the building blocks and how they connect, and the remaining sections the why.
3. Who writes it:
   - **Majid's decision (D1)**: fill only the header and "Context", and list the options under "Alternatives considered" without judging them. Leave "Decision", "Rationale" and "Consequences" for Majid, with a one-line prompt in each section. Tell Majid the file is ready. When he says he is done, critique it as a senior architect: missing forces, weak rationale, unnamed consequences, alternatives rejected for the wrong reason. Do not rewrite his text; suggest changes and let him edit.
   - **Claude's decision (D2) or one already in the design plan**: fill every section briefly, naming the rejected alternatives and why.
4. If the decision changes something in `phase1-design-plan.md`, propose the matching edit to the plan and mention the ADR number in it.
5. Reply in one or two sentences: what was decided (or what Majid needs to write) and where it is recorded.
