---
name: retro
description: Run a short learning retrospective with Majid at the end of a stage and record it in docs/learning-log.md. Use from /close-stage or when Majid runs /retro.
argument-hint: [stage-number]
---

Run the retrospective for stage $ARGUMENTS with Majid. The log records what Majid learned in his own words, so ask; do not write his answers for him.

1. List the concepts in the stage's "Learning" section and the D1 decisions he made, as a reminder.
2. Ask Majid these questions, all in one message:
   - Which concept is now clear to you? Explain one of them in two or three sentences.
   - Which concept is still unclear?
   - Which decision would you make differently if you started this stage again, and why?
   - What would you change in how we work together in the next stage?
3. Wait for his answers. Record them in English, keeping his meaning.
4. Append a section to `docs/learning-log.md`:

   ```
   ## Stage N: <name> (<date>)
   Concepts: ...
   Decisions I made: ... (ADR numbers)
   What I understood: ...
   Still unclear: ...
   What I would change: ...
   Working-style changes for next stage: ...
   ```

5. For each "still unclear" item, offer one `/explain` topic or a small exercise. If he asks for a working-style change, propose the matching edit to CLAUDE.md or the next stage file.
