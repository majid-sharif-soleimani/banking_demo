# Chat UI end-to-end tests

Playwright drives a real browser against the whole bank started by the AppHost.

```bash
(cd agent/chat-ui && npm ci && npm run build)                     # the UI the host serves
dotnet run --project core_banking/bank-platform/src/Bank.AppHost  # in another terminal; wait until healthy
cd agent/tests/chat-ui-e2e && npm ci && npx playwright install chromium && npx playwright test
```

The agent host listens on `http://localhost:5180` and the gateway on `http://localhost:5100` (their `launchSettings.json`). Override with `AGENT_URL` and `GATEWAY_URL`. The tests register their own customers through the gateway, so they run on a fresh AppHost.
