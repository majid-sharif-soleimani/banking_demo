#!/usr/bin/env bash
# Runs on the EC2 instance every night (bank-backup.timer), or on demand through deploy/backup.sh. Dumps the whole
# PostgreSQL cluster and copies it to s3://<bucket>/backups/; the bucket deletes backups after 30 days.
set -euo pipefail
cd /opt/bank/deploy
# shellcheck disable=SC1091
source stack.env
file="bank-$(date -u +%Y-%m-%dT%H%M%SZ).sql.gz"
# The dump holds all customer data and the role password hashes: root only, and removed even when the upload fails.
umask 077
tmp=$(mktemp)
trap 'rm -f "$tmp"' EXIT
docker compose -f docker-compose.aws.yml exec -T postgres pg_dumpall -U postgres | gzip > "$tmp"
aws s3 cp --region "$AWS_REGION" "$tmp" "s3://$BANK_BUCKET/backups/$file"
echo "Backup written to s3://$BANK_BUCKET/backups/$file"
