# PanelBridge — multi-panel conveyancing integration platform

A .NET 10 Azure Functions service that sits between a conveyancing firm's case-management system and multiple supplier "panels" (referral / instruction networks). It exposes one consistent REST/JSON API regardless of which panel a case originated on, and handles the differences between panels — REST vs. SOAP, XML vs. typed objects, panel-specific auth flows — entirely on the server side.

This repository is a **sanitised showcase** for one-on-one technical review. Credentials, internal hostnames, the client firm's identity, and Entra IDs have been removed or replaced with placeholders. The two supplier services this integrates with — **SortRefer** and **Econ (DigitalMove)** — are public B2B platforms and are named as-is so the architecture reads naturally. Architecture and code style are unchanged from the production version.

---

## What it does

- Accepts inbound REST calls from the firm's CMS for the full case lifecycle (`accept`, `cancel`, `complete`, `decline`, `reactivate`, `suspend`), case-handler management, notes, documents, milestones, and reference data
- Dispatches each call to the correct supplier panel based on which panel owns the case
- Maintains a local `caselookup` table that gives every case a panel-agnostic `universalId` (GUID) and bridges it to each panel's own reference
- Polls each panel hourly on the clock to pull pending instructions and case handlers into the local database without manual intervention
- Surfaces consistent JSON envelopes regardless of panel — XML-only suppliers are normalised server-side
- Documents every endpoint via OpenAPI; Swagger UI gated by signed-cookie auth with per-user passwords; programmatic API access via either an X-API-Key header or OAuth2 client_credentials against Entra ID

---

## Architecture highlights

### Panel abstraction
- `IPanelClient` defines the common contract (28 methods covering the whole spec)
- `PanelClientRegistry` resolves the right implementation by panel key at request time
- Concrete implementations:
  - **SortRefer** — REST/XML over HTTPS, typed `HttpClient` with `AddStandardResilienceHandler` for retry/timeout/circuit-break
  - **Econ** — SOAP/WCF with `TransportWithMessageCredential` binding, session-based auth (StartSession → cache the returned session-credential pair → use those on every subsequent InstructionManagementService call)

### Single-IP egress for whitelisted suppliers
Econ requires a static outbound IP for whitelisting. Function Apps rotate IPs by default, so Econ traffic routes through a small Squid proxy VM (locked-down NSG, single static public IP). The proxy address is plumbed through `EconOptions` and applied selectively to that panel's binding — SortRefer traffic continues to go out directly.

### Session caching
`EconClient` is a singleton with an internal `SemaphoreSlim`-guarded session cache. `BasicHttpBinding.UseDefaultWebProxy` is false and `ProxyAddress` is wired to the egress proxy. `StartSession` is called with the configured credentials; the returned `SessionUserName` / `SessionPassword` pair is cached for a configurable TTL and substituted into every subsequent `InstructionManagementService` call.

### Idempotency
Every `caselookup` row has a unique constraint on `(PanelId, PanelRef)`. The hourly polling job can re-process the same instruction list as many times as it likes without producing duplicates; new rows only land when a `PanelRef` hasn't been seen before.

### Strongly-typed responses + OpenAPI metadata
Every endpoint has a typed response model annotated for OpenAPI. The Swagger schema panel shows the exact shape of `data`, not a free-form `object`. Panel XML responses (instructions, documents, lenders, milestones, handlers) are normalised to JSON before leaving the boundary — the firm's CMS never sees raw panel XML.

### Auth (three layers)
1. **X-API-Key** — shared-secret header, constant-time compared, for programmatic callers
2. **OAuth2 client_credentials** — full Entra ID app registration, scope-bound tokens, served via the OpenAPI security definitions
3. **Swagger UI session cookie** — form login at `/api/swagger-login`, HMAC-signed cookie with configurable TTL, supports multiple users (each with their own password) bound from `PanelBridge:SwaggerUsers:N:Username/Password` config

