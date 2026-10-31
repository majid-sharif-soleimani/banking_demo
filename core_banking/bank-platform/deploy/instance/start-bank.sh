#!/usr/bin/env bash
# Runs on the EC2 instance at every boot (bank.service). Reads the passwords from AWS Secrets Manager with the
# instance role, writes one env file per container (root only), and starts the bank (bank-platform ADR 0004).
# Nothing here is a secret; the values exist only in the secret and in /opt/bank/deploy/env/ on the instance.
set -euo pipefail
cd /opt/bank/deploy
# shellcheck disable=SC1091
source stack.env

secret=$(aws secretsmanager get-secret-value --region "$AWS_REGION" --secret-id "$SECRET_ID" --query SecretString --output text)
value() {
  local v
  v=$(jq -er --arg k "$1" '.[$k] // empty' <<<"$secret") || { echo "The secret has no $1 (deploy/README.md)." >&2; exit 1; }
  printf '%s' "$v"
}

umask 077
mkdir -p env
{
  echo "POSTGRES_PASSWORD=$(value POSTGRES_PASSWORD)"
  for s in OPERATIONS CUSTOMERS ACCOUNTS CARDS LENDING LEDGER; do echo "${s}_DB_PASSWORD=$(value "${s}_DB_PASSWORD")"; done
} > env/postgres.env
for s in operations customers accounts cards lending ledger; do
  upper=$(tr '[:lower:]' '[:upper:]' <<<"$s")
  echo "ConnectionStrings__${s}-db=Host=postgres;Port=5432;Database=bank;Username=${s};Password=$(value "${upper}_DB_PASSWORD")" > "env/${s}.env"
done
printf 'Files__Bucket=%s\nAWS_REGION=%s\n' "$BANK_BUCKET" "$AWS_REGION" >> env/customers.env

# Private images need a token with read:packages; public images need none.
if token=$(jq -er '.GHCR_TOKEN // empty' <<<"$secret"); then
  echo "$token" | docker login ghcr.io -u "$(jq -r '.GHCR_USER // "<owner>"' <<<"$secret")" --password-stdin
fi

export BANK_IMAGE_TAG
docker compose -f docker-compose.aws.yml pull
docker compose -f docker-compose.aws.yml up -d --wait
