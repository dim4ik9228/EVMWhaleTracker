# Local payment callbacks through ngrok

The optional `docker-compose.ngrok.yml` runs a pinned official ngrok agent alongside the existing app. It forwards only exact POST `/billing/nowpayments/ipn` to `http://app:8080`. Other paths/methods return 404 at the edge. Payment signatures remain validated by the app. No browser OAuth is placed in front of the webhook.

The authenticated agent config is private at `.docker/ngrok-agent.yml`, excluded from Git and Docker build context, mounted read-only. The original Windows ngrok config is unchanged. Local HTTP request inspection is disabled; management API is available only on host loopback `http://127.0.0.1:4041`. Ngrok remains the TLS-terminating tunnel provider; do not treat it as an end-to-end encrypted channel beyond its edge.

Start the configured service from the repository root:

```powershell
docker compose -f docker-compose.yml -f docker-compose.ngrok.yml up -d --no-deps ngrok
```

Stop only the tunnel:

```powershell
docker compose -f docker-compose.yml -f docker-compose.ngrok.yml stop ngrok
```

The container uses `restart: unless-stopped`. Host/Docker/ngrok must remain running. After an agent restart inspect the local management API for the current HTTPS URL; do not assume URL stability. Set `Billing:CallbackUrl` in ignored `appsettings.Local.json` to that URL plus `/billing/nowpayments/ipn` and restart only app to refresh cached options. Existing issued invoices retain their original callback URL, so avoid tunnel changes while testing an outstanding payment.

Validation: unsigned POST should return 401, public root/health/GET callback should return 404, and local app health should remain available. Do not fabricate a signed settlement to test ingress. Real payment tests require separately scoped payer/amount authorization. Configuring the tunnel does not activate sales or replace missing terms/support and merchant payout verification.
