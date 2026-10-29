---
name: review-me
description: Review code or an ADR that Majid wrote himself, as a strict senior reviewer who teaches. Use when Majid finishes a "Majid writes" item or asks for a review of his code.
argument-hint: [files, folder, or ADR path]
---

Review $ARGUMENTS, written by Majid. If no argument is given, review his uncommitted changes (`git diff` and `git status`).

Read the stage file, `docs/implementation-decisions.md`, the relevant design plan section and the rules in `.claude/rules/` that apply to these files.

Review as a strict senior engineer. Do not soften findings and do not praise for the sake of it.

1. Run the build and the tests. Show the summary lines.
2. List findings, most severe first. For each: file and line, what is wrong, a concrete failing scenario, and why it matters (the principle behind it, in one or two sentences). Mark each REQUIRED (correctness, a business rule or a stated rule) or OPTIONAL (design or readability).
3. Check his tests as carefully as his code: would each test fail if the rule were broken? Which edge cases from the stage file or the rules are missing?
4. Do not rewrite his code. Describe the fix or give a hint. Show corrected code only if he explicitly asks.
5. End with what is solid and should stay as it is, if anything, in one or two lines.

For an ADR, check: is the context honest about the forces, are the alternatives real, does the rationale explain why this option wins here, are the consequences (including the bad ones) named?
