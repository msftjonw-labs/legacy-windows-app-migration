# Legacy Orders modernization plan

This plan moves the [legacy baseline](../README.md) from a .NET Framework 4.8
WinForms desktop application to:

- a browser frontend hosted on **Azure Static Web Apps**;
- an ASP.NET Core **.NET 10 backend in a Windows container on Azure Container
  Instances (ACI)**;
- Azure-managed data, document storage, identity, and telemetry.

It uses two GitHub Copilot agents, but they do not perform the whole
re-architecture automatically. Some steps require custom prompts and
engineering review.

## Executive summary

| Goal | Primary tool | Fit | Key constraint |
|---|---|---|---|
| .NET Framework 4.8 to .NET 10 | **GitHub Copilot upgrade** | Good | The upgraded app remains a desktop WinForms application |
| Prepare Azure dependencies | **GitHub Copilot modernization** | Partial | Predefined tasks cover storage, identity, database, and telemetry patterns, not WinForms-to-web conversion |
| Windows backend on ACI | Modernization deployment plus custom instructions | Feasible, needs a spike | ACI documents Windows Server 2019-family images only; .NET 10 GA images target LTSC 2022/2025 |
| Cloud-native UI | Custom prompts, possibly assisted by Copilot | Manual design work | Neither agent has a predefined WinForms-to-browser scenario |

Recommended target:

```text
Browser
  |
  | HTTPS: Blazor WebAssembly UI, Microsoft Entra sign-in
  v
Azure Static Web Apps (Standard)

Browser -- HTTPS + Entra bearer token --> Azure API Management Standard v2
                                             | CORS, JWT validation, throttling
                                             | VNet integration
                                             v
                         Private ACI Windows container group
                         ASP.NET Core .NET 10, x64
                         Server Core LTSC 2019
                         Registered .NET COM pricing server
                                             |
                    Managed identity / private networking
                         |                         |
                    Azure SQL Database       Azure Blob Storage
                         |
               Application Insights via OpenTelemetry
```

## Findings from the Microsoft documentation

### GitHub Copilot upgrade

The upgrade agent supports Windows Forms projects and upgrades .NET Framework
projects to .NET 8 or later. It also offers SDK-style conversion. It runs
assessment, upgrade options, planning, and executable tasks. It keeps state in
`.github/upgrades/{scenarioId}` and commits progress.

Use it for:

- converting both projects to SDK-style;
- targeting .NET 10;
- updating build and test infrastructure;
- fixing compatibility problems.

Do not expect it to convert WinForms screens into a web frontend. Its web
conversion scenario is Web Forms to Blazor, not WinForms to Blazor.

