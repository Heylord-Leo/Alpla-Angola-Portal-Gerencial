# Version

## Current Version

v2.9.12

## Version History

- **2.9.12**: Fixed Line-Item Persistence Failure. Resolved a `DbUpdateException` (Database Constraint Violation) when adding, updating, or deleting line items. This was caused by missing mandatory fields (`ActorUserId`, `ActionTaken`, `NewStatusId`) in the `RequestStatusHistory` audit log entries created during these operations.
- **2.9.11**: Buyer OCR & System Logs Auth. Standardized the use of `apiFetch` in the Buyer OCR upload flow and Admin System Logs to ensure consistent JWT token propagation, resolving various `401 Unauthorized` errors.
- **2.9.10**: Auth Connectivity Fix. Restored API credential injection for the System Administrator Document Extraction Settings backend endpoints by migrating from native `fetch()` to authenticated `apiFetch()`.
- **2.9.9**: Fixed missing EF Core migrations for NotificationStatuses and InformationalNotifications, preventing backend crashes that broke global data flows including document extraction.
- **2.9.8**: Rollback: Payment Document-First Workflow. Reverted the "Document-First" creation flow for Payment Requests, restoring pre-existing strict validation constraints and legacy QuotationEntry integrations across the workspace.

- **2.9.7**: Zero-Trust Authorization & Legacy Persona Purge. Complete removal of "Visualizar Como" (User Mode) simulation. Implemented centralized Role Constants and claims-based authorization across all module controllers and the frontend API layer.
- **2.9.6**: Fixed visual display of auto-selected Plant when Company is changed.
- **2.9.5**: Corrected Plant/Company scope logic to ensure the Plant field remains interactive for users with multiple scopes, dynamically auto-selecting only when logically necessary.

- **2.9.4**: Plant-Scope-Based Request Restrictions. Implemented mandatory access control for the "Empresa" and "Planta" fields in the New Request flow, including frontend filtering, backend `403 Forbidden` enforcement, and data consistency validation.

- **2.9.3**: Improved User Profile Transparency. Transformed the "Meu Perfil" area into a professional account management interface with dynamic mapping for plants/departments, explicit status indicators, and integrated account actions.
- **2.9.2**: Restricted "Configurações" (Settings) menu access to `System Administrator` role only.
- **2.9.1**: Robust Notification Read-State UX. Implemented a role-aware notification engine with persistent read-states, operational workflow synchronization, and "Mark All as Read" / "Clear Read" management for optimized actor efficiency.
- **2.9.0**: Compras & Logística Management Cockpit. Implemented the new top-level workspace with a centralized dashboard (KPIs, Attention Panel, Quick Actions) and an interactive "Manual de Operação" side drawer for operational guidance.
- **2.8.0**: Account & Notification Modernization. Implemented a read-only Profile side drawer, integrated the "Alterar Palavra-passe" flow into the AppShell, and launched a real-time actionable notification engine for operational actors (Buyers, Approvers, Finance, Receiving).
- **2.7.0**: UI/UX Standardization Pass. Brought Login and User Management screens to the standardized UI design. Converted User Management editing to a Drawer-based pattern.
- **2.6.0**: User Management Administration UI. Integrated a high-density management table for users, roles, and plant/department scopes with stricter Local Manager subset logic.
- **2.5.1**: "Aguardando Pagamento" KPI Card.
- **2.5.0**: Sidebar & Navigation Redesign. Implemented a professional collapsible sidebar with dynamic logo headers and smooth layout transitions.
- **2.4.1**: Synchronized Urgency Indicators. Integrated overdue and due-soon visual logic into the Buyer / Gestão de Cotações workspace with refined finalized status exclusions.
- **2.4.0**: Formalized KPI Dashboard & Summary Cards Standard. Added normative directives and UI standards for robust dashboard patterns.
- **2.1.0**: Management Dashboard KPIs. Integrated interactive KPI summary cards with a unified backend data flow and standardized design.
- **2.0.6**: Improved Requests List Filtering. Updated `RequestsController` to filter by strict Request Header `PlantId`.
- **2.0.5**: Fixed Backend Plant Persistence. Corrected DTOs and RequestsController to ensure `PlantId` is saved and projected correctly across the request lifecycle.

