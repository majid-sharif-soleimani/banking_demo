---
paths:
  - "core_banking/**/*IntegrationTests*/**/*.cs"
---

# Integration test rules

- Use Testcontainers for PostgreSQL and Kafka; one container set per test class fixture, not per test.
- Each test seeds its own data and uses its own ids; tests must pass in any order and in parallel.
- Control time with a fake `IBusinessClock`; never sleep to wait for time to pass.
- Wait for asynchronous effects with a polling helper that has a timeout, not fixed delays.
- Every handler gets at least: the happy path, the duplicate-message case (effect applied once), and one failure path.
