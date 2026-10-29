---
name: explain
description: Explain a pattern, concept, decision or piece of code in this project so Majid understands it. Use when Majid asks why something is built the way it is, or runs /explain.
argument-hint: [topic, file, or "last slice"]
---

Explain $ARGUMENTS to Majid, an experienced .NET developer with a banking background who is learning solution architecture. Skip basics he already knows; go deep on the architecture.

Use this order and keep each part short:

1. **The problem**: what goes wrong without it, as a concrete failure in this banking system (for example: money debited twice, an event lost after a crash).
2. **The idea**: how the pattern solves it, in a few sentences.
3. **Where it is here**: the files and lines in this repo that implement it, and the flow through them. Draw a small ASCII sequence diagram when there is a message flow.
4. **The cost**: what it makes harder (complexity, latency, operations, testing).
5. **Alternatives**: one or two other ways to solve the same problem and when they would be the better choice.
6. **When not to use it**.
7. **Check yourself**: one question Majid can answer to test his understanding. Do not give the answer unless he asks.

If $ARGUMENTS is empty or "last slice", explain the patterns used in the most recent slice.
Point to a well-known source (book chapter, article or docs page) for further reading only when you are sure it exists.