Source: [GitHub Copilot upgrade overview](https://learn.microsoft.com/en-us/dotnet/core/porting/github-copilot-upgrade/overview).

### GitHub Copilot modernization

The modernization agent assesses Azure readiness, applies predefined migration
tasks, builds and resolves issues, and assists with deployment. If an Azure
migration requires a newer .NET version, it invokes the upgrade agent.

Relevant predefined tasks:

| Legacy concern | Predefined task |
|---|---|
| Local attachment and invoice files | Migrate to Azure Blob Storage |
| Credentials, if introduced | Migrate to Managed Identity and Azure Key Vault |
| `System.Diagnostics.Trace` file logging | Migrate to OpenTelemetry on Azure |
| User access | Migrate to Microsoft Entra ID, adapted for a new API and SPA |

`orders.json` is not a recognized database source. Use a custom task to move
it to Azure SQL Database with managed identity, after the repository/service
layer exists.

Sources:

- [GitHub Copilot modernization overview](https://learn.microsoft.com/en-us/dotnet/azure/migration/appmod/overview)
- [Deploy with GitHub Copilot modernization](https://learn.microsoft.com/en-us/dotnet/azure/migration/appmod/deploy)

## Critical ACI constraints

Resolve these before committing to production use of ACI.

| Constraint | Consequence for this app | Plan |
|---|---|---|
| ACI documents Windows Server 2019-family base images: Server Core `ltsc2019`, Nano Server `1809`, or Windows `1809`. | Official .NET 10 GA ASP.NET Windows images are LTSC 2022/2025. | Use `mcr.microsoft.com/windows/servercore:ltsc2019` with a **self-contained** `win-x64` publish. |
| .NET 10 supports Windows Server Core 2019 on **x64**. | The current x86 COM component cannot load into an x64 backend process. | Rebuild the COM server as x64 using .NET COM hosting. |
| Windows ACI does not support volume mounts or multiple containers per group. | No local persistent data or sidecars. | Make the backend stateless; use Azure SQL and Blob Storage. |
| Windows ACI lacks Azure Monitor resource-usage metrics. | Less platform observability. | Instrument with OpenTelemetry and Application Insights. |
| VNet-deployed ACI cannot have a public IP or DNS label and requires NAT Gateway for outbound access. | APIM must reach ACI privately. | Use APIM Standard v2 outbound VNet integration and a NAT Gateway for ACI. |
| ACI private IPs can change after recreation. | APIM's backend URL can become stale. | Update the APIM backend during deployment; evaluate an internal Standard Load Balancer for a stable endpoint. |

Windows Server 2019 is in extended support, not mainstream support. ACI is
appropriate for this modernization experiment and small isolated workloads.
For long-term production, evaluate AKS Windows node pools or another Windows
container platform with Windows Server 2022/2025 support.

Sources:

- [ACI overview: Windows feature limitations](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-overview)
- [ACI FAQ: supported Windows base images](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-faq)
- [ACI virtual network constraints](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-virtual-network-concepts)
- [.NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- [APIM outbound VNet integration](https://learn.microsoft.com/en-us/azure/api-management/integrate-vnet-outbound)

## Phase 0: Prepare the repository and tools

1. Put this project in its **own Git repository**. It is currently untracked
   inside a repository rooted at the user profile. The upgrade agent creates
   branches and commits, so do not run it against the profile-level repository.
2. Commit the legacy baseline and record the expected results:
   - `.\build.ps1 -Test`
   - `.\bin\LegacyOrders.Tests.exe --com --ui`
   - `PO-1001`: subtotal 2400.00, tax 198.00, total 2598.00 USD
3. Install the .NET 10 SDK.
4. Install the upgrade and modernization agents in Visual Studio, VS Code, or
   GitHub Copilot CLI. Use Guided mode for this first run.
5. For image builds, use either:
   - a Windows host with Docker in Windows-container mode; or
   - ACR Tasks with `--platform windows`.

## Phase 1: Upgrade to .NET 10 with the upgrade agent

### Objective

Produce an SDK-style, .NET 10 version of the existing WinForms application with
behavior parity. Do not redesign the UI in this phase.

### Prompt

```text
@upgrade Upgrade this solution from .NET Framework 4.8 to .NET 10.
Use Guided mode and a bottom-up strategy.

Convert projects to SDK-style. Target net10.0-windows for WinForms and
Windows/COM-specific projects.

Preserve behavior:
- Draft -> Approved -> Invoiced workflow;
- invariant decimal parsing and midpoint-away-from-zero rounding;
- PO-1001 totals: 2400.00 subtotal, 198.00 tax, 2598.00 total;
- CSV validation and atomic import;
- atomic JSON save, backup, process lock, and corrupt-data preservation;
- attachment validation, 25 MiB limit, and generated filenames;
- HTML encoding in invoices.

Do not remove COM pricing. Convert it into a .NET COM server using comhost.
Treat x64 as the container target. If x86 desktop compatibility is retained,
document separate x86 and x64 registration and validation.

Replace build.ps1 compilation with dotnet build/test. Convert TestRunner into a
standard test project without weakening assertions. Keep COM and WinForms
integration tests in a Windows-only category.

Do not migrate to Azure yet. Stop after validation and summarize all behavior
changes and unresolved risks.
```

### Expected upgrade decisions

| Area | Expected outcome |
|---|---|
| Project format | SDK-style projects |
| Desktop target | `net10.0-windows`, `UseWindowsForms=true` |
| COM server | `EnableComHosting=true`; register `*.comhost.dll` with the matching-bitness `regsvr32` |
| COM activation | Keep CLSID/ProgID; validate late binding and `dynamic`; embed a type library if required |
| Office | Keep optional automation in the desktop app for now |
| Tests | Unit tests plus Windows-only COM/UI tests |

### Exit gate

- Upgraded app builds with warnings treated as errors.
- All baseline tests pass.
- COM activation passes for each supported bitness.
- WinForms manual workflow still passes.
- Commit the `.github/upgrades/` state with the upgraded code.

## Phase 1.5: Separate business logic from WinForms

This is a custom refactoring phase. It makes the code useful to the
modernization agent and prevents UI event handlers from becoming API logic.

Create:

| Project | Target | Responsibility |
|---|---|---|
| `LegacyOrders.Domain` | `net10.0` | Order entities, validation, state transitions |
| `LegacyOrders.Application` | `net10.0` | Use cases: import, save, calculate, approve, invoice, attach |
| `LegacyOrders.Documents` | `net10.0` | HTML invoice and headless DOCX/XLSX generation |
| `LegacyOrders.Pricing.Com` | `net10.0-windows` | COM-visible pricing server |
| `LegacyOrders.Infrastructure.Local` | `net10.0-windows` | Existing JSON, local files, desktop compatibility |
| `LegacyOrders.Desktop` | `net10.0-windows` | Temporary WinForms client |

Use interfaces such as `IOrderRepository`, `IDocumentStore`, `IPricingEngine`,
`IInvoiceRenderer`, and `IExportGenerator`. WinForms should call application
services, not storage or pricing APIs directly.

Prompt:

```text
Refactor the upgraded .NET 10 solution so WinForms contains only presentation
logic. Extract domain rules, use cases, pricing, document generation, and
storage behind interfaces. Preserve all tests and the existing WinForms
workflow. Add focused tests for every state transition before moving it.
Do not add Azure dependencies in this step.
```

## Phase 2: Build and containerize the backend

### Backend API

Add `LegacyOrders.Api`, an ASP.NET Core .NET 10 API using the application layer.
Use `net10.0-windows`, `win-x64`, and a self-contained publish for ACI.

| Endpoint | Purpose |
|---|---|
| `GET /api/orders` | List orders |
| `POST /api/orders` | Create draft |
| `GET /api/orders/{id}` | Get order with `ETag` |
| `PUT /api/orders/{id}` | Update draft with `If-Match` |
| `POST /api/orders/import` | Validate and atomically import CSV |
| `POST /api/orders/{id}/calculate` | Calculate through COM pricing |
| `POST /api/orders/{id}/approve` | Approve draft |
| `POST /api/orders/{id}/invoice` | Generate invoice and transition to Invoiced |
| `POST /api/orders/{id}/attachments/upload-requests` | Validate metadata and issue a short-lived upload grant |
| `POST /api/orders/{id}/attachments/{attachmentId}/complete` | Verify upload and associate attachment |
| `GET /api/orders/{id}/attachments/{attachmentId}` | Authorized download |
| `GET /api/orders/{id}/invoice` | Download invoice |
| `GET /api/orders/{id}/exports/xlsx` | Generate workbook without Office |
| `GET /api/orders/{id}/exports/docx` | Generate Word invoice without Office |
| `GET /health/live`, `GET /health/ready` | Liveness and dependency readiness |

API requirements:

- Return RFC 7807 problem details for validation and conflict errors.
- Use optimistic concurrency, not the single-process file lock.
- Enforce the same validation and transitions as the desktop app.
- Use idempotency keys for import, approval, and invoice generation.
- Never accept client-provided storage paths or blob names.
- Authorize each operation by role, for example `Orders.Editor` and
  `Orders.Approver`.

### Azure modernization tasks

Run tasks one at a time and validate between them:

1. **Migrate to Azure Blob Storage** for attachments, invoices, and exports.
   Use private containers. Use short-lived, least-privilege SAS for browser
   transfers, or stream through the API if the files stay small.
2. **Custom Azure SQL task** to replace `orders.json`. Use EF Core migrations,
   managed identity, transactions, unique order references, and `rowversion`.
3. **Migrate to OpenTelemetry on Azure** for traces, logs, metrics, and
   correlation IDs.
4. **Migrate to Microsoft Entra ID** for API authentication and app roles.
5. **Managed Identity and Key Vault** only for any credential that cannot use
   Entra authentication. Do not introduce Office or storage keys.

Example prompt:

```text
Use GitHub Copilot modernization. Run predefined task:
Migrate local file system file I/O to Azure Blob Storage.

Scope only the backend API and infrastructure projects. Preserve the
25 MiB limit, allowed extensions, generated object names, private containers,
and authorization checks. Use managed identity and Azure.Identity. Do not use
storage account keys or connection strings with keys. Add tests with a fake
document store and an Azurite integration test. Stop after build and tests.
```

### Windows container

The image must target ACI-compatible Windows Server 2019:

```dockerfile
FROM mcr.microsoft.com/windows/servercore:ltsc2019
WORKDIR C:/app
COPY publish/ C:/app/
RUN C:\Windows\System32\regsvr32.exe /s C:\app\LegacyOrders.Pricing.Com.comhost.dll
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["C:\\app\\LegacyOrders.Api.exe"]
```

This is a design starting point, not a validated Dockerfile. Container spike
acceptance criteria:

- `regsvr32` succeeds during the image build.
- A request-level test activates COM from multiple concurrent API requests.
- Totals for `PO-1001` match the baseline.
- The image is below ACI's 15 GB limit.
- Startup, readiness, and shutdown are reliable.

Do not install or automate Word/Excel in the container. Replace Office
automation with OpenXML-based generation. Do not run Explorer, shell verbs,
message boxes, or other desktop UI code in the backend.

### ACI deployment prompt

```text
Use #appmod-generate-architecture-diagram and #appmod-get-plan.
Deploy LegacyOrders.Api to Azure using Bicep.

Hosting service is Azure Container Instances, osType Windows. Do not change
the host to Container Apps, App Service, or AKS without asking.

Use a self-contained win-x64 publish on
mcr.microsoft.com/windows/servercore:ltsc2019. Push to Azure Container
Registry and pull without admin credentials.

Deploy the container group into a dedicated delegated subnet. Do not assign a
public IP. Configure NAT Gateway for outbound access.

Create Azure SQL Database and Blob Storage with Entra authentication and
private endpoints. Grant only the required managed-identity roles.

Create APIM Standard v2 with outbound VNet integration to ACI. Configure CORS
for the Static Web Apps origin, validate Entra tokens, apply rate limits, and
update the backend URL after each ACI deployment.

Configure Application Insights. Validate readiness, COM pricing, database,
storage, and the PO-1001 workflow before reporting success.
```

Review the generated plan before execution. Reject plans that expose ACI
publicly, enable ACR admin credentials, use storage keys, or skip validation.

## Phase 3: Re-architect the frontend

### Recommended host

Use **Azure Static Web Apps Standard** with **Blazor WebAssembly**.

Why:

- The UI becomes static assets served from a managed global host.
- The UI has no server-side Windows dependency.
- Blazor lets the team reuse C# DTOs and validation rules.
- Static Web Apps supports custom domains, managed TLS, environments, and
  GitHub-based deployment.

Static Web Apps managed APIs cannot host this Windows backend. Its supported
linked backends do not include ACI, and network-isolated linked backends are
not supported. Therefore, call APIM directly from the browser with an Entra
access token and APIM CORS.

Source: [Static Web Apps API support](https://learn.microsoft.com/en-us/azure/static-web-apps/apis-overview).

Use **Azure Container Apps** instead only if the team chooses a server-rendered
Blazor or other server-side UI. It must still call the containerized backend
API rather than contain business logic.

### Desktop-to-web behavior mapping

| WinForms/Windows behavior | Cloud frontend behavior |
|---|---|
| WinForms grid and form | Blazor order list, editor, and editable line grid |
| Message boxes | Inline validation, confirmation dialogs, toast notifications |
| Open-file dialog for CSV | Browser file picker or drag-and-drop upload |
| Explorer CSV context menu | Upload page; optional Windows companion only if still required |
| Copy local attachment | Direct upload to private Blob Storage, then completion API |
| Open attachment with Shell | Authorized download or browser preview |
| Reveal in Explorer / Order folder | Attachment list and download links |
| Launch HTML invoice | Display invoice page, print, or download |
| Word/Excel COM automation | Server-generated DOCX/XLSX downloads |
| Local process lock | Server-side concurrency and conflict UI |

### Frontend-to-backend communication

1. User signs in with Microsoft Entra ID through MSAL in Blazor WebAssembly.
2. Blazor requests an access token for the `LegacyOrders.Api` scope.
3. Blazor calls `https://<apim-domain>/orders/...` with the bearer token.
4. APIM validates issuer, audience, expiration, signature, and required role.
5. APIM allows only the Static Web Apps production and custom-domain origins.
6. APIM forwards to the private ACI endpoint over VNet integration.
7. The API validates the token again and enforces resource authorization.
8. The API uses managed identity for SQL and Storage.

Do not rely only on CORS for security. CORS controls browser reads; it does not
authenticate callers.

Configure:

- typed API client generated from OpenAPI;
- request timeouts and retry only for safe operations;
- idempotency keys for unsafe retries;
- correlation IDs shown in user-visible error details;
- `ETag`/`If-Match` conflict handling;
- upload progress and size validation before upload;
- background jobs for any invoice/export work that can exceed request timeouts.

## End-to-end validation

| Area | Required evidence |
|---|---|
| Business parity | Same totals, transitions, CSV rejections, and invoice content as baseline |
| COM | Registered COM server activates in Windows ACI under concurrent requests |
| Security | Anonymous, wrong-audience, and insufficient-role requests are rejected |
| Networking | ACI has no public endpoint; APIM can reach it; outbound uses NAT Gateway |
| Data | Concurrent edits produce conflicts, not lost updates |
| Documents | Private blobs; no traversal; size/type validation; authorized downloads |
| Browser | Create, import, edit, calculate, approve, invoice, upload, and download work |
| Recovery | Recreating ACI loses no committed orders or documents |
| Operations | Health checks, OpenTelemetry traces, logs, and correlation IDs are visible |

## Responsibility split

| Work | Upgrade agent | Modernization agent | Custom engineering |
|---|---:|---:|---:|
| SDK-style conversion | Yes | Via upgrade | Review |
| .NET 10 upgrade | Yes | Via upgrade | Review |
| COM hosting redesign | Assist | Assist | Required |
| WinForms logic extraction | Assist | No predefined task | Required |
| ASP.NET Core API | Assist | Assist | Required |
| Blob Storage | No | Predefined task | Review |
| SQL migration from JSON | No | Custom prompt | Required |
| OpenTelemetry | No | Predefined task | Review |
| Entra authentication | No | Predefined task adapted | Required |
| Windows ACI deployment | No | Custom deployment prompt | Required review |
| Static Web Apps frontend | Assist | Assist | Required |

## Recommended order

1. Commit the baseline in a dedicated repository.
2. Run the upgrade agent to .NET 10 WinForms; prove parity.
3. Extract business logic from WinForms.
4. Spike the .NET 10 x64 COM server in a Windows Server 2019 container.
5. Build the ASP.NET Core API.
6. Run modernization tasks for Blob, SQL, OpenTelemetry, and Entra.
7. Deploy private ACI behind APIM.
8. Build and deploy the Blazor WebAssembly frontend to Static Web Apps.
9. Run the end-to-end validation suite.
10. Retire the WinForms client only after workflow parity is accepted.
