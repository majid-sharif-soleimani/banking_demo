# 0005. Send logs to a local Seq, one group per system

- Status: Accepted
- Date: 2026-11-04
- Scope: bank-platform (Bank.ServiceDefaults, home hosting), agent
- Design plan reference: none; bank-platform ADR 0004 (home and AWS configuration)
- Decided by: Majid for Seq and for one group per log (2026-11-04). Claude for how it is wired.

## Context

Finding out why a chat stopped answering meant reading `docker logs` of two containers side by side. Majid already runs Seq on the host machine and wants the logs there: the agent's now, the bank's in the next step, each in its own group. Seq has no folder or group object for events. It stores events with properties and offers saved filters, called signals.

## Decision

1. `Bank.ServiceDefaults` 0.2.0 sends logs to Seq when `Seq:ServerUrl` is set, as a second OTLP log destination next to the dashboard (`/ingest/otlp/v1/logs`, HTTP protobuf). Without the setting nothing changes, so AWS, CI and tests are not affected.
2. A group is the resource attribute `log_group`, set in `OTEL_RESOURCE_ATTRIBUTES` per container. `agent` covers the agent host and the bank MCP server; `banking-demo` is reserved for the bank services. `service.name` still tells the containers apart.
3. In Seq, each group is one signal with the filter `@Resource.log_group = 'agent'`.
4. Containers reach Seq on the host machine as `host.docker.internal` (`extra_hosts`), without joining Seq to the bank's network.
5. HTTP client, Polly and EF Core command lines are at `Warning` in the agent's two apps. They were most of the volume and said nothing about what the agent did.

## Rationale

- OTLP needs no new package: the exporter is already there for the dashboard, and the same log pipeline feeds both.
- A property set per container needs no code per service, so the next step is only configuration and a rebuild.
- All consumers moved to 0.2.0 now, because CI packs one version from the same commit. They behave the same until `Seq:ServerUrl` is set.

## Alternatives considered

- `Seq.Extensions.Logging`: one line to add, but a second log pipeline, a new dependency, and a global property would need a wrapper.
- One Seq API key per group, with applied properties: Seq's own way, but it adds a secret per group and setup by hand, for a local Seq.
- An OpenTelemetry Collector between services and Seq: the right tool for many destinations, too much for one more.

## Consequences

- What is logged reaches Seq as is, so the rule stays: no personal data, tool arguments or results in logs.
- The Seq address is plain configuration; Seq ingestion on the host machine currently needs no key. If a key is added, it needs a header on the exporter and a secret.
- If Seq is down, the exporter drops those logs after retrying. The service and the dashboard are not affected.
