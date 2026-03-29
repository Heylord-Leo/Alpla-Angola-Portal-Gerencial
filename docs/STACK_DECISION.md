# Technical Stack Decision (V1)

## Document Info

- **Project:** Alpla Angola Internal Portal
- **Module:** Purchase Requests & Payments
- **Version:** 0.1.0 (Draft)
- **Status:** Proposed
- **Related Specs:** `REQUIREMENTS_V1.md`, `ARCHITECTURE.md`

---

## 1. Decision Summary (V1)

**Final Recommended Stack:**

- **Backend:** ASP.NET Core 8.0 (C#) Web API
- **Frontend:** React (Next.js or Vite) with TypeScript
- **Database:** SQL Server (On-Premises)
- **ORM:** Entity Framework (EF) Core
- **Authentication:** Windows Authentication (Intranet) / JWT fallback, migrating to Entra ID in future
- **File Storage:** Local Server Filesystem (Windows Server) with paths stored in SQL Server
- **Deployment:** IIS (Internet Information Services) on Windows Server

**Rationale:**
The existing production infrastructure revolves around a Windows Server VM and SQL Server. ASP.NET Core provides first-class, native support for both IIS hosting and SQL Server via EF Core. It is strictly typed, highly secure by default, and excels at enterprise workflows (RBAC, audit trails) which are critical for procurement and finance.

---

## 2. Evaluated Options

### Option A: ASP.NET Core + React + SQL Server + EF Core (Recommended)

- **Strengths:** Native synergy with Windows Server/IIS. EF Core is the industry standard for SQL Server. Strongly typed backend ensures robust financial/workflow logic. Excellent built-in dependency injection, logging, and security middleware.
- **Weaknesses:** Steeper learning curve if the team is purely JavaScript-focused. Slightly heavier boilerplate than Node.js.
- **Fit for Windows Server On-Prem:** **Excellent.** ASP.NET Core hosts effortlessly in IIS.
- **Fit for Enterprise Workflows:** **Excellent.** Built-in policy-based authorization makes enforcing the complex matrix in `ACCESS_MODEL.md` straightforward.
- **Maintainability:** High. C# enforces structural rigor, making it easier for a small team to maintain years later without fear of dynamic typing errors.
- **Future Integrations:** Perfect for Primavera (often natively Windows/SQL based) and standard SOAP/REST APIs for AlplaPROD.

### Option B: Node.js (NestJS) + React + SQL Server + Prisma/TypeORM

- **Strengths:** Single language (TypeScript) across the entire stack. NestJS provides a structured, Angular-like backend architecture. Fast initial development.
- **Weaknesses:** Hosting Node.js robustly on IIS requires `iisnode` or setting up a reverse proxy (Nginx/IIS + PM2), adding operational complexity on Windows Server. SQL Server drivers for Node (like `mssql` or Prisma) are good, but occasionally lag behind EF Core in advanced enterprise edge cases.
- **Fit for Windows Server On-Prem:** **Moderate.** Node is historically Linux-first; Windows deployment requires slightly more manual service configuration.
- **Fit for Enterprise Workflows:** **Good.** NestJS supports guards and interceptors for RBAC.
- **Maintainability:** Good, assuming strict TypeScript adherence is enforced.

### Option C: Python (FastAPI/Django) + React + SQL Server

- **Strengths:** Extremely fast to write. Great if the team heavily leans toward Python scripts (aligning with the DOE Execution layer).
- **Weaknesses:** Django is highly opinionated and its ORM is tuned for PostgreSQL/MySQL; SQL Server support via `django-mssql-backend` exists but is a community package. FastAPI requires manual assembly of the enterprise architecture (auth, ORM pipelines). Hosting Python applications on IIS (via `wfastcgi` or waitress) is notoriously brittle compared to Linux/Gunicorn.
- **Fit for Windows Server On-Prem:** **Poor.** Python web hosting on Windows Server IIS is generally discouraged for mission-critical enterprise apps unless using Docker (which adds infrastructure overhead).
- **Maintainability:** Moderate. Python's dynamic nature for complex financial state-machines requires intense unit testing coverage.

---

## 3. Final Recommendation (V1)

**Selected:** Option A (ASP.NET Core + React)

**Why it's the best fit:**
The absolute constraint of deploying to an existing on-premises Windows Server VM with SQL Server makes ASP.NET Core the undisputed choice for operational stability. We eliminate the risk of brittle third-party hosting adapters (like `iisnode` or `wfastcgi`). Furthermore, the strictness of C# and EF Core perfectly aligns with the requirement to build an immutable workflow audit trail and strict role-based access controls for financial requests.

**Why alternatives were rejected:**
While Node.js and Python are excellent ecosystems, configuring their runtimes to run as perpetual, auto-restarting background services natively under IIS on Windows Server introduces unnecessary DevSecOps overhead for a small team.

---

## 4. Detailed V1 Stack Decisions

- **Backend Framework:** `.NET 8.0` (Long Term Support).
- **Frontend Framework:** `React 18+`. Recommend `Vite` for a pure SPA architecture, as SEO/SSR is irrelevant for an internal portal, simplifying IIS deployment to just serving static files.
- **ORM:** `Entity Framework Core 8.0`. Code-first approach to generate deterministic SQL migration scripts.
- **API Style:** standard `RESTful` JSON APIs.
- **Authentication:** Since this is an internal Alpla portal on a Windows network, V1 should look to utilize **Windows Authentication** (Intranet) if the IIS server is domain-joined to capture the user's AD identity automatically. Fallback: Local accounts with JWT. (Future: Migrate to Entra ID / OAuth2).
- **Authorization:** `ASP.NET Core Policies` based on Roles + Status validations in the MediatR/Service layer.
- **Attachment Storage:** Save files to a protected local directory on the Windows Server (e.g., `D:\AlplaPortal\Attachments\`). The `RequestAttachment` SQL table will store the relative filepath. Do *not* store binary blobs directly in SQL Server to prevent database bloat.
- **Logging:** `Serilog` writing to flat files (`.txt` rolling daily) and optionally the Windows Event Viewer for critical crashes. Business audit trails go to the SQL `RequestStatusHistory` table.
- **Configuration:** `appsettings.json` with environment-specific overrides (`appsettings.Production.json`). Passwords/Keys in Environment Variables on the server.
- **Deployment Strategy:**
  - Frontend: `npm run build` -> Copy `dist` folder to an IIS Site.
  - Backend: `dotnet publish` -> Copy to an IIS Virtual Application.
- **Testing Strategy:** `xUnit` + `Moq` for backend business logic (Approvals). `Vitest` or `Jest` for frontend component rendering.

---

## 5. High-Level Project Structure

Aligning with the DOE architecture where the Orchestrator works in the root, the actual application code will sit in `src/`.

```text
/
├── directives/               # DOE Rules
├── docs/                     # Project Documentation
├── execution/                # DOE Python scripts
├── src/
│   ├── backend/              # ASP.NET Core Web API
│   │   ├── AlplaPortal.Api/          # Controllers, Program.cs
│   │   ├── AlplaPortal.Core/         # Entities, Interfaces, Domain Logic
│   │   └── AlplaPortal.Infrastructure/ # EF Core DbContext, Migrations, File IO
│   └── frontend/             # Vite + React SPA
│       ├── src/
│       │   ├── components/   # Reusable UI (Buttons, Tables)
│       │   ├── features/     # Specific modules (Requests, Approvals)
│       │   ├── services/     # API Fetch calls
│       │   └── utils/        # Formatting, constants
├── tests/
│   ├── backend.tests/        # xUnit tests
│   └── frontend.tests/       # Vitest tests
└── .env
```

---

## 6. Risks and Mitigations

| Risk | Impact | Mitigation Strategy (V1) |
| :--- | :--- | :--- |
| **File Storage Growth** | Full disk crash | Implement strict 10MB limits per file (`REQUIREMENTS_V1`). Set up a Windows Server disk alert. |
| **Duplicate Submissions** | Double ordering | Disable buttons on frontend immediately on click. Implement idempotency keys or fast-fail status checks in the backend EF Core transactions. |
| **Permission Drift** | UI says Yes, API says No | Centralize action logic. API returns `allowedActions[]` for each row; frontend simply maps the array to UI buttons. |
| **Auth Migration to Entra ID** | High refactor cost | Abstract the `IUserService` so the backend relies exclusively on standard Claims. Swapping Windows Auth to Entra OIDC later only requires changing middleware, not business logic. |
| **Python DOE vs C# App** | Fragmentation | Ensure DOE Python scripts (`execution/`) interact with the C# backend strictly via REST APIs or direct SQL reads, not by sharing memory/libraries. |

---

## 7. Implementation Readiness Checklist

Approved next steps to begin immediate development:

- [ ] Log DEC-004 in `docs/DECISIONS.md`.
- [ ] Draft `docs/API_V1_DRAFT.md` (defining core REST endpoints).
- [ ] Scaffold `src/backend` with .NET 8 Web API and EF Core DbContext.
- [ ] Generate initial EF Core SQL Migration matching `DATA_MODEL_DRAFT.md`.
- [ ] Scaffold `src/frontend` with Vite/React/TypeScript.
- [ ] Create `seed.sql` for initial Lookups (Status, Types, Roles).
