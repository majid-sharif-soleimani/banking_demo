# Hosting the demo bank on a private host machine

The demo bank runs on a private host machine for one consumer, the phase 2 AI agent. The setup has to run all the time, survive a reboot without losing data, and be reachable from outside the host machine at `https://<machine>.<tailnet>.ts.net`.

The Aspire AppHost cannot do this: it starts with an empty database on every run and gives the gateway a changing port. This guide reuses the EC2 setup (`docker-compose.aws.yml`) instead. The same containers run under Docker Compose with persistent volumes, and Tailscale takes the place of the AWS security group.

```mermaid
flowchart LR
  Agent[AI agent] -->|HTTPS| TS[Tailscale serve / funnel<br/>machine.tailnet.ts.net]
  TS -->|http://127.0.0.1:8080| GW[gateway]
  GW --> OPS[operations] & CUS[customers] & ACC[accounts] & CRD[cards] & LEN[lending] & LED[ledger]
  OPS & CUS & ACC & CRD & LEN & LED --> PG[(PostgreSQL<br/>volume bank_postgres-data)]
  OPS & CUS & ACC & CRD & LEN & LED <--> K[(Kafka<br/>volume bank_kafka-data)]
```

| File | Role |
| --- | --- |
| `docker-compose.aws.yml` | The whole stack: six services, gateway, PostgreSQL, Kafka, Kafka UI, Mailpit, Aspire dashboard. Unchanged. |
| `docker-compose.home.yml` | Overrides only what differs at home: local images, local file storage, gateway on `127.0.0.1:8080`. |
| `env/*.env` (gitignored) | Database passwords, connection strings and the gateway's API key. On EC2, `start-bank.sh` writes these from Secrets Manager. At home, step 3 writes them. |

All commands run from the repository root unless a step says otherwise.

## 1. Check the prerequisites

```bash
docker compose version     # 2.24 or later (needed for !override and optional env files)
dotnet --version           # 10.x
tailscale status           # this machine is logged in
jq --version               # the checks in step 7 read JSON with jq (install jq first)
# Enable the Docker service at boot, so the bank starts too.
```

The host machine must not sleep or power off while the bank should be reachable.

## 2. Run the tests and build the images

The shared packages go into the local feed first. The services restore them from there.

```bash
cd core_banking
dotnet pack bank-contracts -c Release -o .local-feed
dotnet pack bank-platform/src/Bank.ServiceDefaults -c Release -o .local-feed

# The code is green before it becomes an image (platform plus end-to-end: about 6 minutes, needs Docker).
(cd bank-platform && dotnet test)

# One image per service in the local Docker daemon, built with the SDK (no Dockerfile, same as CI).
for s in operations customers accounts cards lending ledger identity; do
  (cd "bank-$s" && dotnet publish "src/Bank.${s^}.Api" -c Release --os linux --arch "$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/')" /t:PublishContainer \
     -p:ContainerRepository="bank-$s" -p:ContainerImageTags=home) || break
done
(cd bank-gateway && dotnet publish src/Bank.Gateway -c Release --os linux --arch "$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/')" /t:PublishContainer \
   -p:ContainerRepository=bank-gateway -p:ContainerImageTags=home)

docker images 'bank-*'     # eight images tagged "home"
```

## 3. Write the passwords (once)

PostgreSQL creates the roles only on an empty volume, so the passwords are generated once and kept. The guard keeps a second run from overwriting them.

```bash
cd core_banking/bank-platform/deploy
if [ ! -f env/postgres.env ]; then
  umask 077 && mkdir -p env
  services=(operations customers accounts cards lending ledger)
  {
    echo "POSTGRES_PASSWORD=$(openssl rand -hex 16)"
    for s in "${services[@]}"; do echo "${s^^}_DB_PASSWORD=$(openssl rand -hex 16)"; done
  } > env/postgres.env
  for s in "${services[@]}"; do
    pw=$(grep "^${s^^}_DB_PASSWORD=" env/postgres.env | cut -d= -f2)
    echo "ConnectionStrings__${s}-db=Host=postgres;Port=5432;Database=bank;Username=${s};Password=${pw}" > "env/${s}.env"
  done
fi
# The key every caller sends to the gateway in X-Api-Key. The gateway refuses to start without it.
if [ ! -f env/gateway.env ]; then
  (umask 077 && echo "Gateway__ApiKey=$(openssl rand -hex 32)" > env/gateway.env)
fi
git status --short env     # prints nothing: env/ is ignored
```

