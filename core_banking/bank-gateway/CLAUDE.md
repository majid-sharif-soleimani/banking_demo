# bank-gateway

The single entry point (design plan ADR 12): YARP in front of every service. No database, no events. Plan: `docs/stages/stage-7-plan.md`.

## Behaviour

- `/api/<service>/{**rest}` → `http://<service>/{rest}`; the address comes from Aspire service discovery (`services:<service>:http:0`). Services listed in `Gateway:Services`.
- When `Gateway:ApiKey` is set, every `/api` call needs it in `X-Api-Key`; otherwise 401 `invalid_api_key`. The header is removed before forwarding. Outside Development the gateway refuses to start without a key (set `Gateway__ApiKey`; a secret, never in `appsettings.json`).
- Every `/api` call needs `X-Customer-Id` (a GUID) or `X-Performed-By`; otherwise 401 `missing_identity` (400 for a malformed customer id). A blank header counts as missing. Both are forwarded; `X-Correlation-Id` is kept (at most 128 plain characters, otherwise replaced) or created, and echoed in the response. The services do not log it yet.
- `/openapi`: list of each service's document URL through the gateway. `/health`: the gateway plus every downstream `/health` (tag `ready`); `/alive`: the gateway only.

## Gotchas

- Configuration arrays merge by index: a blank `Gateway:Services` entry switches a service off; duplicates count once.
- Tests start a real stub service on a random port and point service discovery at it with `UseSetting("services:<name>:http:0", url)`.

## Commands

```bash
dotnet build && dotnet test      # no Docker needed
```
