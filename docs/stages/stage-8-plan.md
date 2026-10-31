# Stage 8 plan: End-to-end tests and AWS

## Context

Stages 2–7 are Ready for review (not merged). Stage 8 does two things.

- **It proves the whole system** with five end-to-end scenarios. They run against the real AppHost (six services, the gateway, PostgreSQL, Kafka, Mailpit), and every call goes through the gateway. Unit and service tests prove each part; only these scenarios prove that the parts agree. For example, the ledger and the statements must tell the same story, and a missed installment must end with an email.
- **It prepares the same system for one small EC2 instance** that runs only during demos (design plan section 9). The pieces are a Compose file with the services, a CDK stack, S3 for files and backups, start, stop and backup scripts, and CI jobs that build images.

The hard parts:

- **Time.** The scenarios cover months of business days. Each scenario starts a fresh bank on 2026-01-01 and closes days one by one, waiting for every end of day.
- **Eventual consistency.** Collections, allocations and journal entries arrive through Kafka after a day opens, so every check waits until the state settles.
- **Nothing on AWS happens here.** Claude writes the code; `cdk deploy`, every `aws` command and every image push are Majid's (stage file, workspace rules). `cdk synth` must work without an AWS account.

Environment: `~/repos/banking_demo` on `<machine>`, branch `stage-8-e2e-aws` (from `stage-7-gateway-import`).

**Working mode (Majid, 2026-10-31):** Claude writes everything, including the "Majid writes" item (scenario 3), with a learning document per slice. D1 decisions are made by Claude under Majid's delegation and listed below. The CDK review with `/explain` before deployment stays Majid's, and so do all deployments.

## Slices

1. **End-to-end scenarios** in `bank-platform/tests/Bank.EndToEndTests` (Aspire testing, one fresh AppHost per scenario, scenarios run one after another):
   1. Customer registers, opens SEK and EUR accounts, deposits, transfers SEK to EUR; ledger and statements agree.
   2. Annuity loan: apply, review, request information, upload a document, approve, disburse, three months of collection, early payoff; trial balance zero.
   3. Missed installments: overdue, penalties from day 6, manual card payment clears arrears, classification returns to Current.
   4. 91 days unpaid: Default, emails sent.
   5. Standing order fails for lack of funds; customer emailed; next run succeeds after a deposit.
2. **Docker Compose for AWS:** `bank-platform/deploy/docker-compose.aws.yml` with all services (GHCR images), the gateway, PostgreSQL, Kafka, Mailpit and the standalone Aspire dashboard. Only the gateway port is published to the outside.
3. **CDK stack** in C# (`deploy/cdk`):
   - one VPC with a public subnet;
   - one t4g.medium instance;
   - a security group that opens the gateway port and SSH from a configurable CIDR;
   - an S3 bucket for files and backups;
   - an IAM role with access to that bucket and to one Secrets Manager secret;
   - user data that installs Docker and starts Compose.
4. **S3 storage and scripts:**
   - `S3FileStorage : IFileStorage` in Customers, selected by `Files:Provider`;
   - `deploy/start.sh`, `deploy/stop.sh` and `deploy/backup.sh`.
5. **CI image jobs:**
   - each service workflow gets a job that builds its image and pushes it to GHCR on `master`, disabled until Majid enables it;
   - an end-to-end workflow that runs only on demand.

## Decisions to review (Majid's delegation of 2026-10-31)

