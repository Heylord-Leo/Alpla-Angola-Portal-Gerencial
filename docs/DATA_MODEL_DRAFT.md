# Logical Data Model Draft (V1)

## Document Info

- **Project:** Alpla Angola Internal Portal
- **Module:** Purchase Requests & Payments
- **Version:** 0.6.0
- **Status:** Draft — Partially Implemented (Core Workflow & Master Data)
- **Related Specs:** `REQUIREMENTS_V1.md`, `ACCESS_MODEL.md`, `ARCHITECTURE.md`

---

## 1. Purpose

This document defines the draft **Logical Data Model** for the V1 Purchase Requests & Payments module. It focuses on entities, attributes, and relationships necessary to support the defined workflows, access rules, audit trails, and attachments.

*Intentionally excluded from V1 Draft:* Physical SQL data types, index tuning strategies, and exact physical schemas for future integrations.

---

## 2. Modeling Principles (V1)

- **Modular & Extensible:** Built around a core `Request` pattern that can scale to future modules (e.g., Vacation, Maintenance).
- **Auditability First:** Immutable historical tracking using state-aware history tables.
- **Workflow-Aware:** Built to explicitly support multi-stage approval states and role-based custody.
- **Integration-Ready:** Future AlplaPROD and Primavera fields are stubbed as nullable string references to prevent tight coupling in V1.

---

## 3. Core Entities (V1)

1. **User (or UserReference):** Represents the human actor. *(Note: If auth is handled by Entra/AD, this may just be a reference table to store local preferences or role mappings).*
2. **Role:** Defines system access levels.
3. **UserRoleAssignment:** Junction linking Users to Roles.
4. **Department:** Business unit for scoping approvals. ✅ Implemented.
5. **Plant:** Physical location of the request requirement. ✅ Implemented.
6. **Request:** The central transactional record.
7. **RequestStatusHistory:** The immutable audit timeline of actions.
8. **RequestAttachment:** File metadata linked to a request.
9. **RequestLineItem:** Individual items or services within a request.
10. **Supplier:** Managed Master Data with dual-code identification (`PortalCode` and `PrimaveraCode`). ✅ Implemented.
11. **Lookup Tables:** Small static or admin-managed tables (`RequestType`, `Status`, `Currency`, `Unit`, `NeedLevel`, `Department`, `Plant`, `Supplier`, `LineItemStatus`).

*(Note: In V1, Request Comments are stored directly on the `RequestStatusHistory` record to ensure every comment is tied to a specific workflow action/timeline event).*

---

## 4. Entity Details

### 4.1 Lookup Entities

Used to enforce relational integrity and populate UI dropdowns.

- `RequestType` (e.g., Purchase, Payment)
- `Status` (e.g., Draft, Pending Approval, Approved, Rejected, Cancelled)
- `NeedLevel` (e.g., Baixo, Normal, Urgente, Crítico)
- `Currency` (e.g., AOA, USD, EUR)
- `Unit` (e.g., UN, KG, L, EA). Has `AllowsDecimalQuantity` boolean to enforce integer vs fractional usages.
- `Department` ✅ Implemented with full CRUD + soft-delete
- `Plant` ✅ Implemented with full CRUD + soft-delete
- **Supplier:** ✅ Implemented with dual codes. `PortalCode` (Internal, Unique, Required) and `PrimaveraCode` (External ERP, Optional, Indexed).

*Note: All Master Data lookup tables must implement an `IsActive` (Boolean) column to support soft-deactivation.*

### 4.2 Auth & Scoping Entities

- **User:** `Id`, `Email`, `FullName`, `IsActive`, `DepartmentId` (Optional default).
- **Role:** `Id`, `RoleName` (e.g., 'Solicitante', 'Aprovador de Área').
- **UserRoleAssignment:** `UserId`, `RoleId`, `DepartmentScopeId` (Optional: if an Approver is only responsible for a specific department).

### 4.3 Core: Request Entity (Detailed)

*The central document for the workflow.*

**Header & Metadata:**

- `Id` (PK, UUID or Identity)
- `RequestNumber` (String, Auto-generated human-readable ID, e.g., 'PRQ-2023-0001')
- `RequestTypeId` (FK) -> `RequestType`
- `Title` (String, Required)
- `Description` (Text, Required)
- `RequesterId` (FK) -> `User`
- `DepartmentId` (FK) -> `Department`
- `PlantId` (FK) -> `Plant`
- `NeedLevelId` (FK) -> `NeedLevel` (Grau de Necessidade do Pedido)
- `SupplierId` (FK, Nullable) -> `Supplier` *(Mandatory if RequestType=PAYMENT)*
- `RequestedDate` (Date/Time)
- `NeedByDate` (Date)
- `StatusId` (FK) -> `Status`
- `CurrentResponsibleRoleId` (FK) -> `Role` *(Used to quickly query whose queue this sits in)*

**Financial Data (V1):**

- `CurrencyId` (FK) -> `Currency`
- `EstimatedAmount` (Decimal)
- `IsCapex` (Boolean, Default False)

**Workflow & Timestamps:**

- `CreatedAt` / `CreatedBy`
- `UpdatedAt` / `UpdatedBy`
- `SubmittedAtUtc` (Timestamp, Nullable) - Recorded when the request is officially submitted.
- `IsCancelled` (Boolean flag for soft terminal state)

**Future Integration Hooks (Nullable/Do not implement logic in V1):**

- `PrimaveraPONumber` (String)
- `AlplaProdReference` (String)
- `ExternalSyncStatus` (String)

### 4.4 RequestStatusHistory (Audit Timeline)

*Append-only table. Never update or delete rows here.*

