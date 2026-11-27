# CLAUDE.md: Card Control API

How Claude (or any AI assistant) must work in this repository. Follow it for every change.

## 0. Authorship and AI use

- I designed the solution and wrote the API myself in Visual Studio, one step at a time (see the commit
  history): layered structure, project references, NuGet packages, configuration and API code.
- I used Claude mostly like documentation and a reviewer: to look up how the starter contracts, JWE and JWT
  validation work, to compare design options (e.g. 404 vs 403 for cards of other corporates, when retries are
  safe), and to check my code after each step.
- For some steps I used its code suggestions as a reference, and typed and adapted them myself.
- I typed, built, ran and debugged the API myself, fixed the bugs found in review, and tested the API end to
  end (lock/unlock, audit trail, 401/403/400/409).
- I used AI (Claude) to write the unit and integration tests and the Swagger setup, following the testing rules in section 8. I
  reviewed every test, ran them and made sure they pass.
- From this file onwards Claude may also write clean-up fixes, but only under the rules below
  (plan first, I approve, I review and commit).

## 1. Overview and commands

ASP.NET Core Web API (.NET 10, `net10.0`). An external partner calls it to lock or unlock a corporate card.
Each request carries a bearer JWT, an `X-Initiating-User` header, a JWE-encrypted PAN and a `corporateId`.
The API validates the token, checks entitlement, decrypts the PAN, swaps it for a card token (tokenisation
stub), checks the card belongs to the corporate, and locks/unlocks it on the card platform stub.
No database, no EF Core, no migrations.

Run from the repo root (folder with `ASB-Synechron.slnx`):

| Task | Command |
|---|---|
| Build | `dotnet build ASB-Synechron.slnx` |
| Tests | `dotnet test ASB-Synechron.slnx` |
| Dev key pair (once) | `cd starter && dotnet run tools/jwe-tool.cs -- keygen` (writes `starter/.dev-keys/`, git-ignored) |
| Mock IdP | `cd starter && dotnet run --project mock-identity-provider --urls http://localhost:5090` |
| Tokenisation stub | `cd starter && dotnet run --project tokenisation-stub --urls http://localhost:5070` |
| Card platform stub | `cd starter && dotnet run --project card-platform-stub --urls http://localhost:5080` |
| API | `dotnet run --project CardControl.Api --launch-profile http` → `http://localhost:5045` |

Endpoints: `POST /api/cards/lock`, `POST /api/cards/unlock`.

Swagger UI: `http://localhost:5045/swagger` (Development only; OpenAPI document at `/openapi/v1.json`).

## 2. Permission rule (mandatory)

Before writing or editing code, creating files/projects, or adding/updating NuGet packages: describe the
plan (files, packages with versions, why) and **wait for the author's approval**.

- Read-only work needs no permission: reading, searching, `dotnet build`, `dotnet test`.
- Approval covers only that change. Ask again for the next one.
- Never edit anything under `starter/` (provided starter kit).
- Never commit or push; the author reviews and commits.

## 3. Project structure

Projects sit in the repo root; `src` and `tests` are solution folders only.

```
CardControl.Api/          # composition root: Program.cs, Controllers/CardsController.cs, Errors/CardControlExceptionHandler.cs
CardControl.Model/        # DTOs + shared types: Enums/, Requests/, Responses/, Downstream/, Errors/CardControlException.cs
CardControl.Data/         # Options/ bound from appsettings.json (AuthOptions, DownstreamOptions, EntitlementOptions, JweOptions)
CardControl.Interface/    # contracts: ICardControlService, IPanDecryptor, IEntitlementService, ITokenisationClient, ICardPlatformClient
CardControl.Repository/   # data access: Downstream/ (TokenisationClient, CardPlatformClient), Entitlements/EntitlementService
CardControl.Service/      # business logic: CardControl/CardControlService, Security/JwePanDecryptor
CardControl.Test/         # unit tests (project CardControl.UnitTest)
CardControl.IntegrationTest/  # WebApplicationFactory tests against the full HTTP pipeline
starter/                  # provided stubs and tools (do not modify)
```

Dependency direction:
- Api → Service, Repository, Interface, Model, Data (composition root only)
- Service → Interface, Model, Data. **Never** → Repository
- Repository → Interface, Model, Data
- Interface → Model. Model and Data → nothing
- UnitTest → Model, Repository, Service. IntegrationTest → Api

Keep project, folder and namespace names as they are.

## 4. Architecture rules

- **Controllers are thin**: validate the `X-Initiating-User` header, call `ICardControlService`, return the DTO.
  No business logic, HttpClient or config reads in controllers.
- New business logic: interface in `CardControl.Interface`, implementation in `CardControl.Service`,
  registered in `Program.cs`.
- New downstream/data source: interface in `CardControl.Interface`, implementation in `CardControl.Repository`,
  typed `AddHttpClient<IInterface, Impl>()` in `Program.cs`.
- New settings: class in `CardControl.Data/Options` with a `SectionName` constant, bound with
  `Configure<T>(GetSection(T.SectionName))`, read via `IOptions<T>`.
- DI lifetimes: `JwePanDecryptor` and `EntitlementService` singletons; `ICardControlService` scoped;
  HTTP clients via `IHttpClientFactory`.
- Only DTOs cross the API boundary: `CardActionRequest` in, `CardActionResponse` out. Never return card tokens
  or anything containing a PAN.
- Async all the way, every async method takes and passes a `CancellationToken`, names end in `Async`.

## 4a. Coding standards (mandatory)

