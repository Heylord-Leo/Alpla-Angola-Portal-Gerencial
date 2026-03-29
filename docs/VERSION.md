# Version

## Current Version

v2.9.13

## Version History

- **2.9.13**: Modal Layering Fix. Standardized z-index stacking for User Management, Approval, and Receiving modals using React Portals.
- **2.9.12**: Line-Item Persistence Bug. Fixed audit log constraint violations during line-item updates.
- **2.9.11**: Buyer OCR & Admin Logs Auth standardization.
- **2.9.10**: Auth Connectivity Fix. Restored API credential injection for Admin settings.

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

## [v2.9.13] - 2026-03-29

### Fixed
- **Modal Layering & Stacking Context**: Resolved a critical UI conflict where the sticky Topbar (z-index 100) obscured modal dialogs and side drawers. Implemented `DropdownPortal` in `UserManagement.tsx`, `ApprovalModal.tsx`, and `ReceivingModal.tsx`.
- **Import Resolution**: Fixed a naming collision in `UserManagement.tsx` between the `Link` icon and the `Link` routing component.

## [v2.9.12] - 2026-03-28

### Fixed
- **Line-Item Persistence Bug**: Fixed audit log entries missing mandatory fields in `RequestsController`.

## [v2.9.11] - 2026-03-28

### Fixed
- **Buyer OCR Authentication**: Standardized upload flow to use authenticated utility.
- **Admin Logs Authentication**: Secured backend log access.

## [v2.9.10] - 2026-03-28

### Fixed
- **Document Extraction Settings Auth**: Resolved 401 Unauthorized issue by migrating to `apiFetch()`.

## Last Updated

2026-03-29 (Modal Layering Fix)

## Version Notes

- Introdução de Manutenção de Dados Mestres (Soft Delete, Prevenção de Duplicatas).
- Novo fluxo contínuo de criação: Cabeçalho -> Itens de Linha.
- Integração do "Grau de Necessidade" (Need Level) na entidade `Request`.
