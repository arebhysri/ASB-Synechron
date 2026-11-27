# Card Control API

A .NET 10 ASP.NET Core API that lets a partner **lock and unlock a corporate card**.

The partner calls the API with:
- an access token from the identity provider,
- the card number (PAN) encrypted as a compact JWE,
- the corporate ID,
- the ID of the person who started the action, in the `X-Initiating-User` header.

The API never sees or stores the PAN in clear text outside memory. It never logs the PAN or returns it.

## Request flow

1. **Authenticate**: validate the JWT (signature via the IdP's JWKS, issuer, audience `api://card-control`, lifetime) and require the `CardControl.Write` role.
2. **Validate input**: `X-Initiating-User` is required (max 128 chars), and so is the body.
3. **Entitlement**: the partner must be enabled for the corporate (`CORP-1001`, `CORP-2002`). This is checked **before** the PAN is decrypted.
4. **Decrypt** the JWE (RSA-OAEP-256 / A256GCM) with the bank's private key.
5. **Tokenise**: exchange the PAN for a card token (tokenisation stub).
6. **Get the card** from the card platform and check it belongs to the requested corporate.
7. **Lock or unlock** on the card platform, passing the initiating user for the audit trail.
8. **Return** the masked PAN and the new status.

## How to run

You need the .NET 10 SDK. Run the starter kit commands from the `starter/` folder.

```powershell
cd starter

# Terminal 1: identity provider
dotnet run --project mock-identity-provider --urls http://localhost:5090

# Terminal 2: tokenisation stub
dotnet run --project tokenisation-stub --urls http://localhost:5070

# Terminal 3: card platform stub
dotnet run --project card-platform-stub --urls http://localhost:5080

# Once: create the bank's dev key pair in starter/.dev-keys/
dotnet run tools/jwe-tool.cs -- keygen
```

Then start the API from the repo root (Terminal 4):

```powershell
dotnet run --project CardControl.Api --launch-profile http   # http://localhost:5045
```

Or press F5 in Visual Studio. It opens Swagger UI at http://localhost:5045/swagger (Development only). Click **Authorize** and paste a token.

The API reads the bank's private key from `starter/.dev-keys/bank-dev-1.private.pem`. All settings are in `CardControl.Api/appsettings.json` and are validated at start-up.

### Sample call (PowerShell, from `starter/`)

```powershell
$TOKEN = (Invoke-RestMethod -Method Post -Uri http://localhost:5090/oauth2/token -Body @{
  grant_type    = 'client_credentials'
  client_id     = 'partner-app'
  client_secret = 'dev-only-partner-app-secret'
  scope         = 'api://card-control/.default'
}).access_token

$PAN_JWE = dotnet run tools/jwe-tool.cs -- encrypt --key .dev-keys/bank-dev-1.public.pem 4111111111111111

$body = @{ corporateId = 'CORP-1001'; encryptedPan = "$PAN_JWE" } | ConvertTo-Json

# Lock
Invoke-RestMethod -Method Post -Uri http://localhost:5045/api/cards/lock `
  -Headers @{ Authorization = "Bearer $TOKEN"; 'X-Initiating-User' = 'jane.smith@acme.example' } `
  -ContentType 'application/json' -Body $body

# Unlock
Invoke-RestMethod -Method Post -Uri http://localhost:5045/api/cards/unlock `
  -Headers @{ Authorization = "Bearer $TOKEN"; 'X-Initiating-User' = 'jane.smith@acme.example' } `
  -ContentType 'application/json' -Body $body

# Check the audit trail on the card platform
Invoke-RestMethod http://localhost:5080/admin/audit
```

Response:

```json
{ "maskedPan": "411111******1111", "corporateId": "CORP-1001", "status": "Locked", "updatedAt": "2026-11-27T00:00:00+00:00" }
```

`CardControl.Api/CardControl.Api.http` has the same requests for the Visual Studio HTTP client.

### Run the tests

```powershell
dotnet test
```

The tests need no stubs or keys. They create their own keys and tokens and fake the two downstream services.

## API contract

| Method | Route | Body | Header |
|---|---|---|---|
| POST | `/api/cards/lock` | `{ "corporateId": "...", "encryptedPan": "<compact JWE>" }` | `Authorization: Bearer <token>`, `X-Initiating-User` |
| POST | `/api/cards/unlock` | same | same |

The OpenAPI document is at `/openapi/v1.json` (Development only).

### Errors

All errors are [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`) with a machine-readable `code` field.

| Status | `code` | When |
|---|---|---|
| 400 | `InvalidInitiatingUser` | `X-Initiating-User` missing, empty or longer than 128 chars |
| 400 | `InvalidEncryptedPan` | JWE is malformed, uses the wrong algorithm/key, or fails decryption |
| 400 | `InvalidPan` | Decrypted PAN is not a valid card number (from the tokenisation stub) |
| 400 | *(validation)* | Body missing or required fields empty |
| 401 | – | Token missing, expired, wrong audience/issuer or bad signature |
| 403 | – | Token has no `CardControl.Write` role |
| 403 | `CorporateNotEnabled` | Partner is not enabled for this corporate |
| 404 | `CardNotFound` | Unknown card, or the card belongs to another corporate |
| 409 | `CardAlreadyLocked` | Lock on a locked card |
| 409 | `CardNotLocked` | Unlock on a card that isn't locked |
| 422 | `CardCancelled` | Any change on a cancelled card |
| 502 | `TokenisationFailed` / `CardPlatformFailed` | Downstream returned an unexpected error or response |
| 503 | `CardPlatformUnavailable` | Card platform reported it is unavailable |
| 503 | `DownstreamUnavailable` | Downstream unreachable or timed out (10 s) |
| 500 | `InternalServerError` | Anything unexpected (no details returned) |

## Project structure

Projects are in the repo root. `src` and `tests` are solution folders in `ASB-Synechron.slnx`.

| Project | Responsibility |
|---|---|
| `CardControl.Api` | Program/DI, JWT auth and policy, controller, global exception handler, Swagger |
| `CardControl.Service` | `CardControlService` (orchestration) and `JwePanDecryptor` |
| `CardControl.Repository` | HTTP clients for the tokenisation stub and card platform, entitlement check |
| `CardControl.Interface` | Interfaces between layers |
| `CardControl.Model` | Requests, responses, enums, `CardControlException`, error codes |
| `CardControl.Data` | Options classes bound from configuration |
| `CardControl.Test` | Unit tests (43) |
| `CardControl.IntegrationTest` | Integration tests through the real HTTP pipeline (16) |

Dependencies point inwards: Api → Service / Repository → Interface → Model / Data. The starter kit in `starter/` is unchanged.

## Assumptions

- **Contract**: two routes, `POST /api/cards/lock` and `/unlock`, with the same body. The action is in the route so each is a clear, separate operation.
- **Card of another corporate returns 404, not 403**, with the same body as an unknown card. A partner can't learn that a card exists for a corporate it isn't working with.
- **Entitlement is checked first**, before decrypting the PAN or calling downstream services. A request for a corporate that isn't enabled does no sensitive work.
- **Entitlement** is the configured list of enabled corporates (`Entitlements:EnabledCorporates`). There is one partner, so the list isn't per client.
- **Role** comes from the token's `roles` claim (Entra ID style) and must be `CardControl.Write`.
- **Response** returns the masked PAN from the card platform, the corporate ID, the new status and the update time. The card token is internal and isn't returned.
- **Lock on a locked card is a 409**, not a silent success. The partner sees what actually happened, and the card platform still records the attempt in the audit trail.
- **Cancelled card is 422**, matching the card platform.
- **Channel**: `partner-app` is sent to the card platform as the channel for the audit trail.
- **No retries.** A lock/unlock isn't safe to retry blindly: a retry after a timeout could land on a card whose state already changed, and adds a second audit entry.
- **Swagger UI and the OpenAPI document are Development only.**
- **TLS**: locally the API runs on plain HTTP, like the starter kit. In AKS, TLS is terminated at the ingress, and HTTPS redirection/HSTS would be enabled.

## Shortcuts and what I'd do with more time

- **Resilience**: add Polly (`Microsoft.Extensions.Http.Resilience`) for timeouts and a circuit breaker on the card platform, and retries on the read-only calls (tokenise, get card) only.
- **Keys**: one decryption key read from a PEM file. In production: a key ring by `kid` for rotation, and the key in Azure Key Vault / an HSM.
- **PAN type**: wrap the decrypted PAN in a small type whose `ToString()` is masked, so it can't be logged by mistake.
- **Observability**: correlation ID on every request and passed downstream, structured logging, health checks for the IdP and stubs.
- **Rate limiting** per client.
- **HTTPS**: `RequireHttpsMetadata` is off in Development only because the mock IdP is plain HTTP.
- **Contract tests** against the real stubs (the tests currently fake them).

## Time spent

About 8 hours in total, over two sessions.

## How I used AI tools

- I designed and built the solution myself in Visual Studio: layered structure, project references, NuGet packages, configuration and all API code.
- I used Claude as a pair-programming assistant: to understand the starter contracts, discuss design choices (e.g. 404 vs 403 for cards of other corporates, when retries are safe), get code suggestions per step, and review my work after each step.
- I typed, built, ran and debugged everything myself, fixed the bugs found in review, and tested the API end to end (lock/unlock, audit trail, 401/403/400/409).
- I used AI (Claude) to write the unit and integration tests and the Swagger setup. I reviewed every change, ran the tests and made sure they pass.
- Rules for working with Claude in this repo are in [CLAUDE.md](CLAUDE.md): plan first, I approve, I review and commit.
