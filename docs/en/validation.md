# Validation Guide

[한국어](../검증_가이드.md) · [English](./validation.md)

Updated: 2026-05-15

```bash
make -C apps/omninode-core
python3 apps/omninode-sandbox/executor.py --code "print('ok')"
dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj
npm test
curl -s http://127.0.0.1:8080/readyz
```

`npm test` includes repository hygiene, dashboard syntax checks, router contracts, Telegram/chat contracts, routine/plan/notebook contracts, and the security boundary contract.

For screenshots, check the PNG files under `docs/assets/readme/`, including `dashboard-mobile-composer-390x844.png.png`.

Manual release checks should include remote-dashboard OTP authentication, remote sensitive-setting blocking, WebSocket unauthorized rejection, routine local-image path limits, attachment count/size rejection, and Markdown raw HTML blocking.
