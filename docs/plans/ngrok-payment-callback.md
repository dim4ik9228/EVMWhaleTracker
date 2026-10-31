# Ngrok payment callback tunnel

- Status: completed
- Date: 2026-10-29
- Summary: optional Docker ngrok agent forwards only POST payment callbacks to the existing app.

The user requests tunnel configuration and provided the path to an already-authenticated local ngrok config. Use its token privately in ignored `.docker/ngrok-agent.yml`; never commit or print the credential. Keep the existing host config unchanged. Run the official pinned Docker agent on the app network, upstream `http://app:8080`. Restrict the public endpoint to exact POST `/billing/nowpayments/ipn`; health and other paths/methods must return 404. Disable local request-body inspection and bind management API to host loopback only. Do not add OAuth/browser authentication that would prevent provider webhooks.

Configuration-only workflow: coordinator authors Compose/policy, independent read-only review, then deployment. Start only the tunnel container; preserve existing app/database state. Save callback URL in ignored local appsettings and restart only app if necessary to ensure reload. Sales/enforcement remain false. Verify public unsigned callback reaches app and returns 401, health/root/wrong-method return 404, app local health works, malformed/unsigned requests do not create receipts. No signed fabricated financial events, invoices, trades, payment or merchant account changes. Record private run evidence under ignored artifacts.

Tunnel URL is temporary infrastructure: current invoices retain their issued callback URL, so do not change it during a paid test. Docker/ngrok/host must remain running; restarting agent may change URL. No claim of real NOWPayments payment delivery from unsigned endpoint checks.

## Result

Coordinator configuration work and independent review completed. Review caught a missing credential-file ignore rule, fixed and verified before deployment. Normal/overlay Compose and official agent `config check` passed. Started only the ngrok sidecar, obtained HTTPS URL from loopback management API, verified unsigned POST 401 and root/health/GET callback 404. Saved callback in ignored local appsettings and restarted only app to load cached options. App/ngrok running afterward, local health reachable and callback 401 retained. Receipt counts unchanged; exact local-setting comparison found only CallbackUrl changed. Sales/enforcement remain false. Original host config untouched and credentials never printed. No payment/invoice or signed synthetic callback was sent. Private evidence is under ignored artifacts via `current-ngrok.txt`. See [operation instructions](../operations/ngrok.md).