- `Id` (PK)
- `RequestId` (FK) -> `Request`
- `ActorUserId` (FK) -> `User` (Who took the action)
- `ActionTaken` (String, e.g., "Submitted", "Approved", "Edited")
- `PreviousStatusId` (FK)
- `NewStatusId` (FK)
- `Comment` (Text, Optional depending on action)
- `CreatedAt` (Timestamp of the action)

### 4.5 RequestAttachment

*File metadata.*

- `Id` (PK)
- `RequestId` (FK) -> `Request`
- `FileName` (String)
- `FileExtension` (String)
- `FileSizeMBytes` (Decimal)
- `StorageReference` (String) *(Draft: Path to S3/Blob or local file server)*
- `UploadedByUserId` (FK) -> `User`
- `UploadedAt` (Timestamp)
- `IsDeleted` (Boolean - Soft delete support)

### 4.6 RequestLineItem

*Generic line individual items for the request (1:M with Request). Workflow and approvals occur exactly at the header level.*

- `Id` (PK)
- `RequestId` (FK) -> `Request`
- `LineNumber` (Integer) — *Technical row-order indicator. Not the business priority.*
- `ItemPriority` (String: `HIGH` / `MEDIUM` / `LOW`, Default `MEDIUM`) — *Business priority classification. Backend-validated.*
- `Description` (String, Required)
- `Quantity` (Decimal, Required) *(Validation: Must be whole integer if associated `Unit` has `AllowsDecimalQuantity=false`)*
- `UnitId` (FK) -> `Unit`
- `UnitPrice` (Decimal, Required)
- `TotalAmount` (Decimal) *(Calculated: Quantity * UnitPrice)*
- `CurrencyId` (FK) -> `Currency` *(Inherited from Request header; enforced by backend)*
- `LineItemStatusId` (FK) -> `LineItemStatus` ✅ Implemented.
- `SupplierName` (String, Optional)
- `Notes` (String, Optional)
- `IsDeleted` (Boolean — Soft delete)

### 4.7 LineItemStatus

*Master data for item lifecycle status. Read-only for requesters.*

- `Id` (PK)
- `Code` (String, Unique) — `WAITING_QUOTATION`, `PENDING`, `UNDER_REVIEW`, `ORDERED`, `PARTIALLY_RECEIVED`, `RECEIVED`, `CANCELLED`
- `Name` (String) — Portuguese display name
- `DisplayOrder` (Integer)
- `BadgeColor` (String) — UI color token (e.g., `blue`, `yellow`, `green`)
- `IsActive` (Boolean)

*Initial status assignment rules (backend-enforced):*

- `RequestType.Code = QUOTATION` → `WAITING_QUOTATION`
- `RequestType.Code = PAYMENT` → `PENDING`

---

## 5. Status & Workflow Modeling

The workflow relies on strictly pushing new records to `RequestStatusHistory` while updating the `StatusId` on the parent `Request` table.

- **Traceability:** To render the Timeline UI, query `RequestStatusHistory` where `RequestId = X`, ordered by `CreatedAt ASC`.
- **Rework Ownership:** The parent `Request.CurrentResponsibleRoleId` changes based on status (e.g., changes to 'Aprovador de Área' when submitted, back to 'Solicitante' if rejected).

---

## 6. Textual ERD (Relationships)

- **User** `1 : M` **Request** (as Requester)
- **User** `1 : M` **RequestStatusHistory** (as Actor)
- **User** `1 : M` **RequestAttachment** (as Uploader)
- **RequestType** `1 : M` **Request**
- **Status** `1 : M` **Request**
- **NeedLevel** `1 : M` **Request**
- **Department** `1 : M` **Request**
- **Plant** `1 : M` **Request**
- **Request** `1 : M` **RequestStatusHistory** *(Cascade Delete: False)*
- **Request** `1 : M` **RequestAttachment** *(Cascade Delete: True)*
- **Request** `1 : M` **RequestLineItem** *(Cascade Delete: True)*
- **User** `M : M` **Role** (via UserRoleAssignment)

---

## 7. Data Ownership / Visibility Support

This model directly supports `ACCESS_MODEL.md`:

- **Requester scoping:** Query `Request` where `RequesterId = CurrentUser.Id`.
- **Approver scoping:** Query `Request` where `Status = Pending Approval` AND `DepartmentId IN (Approver's Assigned Departments)`.
- **Finance/Purchaser scoping:** Query `Request` by specific `RequestTypeId` and `StatusId` (e.g., Approved Purchase requests).

---

## 8. Open Questions & Assumptions

- **Assumption:** Active Directory/Entra ID handles Auth. The `User` table here is just a local mirror synced on login for foreign key relationships.
- **Assumption:** Soft deletion is preferred for Attachments (`IsDeleted`) to preserve audit truth if a user removes a file mid-workflow.
- **Assumption:** The `Supplier` field is a managed Master Data entity (`Supplier` table) to ensure data integrity and future compatibility with ERP NIFs.
- **Assumption:** V1 includes a generic `RequestLineItem` entity for itemized inputs. However, all workflow and approvals occur strictly at the `Request` header level. Item-level partial approvals are out of scope for V1.

---

## 9. Next Step Recommendation

To move from this logical outline to implementation:

1. **Stakeholder Confirmation:** Review the remaining Open Questions and validate the decision to use a generic Line Items entity.
2. **Physical DB Design:** Translate this into physical SQL Server schemas (defining `VARCHAR` lengths, `DATETIME2` precision, and clustered indexing on foreign keys).
3. **ORM Models:** Generate Entity Framework (C#) or Prisma (Node) models mirroring this logical structure.
4. **Seed Data:** Create SQL scripts to populate the strict lookup tables (`Status`, `RequestType`, `Roles`).
