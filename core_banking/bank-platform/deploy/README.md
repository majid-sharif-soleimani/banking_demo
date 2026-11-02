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
3. **Choose the values that stay off the repository.** Your IP and the AMI id are passed on the command line, never written to `cdk.json`. Your IP is personal data (`.claude/rules/secrets.md`), and the AMI must be pinned: a changed image id replaces the instance on the next deploy.
   ```bash
   export AWS_REGION=eu-north-1      # the scripts and the CLI use it
   MY_CIDR="$(curl -s https://checkip.amazonaws.com)/32"
   AMI=$(aws ssm get-parameter --name /aws/service/ami-amazon-linux-latest/al2023-ami-kernel-6.1-arm64 --query Parameter.Value --output text)
   ```
   Optional: `-c bank:keyPairName=<existing key pair>` for SSH. Without it, use Session Manager. Without your CIDR the security group allows only a documentation address, so nobody can connect.
4. **Deploy:**
   ```bash
   cd core_banking/bank-platform/deploy/cdk
   npx aws-cdk@2.1145.0 bootstrap      # once per account and region
   npx aws-cdk@2.1145.0 deploy -c bank:allowedCidr="$MY_CIDR" -c bank:amiId="$AMI"
   ```
   Pass the **same AMI** on every later deploy (keep it in your notes, or in the gitignored `cdk.context.json`). Upgrade it only on purpose, after a backup. The root volume is kept even if the instance is replaced (`DeleteOnTermination = false`).
5. **Set the secret** named in the `SecretName` output, once. Write the JSON to a temporary file **outside the repository**, then delete it:
   ```bash
   f=$(mktemp) && $EDITOR "$f"
   aws secretsmanager put-secret-value --secret-id <SecretName> --secret-string "file://$f" && rm -f "$f"
   ```
   Then restart the instance (`stop.sh`, `start.sh`), or run `sudo systemctl restart bank` on it. The JSON holds:
   - `POSTGRES_PASSWORD`, plus `OPERATIONS_DB_PASSWORD`, `CUSTOMERS_DB_PASSWORD`, `ACCOUNTS_DB_PASSWORD`, `CARDS_DB_PASSWORD`, `LENDING_DB_PASSWORD` and `LEDGER_DB_PASSWORD`. Each must be **at least 24 letters and digits**, with no symbols: the values go into env files and connection strings. For example, `openssl rand -hex 16` for each;
   - optionally `GHCR_TOKEN` and `GHCR_USER`.

   `start-bank.sh` refuses to start, and writes nothing, while a key is missing, empty or malformed. The database creates its roles only once, so a wrong first value would be permanent.

### Changing the deployed files later

User data runs only at the first boot. After a change to `docker-compose.aws.yml`, `deploy/instance/` or `db/init`, `cdk deploy` uploads the new files but the running instance keeps the old ones. Either copy the changed file to `/opt/bank/deploy` over Session Manager and run `sudo systemctl restart bank`, or rebuild the instance from a backup. New images only need `BANK_IMAGE_TAG` and a restart.

## Secrets to set

| Key | Where | Used by |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | the stack's Secrets Manager secret | PostgreSQL admin (init only) |
| `<SERVICE>_DB_PASSWORD` (six) | the same secret | the init script creates each role with it; each service connects with its own |
| `GHCR_TOKEN`, `GHCR_USER` (optional) | the same secret | `docker login ghcr.io` for private images |

Only the Customers container can reach the instance role (for S3); `start-bank.sh` blocks the metadata service for every other container. The role can read the secret, so a compromised Customers container could too (bank-platform ADR 0004).

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