The agent (and anything else that calls the bank) needs this key. Give it from `env/gateway.env` through the agent's own secret store, never in a tracked file or a chat. To change the key, write a new value to the file and run `bank up -d --wait`: Compose recreates the gateway.

`bank-platform/docker-compose.yml` (infrastructure only) uses the same project name, `bank`, and the same volume names. If it was ever started with other passwords, the roles already exist with those passwords. In that case, remove the old volumes first. This deletes their data:

```bash
docker volume ls --filter name=bank_     # anything listed is from an earlier run
docker compose -f docker-compose.aws.yml -f docker-compose.home.yml down -v
```

## 4. Start the bank

```bash
cd core_banking/bank-platform/deploy
docker compose -f docker-compose.aws.yml -f docker-compose.home.yml up -d --wait
docker compose -f docker-compose.aws.yml -f docker-compose.home.yml ps    # every service healthy
curl -fsS http://127.0.0.1:8080/health; echo                             # Healthy
```

Every container has `restart: unless-stopped`, so after a reboot Docker starts the bank again with the same data.

Pages on this machine only: Aspire dashboard `http://localhost:18888` (logs, traces), Mailpit `http://localhost:8025`, Kafka UI `http://localhost:8081`.

If the dashboard image does not pull (`aspire-dashboard:13.6` is not checked yet), add `dashboard: { image: mcr.microsoft.com/dotnet/aspire-dashboard:<existing tag> }` to `docker-compose.home.yml`.

## 5. Load the initial data (once, on a fresh bank)

The import tool replays customers, accounts, loans, transactions and standing orders day by day through the gateway, then checks balances against statements and the trial balance. It refuses to run on a bank that already has its customers. It reads the gateway's key from `Import__ApiKey`:

```bash
export Import__ApiKey="$(cut -d= -f2- ../bank-platform/deploy/env/gateway.env)"   # from core_banking/bank-data-import
```

Option A, the 5-customer synthetic fixture (works now, about 2–5 minutes):

```bash
cd core_banking/bank-data-import
dotnet run --project src/Bank.DataImport -- --gateway http://127.0.0.1:8080 \
  --data tests/Bank.DataImport.IntegrationTests/Fixture
echo "exit code: $?"           # 0 = imported and verified
grep Verification data/import-report.md    # Verification: passed
```

Option B, the real databasesample.com export (about 100 customers, two years): put the CSV files in `data/raw/` (gitignored), check the column names in `src/Bank.DataImport/appsettings.json` (`Import:Columns`) against the files, then run the same command without `--data`. This takes much longer, because every business day is closed one at a time.

Option C, generated synthetic data (100 customers, one year; no download, no personal data). The generator writes the six files into `data/raw/` with the column names the tool expects. Its history meets the import's selection targets: 44 borrowers, 14 with late payments, 2 who stop paying and reach Default, and 20 standing orders. All loan amounts and terms fall inside the product ranges. About 9,000 transactions and 363 business days are replayed, so expect a long run.

```bash
cd core_banking/bank-data-import
python3 tools/generate-sample-data.py            # --years 2 for two years, --seed N for another data set
dotnet run --project src/Bank.DataImport -- --gateway http://127.0.0.1:8080
grep Verification data/import-report.md          # Verification: passed
```

To start over with an empty bank: `docker compose -f docker-compose.aws.yml -f docker-compose.home.yml down -v`, then steps 4 and 5. The passwords in `env/` stay valid, because the roles are created again from them.

## 6. Publish the gateway through Tailscale

Use A unless the agent cannot join the tailnet. Both serve `https://<machine>.<tailnet>.ts.net` with a certificate from Tailscale. Plain `http://` is not served. The real name is not written in this repository (it would point anyone to the host machine); the command in step 7 prints it.

```bash
# A: only devices in the tailnet (the agent runs on a machine logged in to Tailscale)
sudo tailscale serve --bg 8080

# B: the whole internet (the agent runs in the cloud, e.g. calls from an LLM provider)
sudo tailscale funnel --bg 8080       # the first time, it prints a link to enable Funnel for this tailnet

tailscale serve status                # shows what is published
sudo tailscale serve reset            # stops A; "sudo tailscale funnel reset" stops B
```

