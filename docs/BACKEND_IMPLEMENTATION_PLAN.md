# Backend Implementation Plan (V1)

## Document Info

- **Project:** Alpla Angola Internal Portal
- **Module:** Purchase Requests & Payments
- **Version:** 0.1.0 (Draft)
- **Status:** Planning
- **Related Specs:** `REQUIREMENTS_V1.md`, `STACK_DECISION.md`, `API_V1_DRAFT.md`, `DATA_MODEL_DRAFT.md`

---

## 1. Phased Implementation Plan

To ensure a methodical, testable rollout of the ASP.NET Core API, V1 backend development will proceed in five distinct phases:

### Phase 1: Foundation & Data Access

- Scaffold the `src/backend` Clean Architecture solution.
- Define `AlplaPortal.Domain` entities (Lookup tables, Auth structures).
- Setup `AlplaPortal.Infrastructure` with EF Core `DbContext` connecting to local SQL Server.
- Create EF Core initial migration.
- Write a `DataSeeder` to populate static lookups (RequestTypes, Statuses, Roles) on startup.

### Phase 2: Core Records (CRUD)

- Implement `AlplaPortal.Domain` entities for `Request` and `RequestLineItem`.
- Scaffold MediatR Handlers / Services for creating a Draft Request and managing its Line Items.
- Expose REST API endpoints (`GET`, `POST`, `PUT`) in `AlplaPortal.Api`.
- Implement `RequestOwner` policy validation to ensure users only modify their own Drafts.

### Phase 3: The State Machine & Audit Trail

- Implement the core workflow actions (`Submit`, `Approve`, `Reject`, `Cancel`).
- Enforce strict DB Transactions: when Status changes, force a correlated `RequestStatusHistory` insert.
- Implement the `allowedActions` computation logic inside Get Request queries to drive frontend UI state securely.

### Phase 4: Attachments

- Implement local Window Server File System Provider in `Infrastructure`.
- Build `multipart/form-data` endpoints for Upload/Download.
- Enforce max 10MB limits, allowed extensions, and soft-delete capabilities.

### Phase 5: Authentication Refinements

- Secure all endpoints using ASP.NET Core Policies matching `ACCESS_MODEL.md`.
- Wire up generic local JWT (or Windows Auth) properly for the Development environment.

---

## 2. Proposed Folder & Project Structure

The backend implements a practical subset of **Clean Architecture** to decouple business logic from the HTTP framework and EF Core.

```text
src/backend/
├── AlplaPortal.sln
├── AlplaPortal.Api/                 # The Web API (Presentation Layer)
│   ├── Controllers/                 # REST Endpoints
│   ├── Middleware/                  # Global Error Handling (ProblemDetails)
│   └── Program.cs                   # DI Setup
│
├── AlplaPortal.Application/         # Use Cases & Interfaces (Business Logic)
│   ├── DTOs/                        # Request/Response shapes
│   ├── Interfaces/                  # IRepository, IFileStorageService
│   ├── Requests/                    # MediatR Queries/Commands (or plain Services)
│   └── Behaviors/                   # Validation pipelines
│
├── AlplaPortal.Domain/              # Enterprise Entities
│   ├── Entities/                    # Request, RequestLineItem, User, Status
│   └── Enums/                       # Hardcoded Enums mapping to static Lookups
│
└── AlplaPortal.Infrastructure/      # External Concerns
    ├── Data/                        # ApplicationDbContext, Entity Configurations
    ├── Migrations/                  # EF Core SQL Migrations
    └── Services/                    # LocalFileStorageService (File IO)
```

---

## 3. Dependency List (NuGet Packages)

**AlplaPortal.Api:**

- `Serilog.AspNetCore` (Logging)
- `Swashbuckle.AspNetCore` (Swagger/OpenAPI for testing endpoints)

**AlplaPortal.Application:**

- `MediatR` (CQRS pattern for decoupled use cases)
- `FluentValidation.DependencyInjectionExtensions` (Input validation)

**AlplaPortal.Domain:**

- *(No dependencies. Pure C#.)*

**AlplaPortal.Infrastructure:**

- `Microsoft.EntityFrameworkCore.SqlServer`
- `Microsoft.EntityFrameworkCore.Design` (For Migrations)

---

## 4. Initial Entity List (Domain Layer)

Based on `DATA_MODEL_DRAFT.md`:

1. `User` / `Role` / `UserRoleAssignment`
2. `Department` / `Plant`
3. `RequestType` / `RequestStatus` / `Priority` / `Currency`
4. `Request` (Header)
5. `RequestLineItem` (Child records)
6. `RequestStatusHistory` (Audit Trail)
7. `RequestAttachment` (File Metadata)

---

## 5. Migration Strategy

All DDL schema changes must be exclusively managed by **EF Core Code-First Migrations**.

- Development: Developers use `dotnet ef migrations add` locally.
- Production: CI/CD or the system admin generates a raw SQL script via `dotnet ef migrations script` to be reviewed and executed against the Windows Server SQL instance natively (preventing app-startup migration permissions bugs in IIS).

---

## 6. Local Development Setup Steps (for C# Dev)

1. Install **.NET 8 SDK**.
2. Run SQL Server locally (LocalDB or Docker SQL Server Edge).
3. Clone repository and navigate to `src/backend/AlplaPortal.Api`.
4. Update `appsettings.Development.json` with the local SQL connection string.
5. Run `dotnet ef database update` to create the schema.
6. Run `dotnet run` (Auto-runs seed scripts upon startup).

---

## 7. Assumptions & Open Questions

- **Assumption:** MediatR will be used over standard injected Services for complex workflow actions (`ApproveRequestCommand`) to naturally implement cross-cutting concerns (Transactions, Validation) via Pipeline Behaviors.
- **Assumption:** The `User` lookup table relies on an initial static manual seed for V1 until true Entra ID SSO is implemented.
- **CQ-400:** Does the team want to utilize UUIDs (`Guid`) or incrementing local Integers (`int`) for primary keys on transactional tables like `Request`? (Recommendation: `Guid` for better security against ID-guessing, keeping a human-readable `RequestNumber` string column for UI).