- **0.4.0**: Consolidated Urgency Fields. Removed redundant 'Priority' Master Data in favor of a definitive 'Grau de Necessidade do Pedido' at the Request Header level. Added numeric 'Prioridade do Item' scaling to Line Items for granular purchasing triage.
- **0.3.1**: Fixed Line Item validation bug where ASP.NET 400 errors caused the `RequestEdit` line item modal to unmount. Separated formal Request Status from the current Stage Actor in the main requests list table for UI clarity.
- **0.3.0**: Added Project-wide UX Standardization (Inline Form Validation and Reusable Currency Input Masking).

Semantic Versioning (MAJOR.MINOR.PATCH)

- **PATCH**: correções, pequenos ajustes seguros, melhorias sem impacto funcional relevante
- **MINOR**: novas funcionalidades, novos fluxos, novos scripts, mudanças compatíveis
- **MAJOR**: mudanças que quebram compatibilidade, redesign relevante, alteração de contrato/schema

## Current Status

Active Development

## Changelog

## [v2.9.12] - 2026-03-28

### Fixed
- **Line-Item Persistence Bug**: Fixed a regression in `RequestsController` where adding, updating, or deleting line items failed with an Entity Framework persistence error. The root cause was identified as a database constraint violation in the `RequestStatusHistory` table; the logic was attempting to create audit log entries without providing mandatory non-nullable fields: `ActorUserId`, `ActionTaken`, and `NewStatusId`.
- **Audit Consistency**: Enforced standardized `ActionTaken` codes (`ITEM_ADDED`, `ITEM_UPDATED`, `ITEM_REMOVED`) for all line-item lifecycle events.

## [2.9.11] - 2026-03-28

### Fixed
- **Buyer OCR Authentication**: Standardized the quotation document upload flow in `BuyerItemsList.tsx` to use the centralized `api.attachments.upload` utility, ensuring consistent JWT token propagation and resolving `401 Unauthorized` errors.
- **Admin Logs Authentication**: Refactored `SystemLogs.tsx` to replace raw `fetch()` calls with the authenticated `apiFetch()` wrapper, securing administrative access to backend logs.
- **API Client Diagnostics**: Improved `handleApiError` in the frontend API client to provide more descriptive feedback for `401` (Unauthorized) and `403` (Forbidden) responses without exposing sensitive credential data.

## [2.9.10] - 2026-03-28

### Fixed

- **Document Extraction Settings Auth**: Resolved a persistent `401 Unauthorized` issue on the `/settings/document-extraction` page. The frontend `api.ts` module was incorrectly using a raw `fetch()` instead of `apiFetch()` for `admin` routes, causing the requests to reach the backend without the user's `Authorization: Bearer` jwt token. The admin endpoint `DocumentExtractionSettingsController` correctly requires `System Administrator` roles, but the lack of a token caused immediate rejections. The OCR engine used in business screens was unaffected, as that endpoint used the correct authenticated pipeline.

## [2.9.9] - 2026-03-28

### Fixed

- **Plant Auto-Selection Display**: Implemented localized state synchronization in the New Request screen. When a Company selection results in exactly one authorized Plant, the system now automatically selects and visibly displays that Plant in the dropdown, eliminating the "locked but empty" visual state.

## [2.9.5] - 2026-03-28

## Last Updated

2026-03-28 (Compras & Logística Cockpit)

## Version Notes

- Introdução de Manutenção de Dados Mestres (Soft Delete, Prevenção de Duplicatas).
- Novo fluxo contínuo de criação: Cabeçalho -> Itens de Linha.
- Integração do "Grau de Necessidade" (Need Level) na entidade `Request`.
