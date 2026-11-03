#!/usr/bin/env bash
# Runs on the EC2 instance at every boot (bank.service). Reads the passwords from AWS Secrets Manager with the
# instance role, writes one env file per container (root only), and starts the bank (bank-platform ADR 0004).
# Nothing here is a secret; the values exist only in the secret and in /opt/bank/deploy/env/ on the instance.
set -euo pipefail
cd /opt/bank/deploy
# shellcheck disable=SC1091
source stack.env

secret=$(aws secretsmanager get-secret-value --region "$AWS_REGION" --secret-id "$SECRET_ID" --query SecretString --output text)

# Every key must be there and non-empty before anything is written: the database init script runs only once, on an
# empty volume, so an empty password written now would become a role without a password for good.
required=(POSTGRES_PASSWORD OPERATIONS_DB_PASSWORD CUSTOMERS_DB_PASSWORD ACCOUNTS_DB_PASSWORD CARDS_DB_PASSWORD LENDING_DB_PASSWORD LEDGER_DB_PASSWORD)
declare -A pw
for key in "${required[@]}"; do
  if ! value=$(jq -er --arg k "$key" '.[$k] | select(type == "string" and length > 0)' <<<"$secret"); then
    echo "The secret has no non-empty $key (deploy/README.md). Nothing was started." >&2
    exit 1
  fi
  # Letters and digits only: the value goes into a compose env file ($ is special) and a connection string (; is).
  if [[ ! $value =~ ^[A-Za-z0-9]{24,}$ ]]; then
    echo "$key must be at least 24 letters and digits (deploy/README.md). Nothing was started." >&2
    exit 1
  fi
  pw[$key]=$value
done

umask 077
mkdir -p env
{
  for key in "${required[@]}"; do echo "$key=${pw[$key]}"; done
} > env/postgres.env
for s in operations customers accounts cards lending ledger; do
  upper=$(tr '[:lower:]' '[:upper:]' <<<"$s")
  echo "ConnectionStrings__${s}-db=Host=postgres;Port=5432;Database=bank;Username=${s};Password=${pw[${upper}_DB_PASSWORD]}" > "env/${s}.env"
done
printf 'Files__Bucket=%s\nAWS_REGION=%s\n' "$BANK_BUCKET" "$AWS_REGION" >> env/customers.env

# The key every caller sends to the gateway in X-Api-Key. Same rule as the passwords: it goes into an env file.
if ! api_key=$(jq -er '.GATEWAY_API_KEY | select(type == "string" and test("^[A-Za-z0-9]{32,}$"))' <<<"$secret"); then
  echo "The secret has no GATEWAY_API_KEY of at least 32 letters and digits (deploy/README.md). Nothing was started." >&2
  exit 1
fi
echo "Gateway__ApiKey=$api_key" > env/gateway.env

# Only Customers (fixed address on the bank network, docker-compose.aws.yml) may reach the instance metadata service
# and so the instance role; every other container, including the gateway and third-party images, is blocked.
iptables -D DOCKER-USER -d 169.254.169.254 -j DROP 2>/dev/null || true
iptables -D DOCKER-USER -s 172.30.0.10 -d 169.254.169.254 -j ACCEPT 2>/dev/null || true
iptables -I DOCKER-USER -d 169.254.169.254 -j DROP
iptables -I DOCKER-USER -s 172.30.0.10 -d 169.254.169.254 -j ACCEPT

# The GitHub account that owns the images (ghcr.io/<owner>/bank-<service>), kept out of the repository.
if ! GHCR_OWNER=$(jq -er '.GHCR_OWNER | select(type == "string" and length > 0)' <<<"$secret"); then
  echo "The secret has no non-empty GHCR_OWNER (deploy/README.md). Nothing was started." >&2
  exit 1
fi

# Private images need a token with read:packages; public images need none.
if token=$(jq -er '.GHCR_TOKEN // empty' <<<"$secret"); then
  echo "$token" | docker login ghcr.io -u "$(jq -r --arg o "$GHCR_OWNER" '.GHCR_USER // $o' <<<"$secret")" --password-stdin
fi

export BANK_IMAGE_TAG GHCR_OWNER
docker compose -f docker-compose.aws.yml pull
docker compose -f docker-compose.aws.yml up -d --wait
