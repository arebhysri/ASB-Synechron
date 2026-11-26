# Starter Kit

Everything around the Card Control API is provided, so you can spend your time on the API itself. You shouldn't need to change any of it. If you do, explain why in your README.

```
starter/
├── candidate-brief.md           # the exercise: start here
├── mock-identity-provider/      # fake OAuth 2.0 / OIDC authorisation server (client credentials)
├── tokenisation-stub/           # fake tokenisation API: exchanges a PAN for a card token
├── card-platform-stub/          # fake card platform
└── tools/
    └── jwe-tool.cs              # creates the bank's dev key pair and encrypts PANs like the partner does
```

Copy `starter/` into your own repository as a starting point. Everything needs the **.NET 10 SDK**.

## Quick start

From the `starter/` folder (these commands work in any shell):

```bash
# Terminal 1: identity provider
dotnet run --project mock-identity-provider --urls http://localhost:5090

# Terminal 2: tokenisation stub
dotnet run --project tokenisation-stub --urls http://localhost:5070

# Terminal 3: card platform stub
dotnet run --project card-platform-stub --urls http://localhost:5080

# Once: create the bank's dev key pair in .dev-keys/
dotnet run tools/jwe-tool.cs -- keygen
```

Then get what the partner sends: an access token and an encrypted PAN.

**PowerShell**

```powershell
$TOKEN = (Invoke-RestMethod -Method Post -Uri http://localhost:5090/oauth2/token -Body @{
  grant_type    = 'client_credentials'
  client_id     = 'partner-app'
  client_secret = 'dev-only-partner-app-secret'
  scope         = 'api://card-control/.default'
}).access_token

$PAN_JWE = dotnet run tools/jwe-tool.cs -- encrypt --key .dev-keys/bank-dev-1.public.pem 4111111111111111
```

**Bash** (needs `jq`)

```bash
TOKEN=$(curl -s -X POST http://localhost:5090/oauth2/token \
  -d grant_type=client_credentials \
  -d client_id=partner-app \
  -d client_secret=dev-only-partner-app-secret \
  -d scope=api://card-control/.default | jq -r .access_token)

PAN_JWE=$(dotnet run tools/jwe-tool.cs -- encrypt --key .dev-keys/bank-dev-1.public.pem 4111111111111111)
```

---

## 1. Mock identity provider

Behaves like Entra ID's client credentials flow, simplified. It runs over plain HTTP, and its signing key is generated at start-up, so tokens from a previous run stop working after a restart.

| Setting | Value |
|---|---|
| Issuer / authority | `http://localhost:5090` |
| Discovery document | `http://localhost:5090/.well-known/openid-configuration` |
| JWKS | `http://localhost:5090/jwks` |
| Token endpoint | `POST http://localhost:5090/oauth2/token` |
| Audience for this API | `api://card-control` (request `scope=api://card-control/.default`) |

### Clients

| `client_id` | `client_secret` | App roles |
|---|---|---|
| `partner-app` | `dev-only-partner-app-secret` | `CardControl.Write` |
| `partner-readonly` | `dev-only-partner-readonly-secret` | *(none)* |

Client credentials can go in the form body or in an HTTP Basic header. These dev secrets live in `mock-identity-provider/appsettings.json`.

### Example token payload

```json
{
  "iss": "http://localhost:5090",
  "aud": "api://card-control",
  "sub": "3b8f2c6e-9a1d-4f7b-8e2c-5d6a7b8c9d01",
  "azp": "partner-app",
  "azpacr": "1",
  "tid": "7d2e4c1a-5b3f-4e8a-9c6d-1f0a2b3c4d5e",
  "roles": ["CardControl.Write"],
  "iat": 1795252897, "nbf": 1795252897, "exp": 1795256497,
  "jti": "f35fb9bf-6c03-43da-93a1-695991fdfb4a",
  "ver": "2.0"
}
```

Signed with RS256. `roles` is left out when the client has no roles.

### Testing extras (mock only, not standard OAuth)

- `lifetime_seconds` form field: `1`–`3600` for a short-lived token, or `-3600`–`-1` for one that has **already expired**.
- `scope=api://other-api/.default` gets a valid token for a **different audience**.

---

## 2. JWE tool

Plays the partner's side of the PAN encryption. Decrypting in your API is part of the exercise.

```bash
# Create the bank's key pair: .dev-keys/bank-dev-1.private.pem (PKCS#8) and .public.pem (SPKI), RSA 3072
dotnet run tools/jwe-tool.cs -- keygen [--kid bank-dev-1] [--out .dev-keys] [--force]

# Encrypt a PAN with the bank's public key
dotnet run tools/jwe-tool.cs -- encrypt --key .dev-keys/bank-dev-1.public.pem [--kid bank-dev-1] <PAN>
```

Output format: compact JWE (RFC 7516), protected header `{"alg":"RSA-OAEP-256","enc":"A256GCM","kid":"bank-dev-1"}`. The plaintext is the PAN digits as UTF-8. `kid` defaults to the key file name. Look at the tool's source if you need more detail.

---

## 3. Tokenisation stub

Exchanges a PAN for a **card token**. It's the only downstream service that sees a PAN; everything else works with the token.