| Decision (D1) | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| EC2 instance size and start/stop routine | bank-platform 0002 | t4g.medium (2 vCPU, 4 GB, ARM), as the stage file says. Started and stopped by hand with `start.sh` and `stop.sh`; no schedule. The EBS volume keeps the data between demos | A schedule (EventBridge) that stops the instance every night: safer against a forgotten instance, but more moving parts |
| What runs in CI on every push and what only before a demo | bank-platform 0003 | Every push runs each service's unit and service integration tests, as today. The end-to-end scenarios (minutes, the whole AppHost) run only on demand (`workflow_dispatch`), before a demo or a merge | End-to-end on every push to `master`: earlier warning, but slow and costly in CI minutes |
| How the AWS instance gets its secrets (`.claude/rules/secrets.md` asks for an ADR in the first stage that needs it) | bank-platform 0004 | One Secrets Manager secret (JSON) created empty by CDK. At boot, `start-bank.sh` on the instance reads it with the instance role and writes a root-only `.env` that Compose reads. The services keep reading `IConfiguration` (environment variables) and never know about AWS | A .NET Secrets Manager configuration provider in every service: no file on disk, but an AWS dependency in every service |

Interpretations:

- **The bucket holds files under `files/` and backups under `backups/`.** Backups expire after 30 days (lifecycle rule) to bound the cost.
- **The standalone Aspire dashboard is only reachable through an SSH tunnel**, like Mailpit and Kafka UI. Only the gateway port is open.
- **The images are built with the .NET SDK container support** (`dotnet publish /t:PublishContainer`), with no Dockerfiles, for linux-arm64 to match the instance.
- **The end-to-end scenarios check the books more directly than the trial balance alone.** Customer deposits in the ledger must equal the account balances, and loans receivable must be zero after a payoff.

## Close-out (2026-10-31)

- Done-when: end-to-end 5 of 5 (about 4.5 minutes), `cdk synth` without an AWS account, `docker compose ... config` valid. Also customers 34 + 29 (5 new S3 tests on LocalStack) and platform 47.
- **The first runs found:**
  - Money's amount must be a JSON string (the fixture now keeps every service's log, which showed it);
  - each service's business date follows Operations a moment later, so `DaysPastDue` is read with `Eventually`;
  - Mailpit pages search results (50 by default).
- `banking-reviewer`: it recomputed the S3 penalty (about 1.30 SEK at due+6) and confirmed the scenarios are not vacuous. Five REQUIRED findings, all fixed:
  1. `start-bank.sh` wrote empty passwords for a missing key. It now validates every key before writing anything.
  2. Every container could reach the instance role. Only Customers can now, and the bucket grants are narrowed to `files/*` and put-only `backups/*`.
  3. An unpinned AMI would replace the instance. It is now pinned with `bank:amiId`, and the root volume is kept on termination.
  4. The README asked for your IP in the tracked `cdk.json`. It is now passed with `-c`.
  5. Kafka's data was outside its volume. `KAFKA_LOG_DIRS` is set, also in the local compose file.

  OPTIONAL items applied:
  - the exact penalty amount, and classification unchanged until end of day (S3);
  - exactly two classification warnings (S4);
  - password rules, and the secret file kept outside the repository;
  - a backup temp file that only root can read and that is always removed;
  - `backup.sh` shows errors;
  - the scripts require `AWS_REGION`;
  - the Kafka heap is capped;
  - CI paths include contracts and ServiceDefaults.

  Not applied (in "What to watch later"):
  - refreshing the deployed files at each boot;
  - a Compose plugin checksum;
  - `/health`-based compose health checks;
  - skipping the SSH rule without a key pair;
  - a test of the provider selection.
- **Not deployed:** no `cdk deploy`, no `aws` command, no image push. The estimated cost is in `deploy/README.md`: about $4–6 a month at 8 hours a week, and $31–53 running all month. Confirm it in the AWS Pricing Calculator.
- To review: bank-platform ADRs 0002–0004, scenario 3 (your item, written on delegation), and the CDK stack with `/explain` before any deployment. `/retro 8` pending. Not merged.

## Done when

```bash
cd core_banking/bank-platform && dotnet test tests/Bank.EndToEndTests
cd core_banking/bank-platform/deploy/cdk && cdk synth      # succeeds without an AWS account
docker compose -f core_banking/bank-platform/deploy/docker-compose.aws.yml config    # valid
```