`serve` and `funnel` write the same entry for port 443; `funnel` only adds the internet flag, so running both leaves one entry. After `funnel` is turned on, the public relays take a few minutes (about 3 on 2026-11-03) before every one of them serves the URL; until then, devices outside the tailnet may get a TLS error.

With Funnel, anyone who knows the URL can reach the gateway. Two locks then guard it: the API key (the caller is the trusted agent) and a login (on whose behalf it acts; workspace ADR 0007). The logins are test logins: `admin/admin` is written in the code, and a customer logs in with the national id as username and password. So anyone who has the API key and reads this repository can act as staff or as any customer. Keep only synthetic data in this bank.

## 7. Test the running bank

Run the checks locally first, then through Tailscale. With serve, the tailnet URL answers only on devices in the tailnet. With Funnel, also test from a device outside it, such as a phone on mobile data.

```bash
TS_URL="https://$(tailscale status --json | python3 -c 'import sys,json;print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')"   # this machine's tailnet name
for GW in http://127.0.0.1:8080 "$TS_URL"; do
  echo "== $GW"
  K="X-Api-Key: $(cut -d= -f2- core_banking/bank-platform/deploy/env/gateway.env)"
  curl -fsS "$GW/health"; echo                                                        # Healthy
  curl -s -o /dev/null -w 'no key: %{http_code}\n' "$GW/api/lending/loan-products"                 # 401
  curl -s -o /dev/null -w 'no login: %{http_code}\n' -H "$K" "$GW/api/lending/loan-products"       # 401
  # Log in as admin; every login gets a new token, and the previous admin session ends (session_replaced).
  T=$(curl -fsS -H "$K" -H 'Content-Type: application/json' -d '{"username":"admin","password":"admin"}' "$GW/api/identity/login" | jq -r .token)
  A=(-H "$K" -H "Authorization: Bearer $T")   # from here on, the key and the token
  curl -fsS "${A[@]}" "$GW/api/operations/currencies"; echo                             # SEK, EUR, USD (active after the import)
  curl -fsS "${A[@]}" "$GW/api/operations/business-days/current"; echo                  # one Open day
  curl -fsS "${A[@]}" "$GW/api/lending/loan-products"; echo                             # includes PURCHASE
  for c in SEK EUR USD; do
    curl -fsS "${A[@]}" "$GW/api/ledger/trial-balance?currency=$c" | jq -c '{currency, balanced: (.totalDebit == .totalCredit)}'
  done                                                                                # balanced: true
done
```

Then check that the data survives a restart:

```bash
cd core_banking/bank-platform/deploy
docker compose -f docker-compose.aws.yml -f docker-compose.home.yml restart
docker compose -f docker-compose.aws.yml -f docker-compose.home.yml up -d --wait
K="X-Api-Key: $(cut -d= -f2- env/gateway.env)"
T=$(curl -fsS -H "$K" -H 'Content-Type: application/json' -d '{"username":"admin","password":"admin"}' http://127.0.0.1:8080/api/identity/login | jq -r .token)
curl -fsS -H "$K" -H "Authorization: Bearer $T" http://127.0.0.1:8080/api/operations/business-days/current; echo   # same date as before
```

Rebooting the host machine is the full check: after login, step 7 passes again without running any command.

## 8. The chat (phase 2)

The agent host serves the chat UI and keeps chats in its own schema `agent` (workspace ADR 0008). It runs as the container `agent` on `127.0.0.1:8090` and reaches the bank through the gateway with the gateway's key.

Once, on a bank whose database was created before `db/init/002-agent-role-and-schema.sql` existed (back up first, step "Day to day"):

```bash
cd core_banking/bank-platform/deploy
alias bank='docker compose -f docker-compose.aws.yml -f docker-compose.home.yml'
umask 077
grep -q '^AGENT_DB_PASSWORD=' env/postgres.env || echo "AGENT_DB_PASSWORD=$(openssl rand -hex 16)" >> env/postgres.env
pw=$(grep '^AGENT_DB_PASSWORD=' env/postgres.env | cut -d= -f2)
key=$(grep '^Gateway__ApiKey=' env/gateway.env | cut -d= -f2-)
printf 'ConnectionStrings__agent-db=Host=postgres;Port=5432;Database=bank;Username=agent;Password=%s\nAgent__Gateway__ApiKey=%s\n' "$pw" "$key" > env/agent.env
bank exec -T -e AGENT_DB_PASSWORD="$pw" postgres psql -U postgres -d bank -q -f /docker-entrypoint-initdb.d/002-agent-role-and-schema.sql
```

