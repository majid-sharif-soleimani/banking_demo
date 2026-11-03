# bank-identity

Test logins for the agent (workspace ADR 0007). The agent logs in through the gateway and sends the token on every later call:

```bash
curl -s -H "X-Api-Key: $KEY" -H 'Content-Type: application/json' \
  -d '{"username":"IMP-C001","password":"IMP-C001"}' http://127.0.0.1:8080/api/identity/login
# {"token":"…","username":"IMP-C001","role":"customer","customerId":"…","name":"…","expiresAt":"…"}
curl -s -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8080/api/identity/sessions/current
```

## Secrets to set

None. The admin login is hard-coded on purpose for testing (ADR 0007); it is not a secret and must be replaced before real users arrive.