### Polling timers
- `HandlerSync` — hourly at `:30`, pulls case handlers from every wired panel and upserts into the local table matched by email (Econ exposes only `PersonRef`, so an email-shaped surrogate is synthesised to satisfy the unique constraint)
- `InstructionPolling` — top of every hour, pulls pending instructions and inserts new caselookup rows with fresh universal GUIDs

### Feature flags
A simple `DocumentsFeature` service reads `Documents:Enabled` from config and short-circuits the three documents endpoints with 404 when disabled. Used to keep one environment as a documents-aware test bench while the live environment stays document-free per supplier contract.

### Audit-friendly fail modes
Panel calls return a typed `PanelOperationResult` distinguishing four outcomes:
- `Success`
- `Failure` — panel returned a structured failure (auth, business rule, etc.)
- `NotSupported` — the operation isn't part of this panel's contract (e.g. Econ has no `Reactivate`)
- `Unavailable` — transport / network / unparseable response

Each maps to a deterministic HTTP status and a stable response envelope.

---

## Tech stack

- **.NET 10** isolated-worker Azure Functions
- **Entity Framework Core 10** with SQL Server / Azure SQL provider; migrations + design-time factory included
- **Microsoft.Extensions.Http.Resilience** for HTTP-side retry/timeout/circuit
- **System.ServiceModel** for SOAP/WCF
- **OpenAPI / Swagger** via `Microsoft.Azure.Functions.Worker.Extensions.OpenApi`
- **Application Insights** for tracing + dependency capture

---

## Layout

```
src/PanelBridge/
├── Program.cs                  DI wiring, config binding, validated options
├── host.json
├── Domain/                     POCOs + enums (CaseLookup, CaseHandler, Panel, etc.)
├── Functions/                  HTTP + Timer triggers, request validation, response helpers
├── Models/
│   ├── Requests/               Validated request DTOs
│   └── Responses/              ApiResponse envelope + typed response shapes
├── Panels/
│   ├── IPanelClient.cs         Common contract
│   ├── PanelClientRegistry.cs  Resolves implementation by panel key
│   ├── SortRefer/              REST/XML panel implementation
│   └── Econ/                   SOAP panel implementation (with auto-generated WCF client)
├── Persistence/
│   ├── PanelBridgeDbContext.cs        EF Core model, constraints, seed
│   ├── PanelBridgeDbContextFactory.cs Design-time factory for migrations
│   └── Migrations/                    EF Core migrations with backfill SQL where relevant
└── Security/
    ├── AuthMiddleware.cs       API key + cookie + Easy Auth bypass
    ├── BridgeSecurityOptions.cs Strongly-typed config + multi-user list
    └── OpenApiSecurityFlows.cs  OAuth2 / OpenAPI flow definitions
```

---

## Running locally

1. Provision SQL Server (LocalDB works fine for dev)
2. Copy `src/PanelBridge/local.settings.example.json` to `src/PanelBridge/local.settings.json` and fill in real values
3. Apply migrations: `dotnet ef database update --project src/PanelBridge`
4. `func start` from `src/PanelBridge/` (requires Azure Functions Core Tools)
5. Swagger UI at `http://localhost:7071/api/swagger/ui`

---

## What's been scrubbed for the showcase

- All credentials — `local.settings.json` is not included; the example file ships with placeholder strings only
- Real supplier hostnames replaced with `<panel-host>`
- The egress proxy's public IP replaced with `<egress-proxy-host>`
- Real Entra IDs replaced with zero-GUIDs
- The supplier-specific WCF data-contract namespace strings have been generalised to `example.com/PanelB/...` — the code still compiles and the structure reads naturally, but no real internal namespace identifiers are exposed
- The client firm's spec documents, lender-specific test data, and CI workflows that named real Azure resources are excluded
- Git history starts fresh; production commit history is not exposed

The full production version is a private codebase. This showcase is intended for one-on-one technical review.
