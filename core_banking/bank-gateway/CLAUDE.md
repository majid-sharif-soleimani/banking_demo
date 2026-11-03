# bank-gateway

The single entry point (design plan ADR 12): YARP in front of every service, including `identity` (workspace ADR 0007). No database, no events. Plan: `docs/stages/stage-7-plan.md`.

## Behaviour

- `/api/<service>/{**rest}` → `http://<service>/{rest}`; the address comes from Aspire service discovery (`services:<service>:http:0`). Services listed in `Gateway:Services`.
- When `Gateway:ApiKey` is set, every `/api` call needs it in `X-Api-Key`; otherwise 401 `invalid_api_key`. The header is removed before forwarding. Outside Development the gateway refuses to start without a key (set `Gateway__ApiKey`; a secret, never in `appsettings.json`).
- Caller-sent `X-Customer-Id` and `X-Performed-By` are always dropped. Every `/api` call outside `/api/identity/*` needs `Authorization: Bearer <token>` (401 `missing_token`). The gateway calls `GET http://identity/sessions/current` (named HttpClient `identity`) and sets `X-Performed-By: <username>` (admin) or `X-Customer-Id: <id>` (customer); the token is not forwarded. A refused token returns identity's own 401 body; identity down is 503 `identity_unavailable`. The gateway refuses to start when `identity` is not in `Gateway:Services`. `X-Correlation-Id` is kept (at most 128 plain characters, otherwise replaced) or created, and echoed in the response. The services do not log it yet.
- `/openapi`: list of each service's document URL through the gateway. `/health`: the gateway plus every downstream `/health` (tag `ready`); `/alive`: the gateway only.

## Gotchas

- Configuration arrays merge by index: a blank `Gateway:Services` entry switches a service off; duplicates count once.
- Tests start a real stub service on a random port and point service discovery at it with `UseSetting("services:<name>:http:0", url)`. The same stub plays identity (`/sessions/current` with fixed test tokens).

## Commands

```bash
dotnet build && dotnet test      # no Docker needed
```