On a fresh volume, step 3 already wrote `AGENT_DB_PASSWORD` if you add it there, and the init script creates the role by itself.

Build and start it (the publish builds the chat UI first, so Node must be installed):

```bash
(cd agent && dotnet publish src/Bank.Agent.Host -c Release --os linux --arch "$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/')" /t:PublishContainer \
   -p:ContainerRepository=bank-agent -p:ContainerImageTags=home)
cd core_banking/bank-platform/deploy && bank up -d --wait --no-deps agent
curl -fsS -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8090/     # 200
```

### The agent's model and tools (phase 2 stage 2)

From stage 2 the chat answers with Claude, and the agent reaches the bank through the bank MCP server (container `bank-mcp`, bank network only). Two secrets, both only in gitignored files:

```bash
cd core_banking/bank-platform/deploy
umask 077
# The MCP server calls the gateway with the gateway's key.
key=$(grep '^Gateway__ApiKey=' env/gateway.env | cut -d= -f2-)
printf 'BankMcp__Gateway__ApiKey=%s\n' "$key" > env/bank-mcp.env
# The Anthropic API key, typed (not echoed, not in the shell history). The agent refuses to start in Production without it.
read -rsp 'Anthropic API key: ' k; echo
{ grep -v '^Agent__Model__ApiKey=' env/agent.env; printf 'Agent__Model__ApiKey=%s\n' "$k"; } > env/agent.env.new && mv env/agent.env.new env/agent.env; unset k
```

Build both images and start them:

```bash
(cd agent && dotnet publish src/Bank.Agent.Mcp -c Release --os linux --arch "$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/')" /t:PublishContainer \
   -p:ContainerRepository=bank-mcp -p:ContainerImageTags=home)
(cd agent && dotnet publish src/Bank.Agent.Host -c Release --os linux --arch "$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/')" /t:PublishContainer \
   -p:ContainerRepository=bank-agent -p:ContainerImageTags=home)
cd core_banking/bank-platform/deploy && bank up -d --wait --no-deps bank-mcp agent
```

The model name is `Agent__Model__Name` (default `claude-haiku-5-5`), the document reader's is `Agent__Model__ReaderName` (default `claude-haiku-5-5`); put them in `env/agent.env` to change them. The other cost limits (agent ADRs 0007, 0009) are `Agent__Model__MaxOutputTokens`, `Agent__Model__HistoryMessages`, `Agent__Model__RunsPerMinute`, `Agent__Model__MaxInputTokensPerRun`, `Agent__Model__ClearToolResultsAbove`, `Agent__Model__KeepToolResults`, `Agent__Model__ClearToolResultsAtLeast` and `Agent__Model__HistoryComponentLength`. Update `bank-mcp` and `agent` together: the host-only MCP tools were renamed (`host_` prefix).

Publish it in the tailnet on its own HTTPS port, next to the gateway on 443:

```bash
sudo tailscale serve --bg --https=8443 http://127.0.0.1:8090
tailscale serve status        # https://<machine>.<tailnet>.ts.net:8443 (tailnet only)
```

Open that address on a device logged in to the tailnet and log in as `admin` / `admin` or as a customer with their national id. Design decision A13 later moves the chat to 443 and takes the gateway off Tailscale.

### Phase 2, stages 3 to 6 on the home bank

Stages 3 to 6 changed four bank services (staff-only checks in Operations and Ledger, new lists in Accounts, Customers and Lending) and the agent. The bank part needs no secret; the agent part needs the two secrets above.

1. Back up first, then rebuild and recreate only the changed services. Customers keeps uploads inside its container, so check that it has none first, or copy them out:

```bash
mkdir -p ~/bank-backups && bank exec -T postgres pg_dump -U postgres -d bank -Fc > ~/bank-backups/bank-$(date +%Y%m%d-%H%M%S).dump
docker exec bank-customers-1 sh -c 'find /home/app/files -type f 2>/dev/null | wc -l'    # 0: nothing to lose
for s in operations ledger customers accounts lending; do
  (cd "../../bank-$s" && dotnet publish "src/Bank.${s^}.Api" -c Release --os linux --arch "$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/')" /t:PublishContainer \
     -p:ContainerRepository="bank-$s" -p:ContainerImageTags=home) || break
done
bank up -d --wait --no-deps operations ledger customers accounts lending
```

