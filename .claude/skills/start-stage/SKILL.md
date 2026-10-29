---
name: start-stage
description: Start a phase 1 stage from its spec in docs/stages and run it slice by slice in learning mode. Use when Majid says to begin stage N.
disable-model-invocation: true
argument-hint: [stage-number]
---

Start stage $ARGUMENTS of phase 1 in learning mode (see "Learning mode" in `docs/implementation-decisions.md`).

1. Read `docs/progress.md`. If the previous stage is not Done, stop and tell Majid.
2. Read `docs/stages/stage-$ARGUMENTS.md` (including its "Learning" section), `docs/implementation-decisions.md`, and only the design plan sections the stage file points to. Read the CLAUDE.md of every existing service folder the stage touches.
3. Write `docs/stages/stage-$ARGUMENTS-plan.md` in English with:
   - the slices from the stage file, in order, and for each slice: folders and projects, files and public interfaces, the tests written first, who writes what (Claude, or Majid for the "Majid writes" items), and its D1 decisions
   - the ADRs this stage needs and who writes each one
   - the done-when commands from the stage file
   - "Blocking questions": business-rule gaps or contradictions with the design plan
4. If there are blocking questions, tell Majid and stop.
5. Give Majid a design briefing for the first slice, short and concrete:
   - the concepts of the slice in plain terms: what problem each one solves and what would go wrong without it
   - the D1 decisions of the slice, each with 2 or 3 options, their trade-offs, and your recommendation labeled as such
   Then wait for his decisions. Do not write code before he has decided.
6. After Majid decides: set the stage to "In progress" in `docs/progress.md`; create the stage's folders if the stage file says so; create branch `stage-$ARGUMENTS-<short-name>`.
7. For each slice:
   - Build Claude's part test-first. Never write the "Majid writes" items; tell Majid which of them are now unblocked and what interface they must fit.
   - Build, run the tests, commit on the stage branch.
   - Checkpoint: show the build and test summary lines; explain what was built, the patterns used and why; list D2 choices with one line each; ask two short questions that check Majid's understanding of the slice. Then stop until Majid says to continue.
   - Before the next slice, give the design briefing for that slice (step 5) and wait for its D1 decisions.
8. Record ADRs with `/new-adr` as decisions are made.
9. When the done-when commands in the stage file pass and Majid's items are reviewed, tell Majid the stage is ready for `/close-stage $ARGUMENTS`.
