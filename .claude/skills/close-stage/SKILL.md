---
name: close-stage
description: Verify and close the current phase 1 stage. Use when the stage's done-when commands pass.
disable-model-invocation: true
argument-hint: [stage-number]
---

Close stage $ARGUMENTS.

1. Run every done-when command in `docs/stages/stage-$ARGUMENTS.md`. Show the summary lines. If anything fails in code Claude wrote, fix it and run again. If it fails in code Majid wrote, show the failure and give a hint; do not fix it for him unless he asks.
2. Use the `banking-reviewer` subagent to review the stage diff against the stage file, `docs/implementation-decisions.md`, `phase1-design-plan.md` and the non-negotiable rules in CLAUDE.md. Fix REQUIRED findings in Claude's code, then re-run step 1. Report REQUIRED findings in Majid's code to him with an explanation, for him to fix. List OPTIONAL findings without fixing them.
3. Check that each decision made during the stage has an ADR, and that the design plan, the implementation decisions and the stage file still match the code. If a business rule changed, propose the plan edit to Majid instead of editing it yourself.
4. Update each touched service folder's CLAUDE.md with what a future session needs (commands, owned tables, events in and out, gotchas). Keep them short.
5. Commit the remaining changes on the stage branch. Do not merge into `master` and do not push.
6. Update `docs/progress.md`: status "Ready for review", today's date, one-line notes.
7. Report what was built, test evidence, review results, the D1 decisions Majid made and the D2 choices Claude made, and open questions.
8. Run `/retro $ARGUMENTS` with Majid.
9. Ask Majid to approve merging into `master` and starting the next stage. After he approves, merge, set the status to Done, and stop.