2. Write `env/bank-mcp.env` and the Anthropic key (the block above), then build and start `bank-mcp` and `agent` (the block above). The MCP server now has two paths, `/mcp/customer` and `/mcp/admin`; nothing else to configure.

3. Try it: log in as a customer and ask for the balances; log in as `admin` and ask for the SEK trial balance. Pick فارسی or Svenska in the header to see the UI in that language.

The evaluation set (`agent/eval`, agent README) runs on the host machine, not in a container, against the model only.

## When Customers says "Address already in use"

Customers has a fixed address (`172.30.0.10`), and a container created at the same time can take it. Find the holder, remove only that container, and start Customers first:

```bash
docker network inspect bank_bank --format '{{range .Containers}}{{.IPv4Address}} {{.Name}}{{println}}{{end}}' | grep '172.30.0.10/'
bank rm -sf <holder-service>
bank up -d --wait --no-deps customers
bank up -d --wait --no-deps <holder-service>
```

To avoid it, recreate Customers on its own before other services. Remove a holder only if it keeps no data in its container.

## Logs in Seq

Seq runs on the host machine, outside the bank's network, on port 5341 (bank-platform ADR 0005). In `docker-compose.home.yml`, `agent` and `bank-mcp` have `Seq__ServerUrl: http://host.docker.internal:5341` and `OTEL_RESOURCE_ATTRIBUTES: log_group=agent`.

Make the group visible in Seq once: open `http://localhost:5341`, type `@Resource.log_group = 'agent'` in the search box, then save it as a signal named `agent`.

Seq keeps its data in a folder on the host machine, so the logs survive a rebuild of the agent. `docker logs` does not: it ends with the container. To read token use, search for `@Message like 'Model call%'` or `@Message like 'Run in chat%'` (agent ADR 0011; `docs/learning/phase-2/fix-loan-summary-and-usage-logs.md` explains the numbers).

The bank's eight services are in `docker-compose.home.yml` too, in the group `banking-demo`: search for `@Resource.log_group = 'banking-demo'` and save that signal the same way. `service.name` tells the containers apart. HTTP client, Polly and YARP lines stay at `Warning` there, because one call through the gateway would otherwise write several of them. To see a request fail, search for `@Level = 'Error'`.

## Bring the bank back up (quick guide)

The bank is already installed: images built, passwords and the API key in `env/`, data in the volumes, the Tailscale entry saved. Nothing below deletes data.

```bash
cd ~/repos/banking_demo/core_banking/bank-platform/deploy
alias bank='docker compose -f docker-compose.aws.yml -f docker-compose.home.yml'

bank ps                                    # 1. what is running? After a reboot, usually everything already is.
bank up -d --wait                          # 2. start whatever is stopped (safe to run any time)
curl -fsS http://127.0.0.1:8080/health; echo                  # 3. Healthy
tailscale serve status                     # 4. shows the tailnet URL with "(tailnet only)"
sudo tailscale serve --bg 8080             #    only if step 4 shows nothing
curl -fsS "https://$(tailscale status --json | python3 -c 'import sys,json;print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')/health"; echo   # 5. Healthy
```

If `bank up` fails with "no such image", the images were removed: run the image build in step 2 again. Do not run `bank down -v` and do not delete `env/`: `-v` erases all data, and new passwords no longer match the database roles. If a step still fails, `bank logs --tail 50 <service>` shows why.

## Day to day

```bash
cd core_banking/bank-platform/deploy
alias bank='docker compose -f docker-compose.aws.yml -f docker-compose.home.yml'
bank ps                      # status
bank logs -f gateway         # one service's log (or the dashboard at :18888)
bank stop / bank up -d --wait
```

After a code change: run step 2 again, then `bank up -d --wait`. Compose recreates only the containers whose image changed, and the data stays.

## What to watch

- Files uploaded to Customers live inside its container and are lost when it is recreated (step 2 after a change). A volume would need its owner set for the container's non-root user.
- The host machine is the server. While it sleeps, is offline or is rebooting, the agent gets no answer.
- The real-dataset column mapping is still unchecked (`docs/progress.md`, open questions).