- **SOLID**
  - S: one responsibility per class (controller = HTTP, service = flow, client = one downstream, decryptor = crypto).
  - O: extend by adding new classes/registrations, not by adding `if` branches to existing ones.
  - L: every implementation honours its interface contract (same exceptions, no surprise nulls).
  - I: small, focused interfaces (`IPanDecryptor`, `ITokenisationClient`, ...). No "god" interfaces.
  - D: depend on `CardControl.Interface` contracts via primary-constructor injection; never `new` a dependency.
- **async/await**: async all the way. No `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, `async void` or
  fire-and-forget tasks. Every async method ends in `Async`, takes a `CancellationToken` and passes it down.
  Independent awaits may run in parallel with `Task.WhenAll`; dependent ones stay sequential.
- **Global exception handler**: exactly one, `CardControlExceptionHandler` (`IExceptionHandler`), registered with
  `AddProblemDetails()` + `AddExceptionHandler<>()` and `app.UseExceptionHandler()` first in the pipeline.
  Controllers and services throw; they never build error responses themselves. See section 5.
- **try/catch**: only where you can do something meaningful:
  - translate a low-level exception into a `CardControlException` (e.g. crypto/format errors in `JwePanDecryptor`,
    transport failures in downstream clients);
  - fall back when a downstream error body isn't JSON.
  Not allowed: try/catch in controllers, empty catches that hide failures, `throw ex;`, catch-log-rethrow at every
  layer, returning null/default to hide an error, treating the caller's `OperationCanceledException` as an error.
  Catch the most specific type, use `when` filters, prefer `using` for disposal. Every catch needs a test.
- **Logging**: `ILogger<T>` via DI only, structured templates with named placeholders, no string interpolation,
  no sensitive data (section 6). See section 7.
- **N+1 queries**: no database today, so N/A. If a DB is ever added: never query inside a loop; load related
  data in one query (`Include`/projection/batched `WHERE IN`). The same applies to HTTP: never call a downstream
  API once per item in a loop when a batch call exists.
- **AsNoTracking**: no EF Core today, so N/A. If EF Core is added: every read-only query uses `AsNoTracking()`
  (or projects to a DTO with `Select`); tracking only for entities you will update.

## 5. Errors

- Expected outcomes throw `CardControlException(statusCode, code, message)`.
- `CardControlExceptionHandler` (the only global handler) turns them into RFC 9457 Problem Details with a
  `code` extension. Anything else → 500 `InternalServerError` with a generic detail.
- Do not add another handler, middleware or exception filter. Extend this one.
- Messages never contain a PAN, JWE, token, exception text from third-party code, or downstream `detail` text.
- Downstream mapping lives in `CardPlatformClient` / `TokenisationClient` only.

| Code | Status |
|---|---|
| InvalidInitiatingUser, InvalidEncryptedPan, InvalidPan | 400 |
| (no / bad token) | 401 |
| CorporateNotEnabled, (missing role) | 403 |
| CardNotFound (also when card belongs to another corporate) | 404 |
| CardAlreadyLocked, CardNotLocked | 409 |
| CardCancelled | 422 |
| TokenisationFailed, CardPlatformFailed | 502 |
| CardPlatformUnavailable | 503 |

## 6. Security rules (mandatory)

- **PAN**: never log it, never put it in an exception message or response. Only `TokenisationClient` sends it.
- **Tokens**: keep issuer, audience `api://card-control`, lifetime (30 s skew), signature via JWKS,
  `MapInboundClaims = false`, `RoleClaimType = "roles"` and the `CardControlWrite` policy
  (`CardControl.Write` role). Never weaken one to make a test pass.
- **JWE**: accept only `alg=RSA-OAEP-256`, `enc=A256GCM`, matching `kid`. Any failure → 400
  `InvalidEncryptedPan` with a generic message.
- **Audit**: every lock/unlock passes `initiatingUser` and `channel` to the card platform.
- **Entitlement**: enabled corporates come from `appsettings.json` (`Entitlements:EnabledCorporates`), checked
  before decrypting. A card of another corporate returns the same 404 as an unknown card.
- Never log the bearer token, the JWE, the private key or request bodies.

## 7. Logging

`ILogger<T>` via DI, structured templates (`"... {Code}"`), never string interpolation. Information for normal
outcomes, Warning for rejected/denied requests, Error for unhandled errors.

## 8. Testing rules

- Every new or changed service, client, endpoint or error mapping gets tests in the same change.
- Unit tests (`CardControl.Test`): small hand-written fakes of the `CardControl.Interface` contracts, no mocking
  library. `HttpClient` tests use a fake `HttpMessageHandler`.
- Integration tests (`CardControl.IntegrationTest`): `WebApplicationFactory<Program>`, fake downstream
  clients, locally signed test JWTs.
- Cover: happy path, 400 validation, 401/403 auth, 403/404 entitlement, 409/422/502/503 mapping, PAN never in
  responses or logs.
- Naming: `Method_Scenario_ExpectedResult`. One behaviour per test.
- `dotnet test` must be green before reporting done.

## 9. Self review and bug reports

After each change check: layering, async/`CancellationToken`, exceptions mapped, no sensitive data in logs or
responses, `[Authorize]` on card endpoints, status codes, DI lifetimes, tests present and green.

When a bug is found, report before fixing: **Bug** (file:line), **Impact**, **Severity**, **Fix plan**, **Test**.
Then wait for approval.

## 10. Known tech debt (do not fix unless asked)

- No retries/timeouts beyond `HttpClient.Timeout` (Polly resilience not added).
- Single decryption key; no key ring / rotation.
- Private key read from a PEM file; production would use Key Vault/HSM.
- `RequireHttpsMetadata` is off in Development only because the mock IdP is plain HTTP.