Both stubs have **no authentication**. Assume that in production they sit behind a service mesh that handles mTLS. You don't need to add auth to them. All errors from both are [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) with a machine-readable `code` field.

### `POST /tokens`

```bash
curl -X POST http://localhost:5070/tokens \
  -H 'Content-Type: application/json' \
  -d '{"pan":"4111111111111111"}'
```
```json
{ "cardToken": "ctok_8f3a1c2e9b7d4a01" }
```

| Status | `code` | When |
|---|---|---|
| 200 | – | Token returned |
| 400 | `InvalidPan` | Missing, not 12–19 digits, or fails the Luhn check |

The tokeniser doesn't know whether a card exists. Any **valid** PAN gets a token. PANs not in the seed data get a new random token that the card platform won't recognise (404). `POST /admin/reset` forgets those.

---

## 4. Card platform stub

Works **by card token only**. It's also the **system of record for the audit trail**: it records every lock and unlock attempt, including rejected ones, against the initiating user your API passes in.

### `GET /cards/{cardToken}`

```json
{
  "cardToken": "ctok_8f3a1c2e9b7d4a01",
  "corporateId": "CORP-1001",
  "cardholderName": "Aroha Ngata",
  "maskedPan": "411111******1111",
  "status": "Active",
  "updatedAt": "2026-01-01T00:00:00+00:00"
}
```

`corporateId` **can be `null`**. `status` is one of `Active`, `Locked` or `Cancelled`.

### `POST /cards/{cardToken}/lock` and `POST /cards/{cardToken}/unlock`

Change the card's state and record the attempt in the audit trail. Returns the updated card (same shape as above).

**Both endpoints take the same request body:**

```json
{ "initiatingUser": "jane.smith@acme.example", "channel": "partner-app" }
```

| Field | Required | Meaning |
|---|---|---|
| `initiatingUser` | Yes (max 128 chars) | The person who started the action |
| `channel` | No (max 128 chars) | The system the request came through |

```bash
curl -X POST http://localhost:5080/cards/ctok_8f3a1c2e9b7d4a01/lock \
  -H 'Content-Type: application/json' \
  -d '{"initiatingUser":"jane.smith@acme.example","channel":"partner-app"}'
```

### `GET /cards/{cardToken}/audit`

The audit trail for a card, oldest first. Every attempt is recorded, whatever the outcome.

```json
[
  {
    "id": "f34d2126-a34d-4c69-99fe-5e64ddf63709",
    "at": "2026-11-21T10:13:42.356+00:00",
    "cardToken": "ctok_8f3a1c2e9b7d4a01",
    "action": "Lock",
    "outcome": "Success",
    "initiatingUser": "jane.smith@acme.example",
    "channel": "partner-app"
  }
]
```

`outcome` is `Success` or the error `code` the attempt got (e.g. `CardAlreadyLocked`).

### Responses

| Status | `code` | When |
|---|---|---|
| 200 | – | Success |
| 400 | `InvalidRequest` | Missing or invalid input, e.g. no `initiatingUser` on a lock or unlock |
| 404 | `CardNotFound` | Unknown card token |
| 409 | `CardAlreadyLocked` | Lock requested on a card that's already locked |
| 409 | `CardNotLocked` | Unlock requested on a card that isn't locked |
| 422 | `CardCancelled` | Any change requested on a cancelled card |
| 503 | `ServiceUnavailable` | Injected fault (see below) |

### Fault injection (for testing and demos)

You can make the card platform slow or flaky, either at startup (environment variables or `appsettings`) or at runtime through the admin API:

```bash
# 2s latency, 30% of /cards/* calls return 503
curl -X PUT http://localhost:5080/admin/faults \
  -H 'Content-Type: application/json' \
  -d '{"latencyMs":2000,"errorRate":0.3}'

curl http://localhost:5080/admin/faults        # show current settings
curl -X POST http://localhost:5080/admin/reset # restore seed data, clear faults and the audit trail
curl http://localhost:5080/admin/audit         # audit trail for all cards
```

| Env var | Default |
|---|---|
| `Faults__LatencyMs` | `0` |
| `Faults__ErrorRate` | `0` (range 0.0–1.0) |

Faults apply to `/cards/*` only. `/healthz` and `/admin/*` always respond normally.

---

## Seed data

Shared by both stubs:

| PAN (tokenisation stub) | Card token | Corporate (card platform) | Status (card platform) |
|---|---|---|---|
| 4111111111111111 | ctok_8f3a1c2e9b7d4a01 | CORP-1001 | Active |
| 4242424242424242 | ctok_2b9e7f4c1a6d3e02 | CORP-1001 | Locked |
| 4012888888881881 | ctok_5d1c8a3f7e2b9c03 | CORP-2002 | Active |
| 4000056655665556 | ctok_9e4b2d7a1c8f5e04 | CORP-2002 | Cancelled |
| 4000002500003155 | ctok_3a7f9c1e5b2d8a05 | CORP-3003 | Active |
| 4000000000003220 | ctok_7c2e5a9f3d1b6c06 | *(none)* | Active |

These are well-known public test card numbers, not real cards. Which corporates the partner is enabled for is in the exercise brief.
