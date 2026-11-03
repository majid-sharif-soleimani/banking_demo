#!/usr/bin/env bash
# Fails when text on stdin contains a secret or personal data (.claude/rules/secrets.md).
# Two pattern lists, both extended regular expressions, one per line:
#   - the generic patterns below, which are safe to commit;
#   - the owner's own values (name, machine, tailnet, email) in .git/info/personal-patterns, which is never committed,
#     because a list of personal data in the repository would publish exactly what it protects.
# Used by pre-commit (staged lines) and pre-push (outgoing commits). Usage: ... | scan-personal-data.sh <what>
set -euo pipefail

generic=(
  '[a-z0-9-]+\.tail[0-9a-f]{4,}\.ts\.net'                 # a real Tailscale machine name
  '@(gmail|googlemail|hotmail|outlook|live|yahoo|icloud|proton)\.(com|me)'
  'AKIA[0-9A-Z]{16}'                                       # AWS access key id
  'arn:aws[a-z-]*:[a-z0-9-]*:[a-z0-9-]*:[0-9]{12}:'        # ARN with an account id
  '-----BEGIN [A-Z ]*PRIVATE KEY-----'
  'gh[pousr]_[A-Za-z0-9]{36}'                              # GitHub token
  'sk-ant-[A-Za-z0-9_-]{20,}'                              # Anthropic API key
)

patterns=$(mktemp)
trap 'rm -f "$patterns"' EXIT
printf '%s\n' "${generic[@]}" | sed 's/[[:space:]]*#.*$//' >"$patterns"
personal="$(git rev-parse --git-common-dir)/info/personal-patterns"
if [[ -f $personal ]]; then
  grep -v -E '^[[:space:]]*(#|$)' "$personal" >>"$patterns" || true
else
  echo "warning: $personal is missing; only the generic patterns are checked (.claude/rules/secrets.md)." >&2
fi

# GitHub's noreply address names the account, which is public anyway; it is the address commits should use.
if hits=$(grep -n -v -E '[0-9]+\+[A-Za-z0-9-]+@users\.noreply\.github\.com' | grep -i -E -f "$patterns"); then
  echo "Blocked: $1 contains what looks like a secret or personal data (.claude/rules/secrets.md):" >&2
  echo "$hits" | cut -c1-200 >&2
  echo "Move the value to user-secrets, .env or a placeholder. Bypass with --no-verify only if this is a false positive." >&2
  exit 1
fi
