# Bank agent

The chat UI and the agent host of phase 2. See `CLAUDE.md` for behaviour and commands, and `docs/learning/phase-2/stage-1-chat-shell.md` for how it works.

## Run locally

```bash
(cd agent/chat-ui && npm ci && npm run build)          # the UI, into the host's wwwroot
dotnet run --project core_banking/bank-platform/src/Bank.AppHost
# open http://localhost:5180 and log in as admin / admin, or as a customer with their national id
```

For UI work with hot reload, run `npm run dev` in `agent/chat-ui` while the AppHost runs; Vite proxies `/auth` and `/agent` to port 5180.

## Secrets to set

| Key | Where | When |
| --- | --- | --- |
| `Agent:Gateway:ApiKey` (`Agent__Gateway__ApiKey`) | Not needed under the AppHost (Development: the gateway needs no key). At home: `deploy/env/agent.env`, the same value as `Gateway__ApiKey`. | Outside Development the host refuses to start without it. |
| `Agent:Model:ApiKey` (`Agent__Model__ApiKey`) | Under the AppHost: `dotnet user-secrets set "Parameters:anthropic-api-key" "<key>" --project core_banking/bank-platform/src/Bank.AppHost`. At home: `deploy/env/agent.env`. | Optional in Development (the echo answers without it); Production refuses to start without it. |
| `BankMcp:Gateway:ApiKey` (`BankMcp__Gateway__ApiKey`) | Not needed under the AppHost. At home: `deploy/env/bank-mcp.env`, the same value as `Gateway__ApiKey`. | Outside Development the MCP server refuses to start without it. |
| `AGENT_DB_PASSWORD` | The AppHost generates it. In Docker Compose: `env/postgres.env`. | When the agent runs in Compose (hosting stage). |

Locally with `dotnet run` outside the AppHost: `dotnet user-secrets set "Agent:Gateway:ApiKey" "<value>" --project agent/src/Bank.Agent.Host`.
