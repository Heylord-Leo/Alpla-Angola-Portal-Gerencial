# Alpla Gerencial Project Overview

## Objective

Internal portal for Purchasing and Payment Request management for Alpla Angola. The system digitizes the full flow: request creation, multi-level approvals, financial control, and audit traceability.

## Scope

- **Implemented Features (V1.1)**:
  - Purchase (Quotation) and Payment requests with multi-stage approval workflow.
  - Quotation Management (line items, suppliers, and cost centers).
  - Attachment upload (Proforma, P.O, Proofs) integrated into the workflow.
  - OCR for automatic data extraction from proforma invoices.
  - Managed Master Data: Units, Currencies, Needs, Departments, Plants, Suppliers, Cost Centers.
  - Operational Dashboard with status summaries and interactive guide.
- **Current Limitations (V1.1)**: No real-time bidirectional integration with Primavera/AlplaPROD (snapshots only).

## Folder Structure

- `docs/` – Project documentation.
- `src/frontend/` – React SPA (Vite + TypeScript).
- `src/backend/` – ASP.NET Core 8 Web API + EF Core.
- `tests/` – Automated tests (Unit and Integration).

## Next Steps

1. Stage 10 Completed: Introduction of Receiving Workspace.
2. Start Stage 11: Email/Teams notifications.
3. Advanced analytical dashboards.

- **Current Version**: v1.1.41 (Stage 10 - Receiving Workspace)
- **Status**: Stable (Receiving Workspace and RECEIVING Role Implemented)
