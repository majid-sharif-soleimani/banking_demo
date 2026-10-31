# Deploying the demo bank to AWS

The same bank that runs locally under Aspire runs on **one EC2 instance** with Docker Compose, started only for demos (design plan section 9). This folder holds everything for that. Nothing here has been deployed. Every `cdk deploy`, `aws` command and image push is run by Majid.

## What is here

| Path | What it does |
| --- | --- |
| `docker-compose.aws.yml` | The six services and the gateway (GHCR images), PostgreSQL, Kafka, Kafka UI, Mailpit and the standalone Aspire dashboard. Only the gateway port is open to the outside. |
| `cdk/` | The AWS CDK app in C#: a VPC with one public subnet, a t4g.medium instance, a security group, an S3 bucket, a Secrets Manager secret and the instance role. |
| `instance/` | Copied to the instance at first boot: `start-bank.sh` (reads the secret, writes the env files, starts Compose), `backup-db.sh`, and the systemd units that run them. |
| `start.sh`, `stop.sh` | Start and stop the instance from your machine. |
| `backup.sh` | Takes a database backup now, through Session Manager. A nightly backup also runs on the instance. |

Why it is built this way: bank-platform ADRs 0002 (instance and start/stop), 0003 (CI and images) and 0004 (secrets).

## First deployment (Majid)

1. **Review the stack with `/explain`** before anything else (stage 8 file).
2. **Images.** Set the repository variable `PUBLISH_IMAGES` to `true` (GitHub → Settings → Variables), merge to `master`, and let each service workflow push its image. Make the packages public on GHCR, or put a token with `read:packages` in the secret as `GHCR_TOKEN`.
3. **Configure** `cdk/cdk.json`:
   - `bank:allowedCidr`: your own IP with `/32`. The placeholder `203.0.113.10/32` is a documentation address, so nobody can connect until you change it;
   - `bank:region`: the region (default `eu-north-1`);
   - `bank:keyPairName`: an existing EC2 key pair for SSH. Leave it empty to use Session Manager only.
4. **Deploy:**
   ```bash
   cd core_banking/bank-platform/deploy/cdk
   npx aws-cdk@2.1145.0 bootstrap      # once per account and region
   npx aws-cdk@2.1145.0 deploy
   ```
5. **Set the secret** named in the `SecretName` output. You set it once, and it is never committed. Then restart the instance (`stop.sh`, `start.sh`), or run `sudo systemctl restart bank` on it.
   ```bash
   aws secretsmanager put-secret-value --secret-id <SecretName> --secret-string file://bank-secret.json
   ```
   `bank-secret.json` is a local file that you delete afterwards. It holds these keys, each with a strong random value:
   - `POSTGRES_PASSWORD`, plus `OPERATIONS_DB_PASSWORD`, `CUSTOMERS_DB_PASSWORD`, `ACCOUNTS_DB_PASSWORD`, `CARDS_DB_PASSWORD`, `LENDING_DB_PASSWORD` and `LEDGER_DB_PASSWORD`;
   - optionally `GHCR_TOKEN` and `GHCR_USER`.

## Secrets to set

| Key | Where | Used by |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | the stack's Secrets Manager secret | PostgreSQL admin (init only) |
| `<SERVICE>_DB_PASSWORD` (six) | the same secret | the init script creates each role with it; each service connects with its own |
| `GHCR_TOKEN`, `GHCR_USER` (optional) | the same secret | `docker login ghcr.io` for private images |

## Every demo

```bash
deploy/start.sh        # prints http://<address>:8080 and the SSH tunnel command
# ... demo ...
deploy/stop.sh         # compute billing stops; the data stays on the volume
```

To see the dashboard (logs, traces), Mailpit and Kafka UI, open the SSH tunnel that `start.sh` prints, then visit `http://localhost:18888`, `http://localhost:8025` and `http://localhost:8081`.

## Estimated monthly cost

This estimate is from public price listings checked on 2026-10-31. The sources disagree for eu-north-1, so confirm in the AWS Pricing Calculator before you deploy.

| Item | Price | Running 8 h a week (~35 h/month) | Running all month (730 h) |
| --- | --- | --- | --- |
| t4g.medium, on demand | about $0.034–0.063 per hour (listings disagree for Stockholm) | $1.20–2.20 | $25–46 |
| Public IPv4 address (while running) | $0.005 per hour | $0.18 | $3.65 |
| 30 GB gp3 volume (always, also when stopped) | about $0.08–0.10 per GB-month | $2.40–2.90 | $2.40–2.90 |
| Secrets Manager, one secret | $0.40 per month | $0.40 | $0.40 |
| S3 (files and 30 days of backups) | cents | < $0.10 | < $0.10 |
| **Total** | | **about $4–6** | **about $31–53** |

Forgetting `stop.sh` costs about $1–1.70 a day.
