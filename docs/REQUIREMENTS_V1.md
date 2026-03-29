# Requirements V1 — Purchase Requests & Payments

## Document Info

- **Project:** Alpla Angola Internal Portal
- **Module:** Purchase Requests & Payments
- **Version:** 0.1.0 (Draft)
- **Status:** Draft
- **Owner:** [Name]
- **Last Updated:** [YYYY-MM-DD]

---

## 1) Purpose

Define the functional and non-functional requirements for the **V1** implementation of the **Purchase Requests & Payments** module for Alpla Angola employees.

This V1 should support the end-to-end internal workflow for:

- creating purchase/payment requests
- approval routing
- status tracking
- attachments
- audit trail
- basic reporting/list views

---

## 2) Scope (V1)

### In Scope (V1)

- Create purchase/payment request
- Save as draft
- Submit request for approval
- Approval workflow (at least one approval layer, expandable later)
- Reject / approve actions with comments
- Request status tracking
- Attachment upload/download
- Request detail page with history/timeline
- Role-based action visibility (permission-aware UI)
- Basic list views (My Requests / Pending Approvals)
- Basic notifications (in-app and/or email placeholder)
- Audit trail of actions

### Out of Scope (V1)

- Full integration with AlplaPROD API (future phase)
- Full integration with Primavera API (future phase)
- Automatic PO creation in Primavera
- Advanced budget validation against live ERP data
- Mobile-first experience
- Advanced analytics / BI dashboards
- Multi-language UI (V1 will use Portuguese as primary UI language)

---

## 3) Business Goals (V1)

- Standardize the request and approval process
- Improve visibility of request status and responsibility
- Reduce informal requests (email/WhatsApp-only process)
- Create traceability (who did what, when, and why)
- Prepare a scalable base for future modules (Maintenance, Budget, Vacation)
- Enable future integration with AlplaPROD and Primavera via API layer

---

## 4) Users and Roles (Initial Access Model)

> Final role matrix may be expanded in `docs/ACCESS_MODEL.md`.

### 4.1 Roles (V1)

- **Solicitante (Requester)**
- **Aprovador de Área (Area Approver)**
- **Comprador (Purchaser/Procurement)**
- **Financeiro (Finance)**
- **Administrador do Sistema (System Admin)**

### 4.2 Role Intent (high-level)

- **Solicitante:** create, edit draft, submit, view own requests, respond to rework
- **Aprovador de Área:** review pending requests, approve/reject with comments
- **Comprador:** review approved requests, proceed procurement steps, update procurement-related fields/statuses
- **Financeiro:** review payment-related requests and finance-visible data based on permissions
- **Admin:** manage settings, users/roles (or mappings), lookups, and support operations

---

## 5) Core Workflow (V1)

## 5.1 High-Level Flow

1. Requester creates request
2. Requester saves as Draft or submits
3. Request goes to Area Approver
4. Area Approver approves or rejects
5. If rejected, request returns for correction/rework (Requester or Purchaser depending on process rule)
6. If approved, request proceeds to next operational status (Purchaser and/or Finance depending on request type)
7. All actions are logged in audit trail

## 5.2 Workflow Notes (to confirm)

- [ ] Confirm whether all requests must always pass through Area Approver
- [ ] Confirm if there are different flows for:
  - purchase request
  - payment request
  - urgent request
- [ ] Confirm rework ownership rule:
  - requester reworks when no quotation step exists
  - purchaser reworks when quotation step already occurred
- [ ] Confirm cancellation cutoff (e.g., cannot cancel after payment completed)
- [ ] Confirm mandatory comment rules for rejection/cancellation

---

## 6) Request Types (V1)

> Keep V1 simple if needed. Can start with one generic request type + category field.

### Option A (Simplified V1)

- Single request model with category/type field:
  - Purchase
  - Payment
  - Other (optional)

### Option B (Structured V1)

- Purchase Request
- Payment Request

### Decision (for V1)

- **Selected approach:** [Option A / Option B]
- **Reason:** [Why]

---

## 7) Functional Requirements

## 7.1 Authentication & Access

- FR-001: User must authenticate before accessing the system
- FR-002: System must apply role-based permissions for screens and actions
- FR-003: System must hide or disable actions the user cannot perform
- FR-004: Disabled actions must show an explanation when action is unavailable due to request state

## 7.2 Create Request

- FR-010: Requester can create a new request
- FR-011: System must support saving a request as **Draft**
- FR-012: Required fields must be validated before submission
- FR-013: Request must receive a unique request number upon creation or submission (define rule)
- FR-014: Requester can attach files during creation/editing
- FR-015: Requester can edit request while in editable states (e.g., Draft / Rework)

## 7.3 Submit & Approval

- FR-020: Requester can submit a valid Draft request
- FR-021: System changes status to **Pending Approval** upon submission
- FR-022: Area Approver can approve a pending request
- FR-023: Area Approver can reject a pending request with comment
- FR-024: Rejection comment must be mandatory [Confirm]
- FR-025: System must record approval/rejection action in audit trail
- FR-026: System must prevent approval actions on non-pending requests

## 7.4 Rework / Return Flow

- FR-030: Rejected requests must return to an editable rework state
- FR-031: System must indicate who is responsible for rework (Requester or Purchaser) based on process rules
- FR-032: Resubmission must preserve full history (no overwrite of previous approval events)

## 7.5 Status Tracking & Audit Trail

- FR-040: Request must display current status clearly (badge + text)
- FR-041: Request detail page must display a timeline/audit trail
- FR-042: Each audit event must include date/time, user, action, comment (if any), resulting status
- FR-043: System must retain previous status transitions for traceability

## 7.6 Attachments

- FR-050: Users can upload attachments to a request (based on permission/state)
- FR-051: System must validate file type and maximum file size
- FR-052: Attachment list must show file name, size, uploaded by, and upload date
- FR-053: Users can download attachments they are authorized to access
- FR-054: Users can remove attachments only in permitted states and roles
- FR-055: Attachment upload errors must be shown inline with clear message

## 7.7 List Views / Work Queues

- FR-060: System must provide **My Requests** list for requester
- FR-061: System must provide **Pending Approvals** list for approvers
- FR-062: List pages must support search and filters
- FR-063: List pages must display status badges
- FR-064: List pages must support pagination
- FR-065: List pages must support row-level quick actions based on permissions and status

## 7.8 Notifications (V1 baseline)

- FR-070: System should notify the next responsible user when a request changes responsibility/status
- FR-071: System should notify requester when request is approved/rejected
- FR-072: Notification channel in V1: [Email / In-app / Both]
- FR-073: Notification templates can be basic in V1 and improved later

## 7.9 Administration / Configuration (Minimal V1)

- FR-080: Admin can manage lookup values (e.g., request categories, priorities, departments) [if in V1]
- FR-081: Admin can view/support request troubleshooting (read-only or elevated access)
- FR-082: System must log administrative changes to critical configuration [optional for V1]

---

## 8) Request Data Fields (Draft Data Model Inputs)

> This is a functional field list, not final SQL schema.

## 8.1 Header / Request Information

- Request Number
- Request Type
- Title / Subject
- Description / Justification
- Requester (User)
- Department
- Plant / Location
- Cost Center [Confirm mandatory]
- Priority / Urgency
- Requested Date
- Need-by Date
- Status
- Current Responsible Role/User

## 8.2 Financial Information (V1 baseline)

- Currency
- Estimated Amount
- Payment Type [if applicable]
- Supplier [optional in initial creation / confirm]
- CAPEX / OPEX classification [Confirm where and when this is required]
- Budget Reference [optional / future]
- VAT/Tax fields [if relevant in V1]

## 8.3 Workflow / Approval Information

- Submitted At
- Approved At / Rejected At
- Approver Comment
- Rework Reason
- Cancellation Reason
- Payment Requested Flag [if applicable]
- Payment Completed Flag [future/confirm]

## 8.4 Attachments

- Attachment ID
- File Name
- File Type
- File Size
- Uploaded By
- Uploaded At
- Related Request ID

---

## 9) Status Model (Draft)

> Final statuses should be confirmed with stakeholders.

### Proposed V1 Statuses

- Draft
- Pending Approval
- Rejected (Rework Required)
- Approved
- In Procurement (optional for V1)
- Waiting Payment (optional for V1)
- Paid (optional for V1)
- Cancelled

### Status Rules (Draft)

- Draft -> Pending Approval (Submit)
- Pending Approval -> Approved (Approve)
- Pending Approval -> Rejected (Reject)
- Rejected -> Pending Approval (Resubmit)
- [Approved -> In Procurement] (if enabled in V1)
- [Approved/In Procurement -> Waiting Payment] (if enabled in V1)
- [Waiting Payment -> Paid] (future/confirm)
- Any eligible state -> Cancelled (until cutoff rule)

---

## 10) Screen Requirements (V1)

## 10.1 My Requests (List Page)

- Search
- Filters (Status, Date Range, Type, Department/Plant if applicable)
- Table with status badges
- Row actions (View / Edit if allowed / Cancel if allowed)
- Pagination
- Dense mode toggle (optional V1)

## 10.2 Pending Approvals (List Page)

- Queue for approver
- Filters (Status, Request Type, Priority)
- Quick actions (Approve / Reject) [optional in list]
- Link to detail page for full review

## 10.3 New/Edit Request (Form Page)

- Sectioned form (cards)
- Required field indicators
- Save Draft / Submit actions
- Attachments section
- Validation messages
- Readonly mode when request is not editable

## 10.4 Request Detail / Approval (Detail Page)

- Header with request number + status badge
- Summary section
- Financial/operational info section
- Attachments section
- Audit timeline / comments
- Action panel (Approve / Reject / Return / Cancel) according to role + state

---

## 11) Non-Functional Requirements (V1)

## 11.1 Performance

- NFR-001: Common list pages should load within [target, e.g., 2–4 seconds] under normal internal network conditions
- NFR-002: Form save/submit actions should provide immediate feedback (success/error)

## 11.2 Security

- NFR-010: Role-based access control must be enforced server-side (not UI-only)
- NFR-011: Sensitive data visibility must be permission-based
- NFR-012: Inputs must be validated server-side
- NFR-013: File uploads must be validated for type/size and safely stored

## 11.3 Auditability

- NFR-020: All workflow actions must be auditable
- NFR-021: Audit trail must preserve historical events and not overwrite prior actions

## 11.4 Reliability

- NFR-030: Failed operations must return clear errors and preserve user-entered data where possible
- NFR-031: System should avoid duplicate submission on double-click (idempotency safeguard or UI lock)

## 11.5 Usability

- NFR-040: UI must follow `docs/UI_UX_GUIDELINES.md`
- NFR-041: Desktop-first experience optimized for office use
- NFR-042: Portuguese labels/messages for V1

---

## 12) Integration Strategy (Future-Ready, Not V1 Full Scope)

### AlplaPROD (Future)

- Planned integration via API layer
- Use cases: [lookup data / stock / production references / item master / etc.]

### Primavera (Future)

- Planned integration via API/service layer
- Use cases: [PO creation / supplier sync / payment status / accounting references / etc.]

### Integration Design Principle

- V1 must be designed so integrations can be added without rewriting core business flows
- Use abstraction/service layer for external systems
- Start with mocks/manual inputs where needed in V1

---

## 13) Assumptions (Current)

- Development starts locally on developer machine
- Production deployment will be on-premises (Windows Server + SQL Server)
- SQL Server is already available in production environment
- API integrations with AlplaPROD and Primavera will come after V1 baseline
- V1 is desktop-first and internal-only (Alpla Angola users)

---

## 14) Open Questions (To Resolve with Stakeholders)

### Process / Workflow

- OQ-001: What are the exact approval stages for Purchase vs Payment requests?
- OQ-002: Is Area Approver always mandatory?
- OQ-003: Who owns rework in each scenario (Requester vs Purchaser)?
- OQ-004: What is the cancellation cutoff rule exactly?
- OQ-005: Which actions require mandatory comments?

### Data / Finance

- OQ-010: At what stage must CAPEX/OPEX be defined?
- OQ-011: Who is responsible for defining CAPEX/OPEX (Requester, Approver, Purchaser, Finance)?
- OQ-012: Which fields are mandatory for Payment requests vs Purchase requests?
- OQ-013: Is supplier mandatory at creation time?

### Notifications

- OQ-020: Email, in-app, or both in V1?
- OQ-021: Who must be notified on each transition?

### Security / Access

- OQ-030: Will V1 use local authentication or AD/Entra integration?
- OQ-031: Are permissions role-based only, or also department/plant-scoped?
- OQ-032: Should Finance see all requests or only payment-related ones?

---

## 15) Acceptance Criteria (V1 Baseline)

V1 is considered acceptable when:

- Users can create and save requests as Draft
- Users can submit requests for approval
- Approvers can approve/reject with audit trail recorded
- Status transitions are visible and consistent
- Attachments work with validation and download
- My Requests and Pending Approvals lists are usable with filters
- Role-based UI actions are consistent with workflow state
- UI follows defined `UI_UX_GUIDELINES.md`
- Core process can run without AlplaPROD/Primavera integrations

---

## 16) Change Log (for this document)

- [YYYY-MM-DD] Initial draft created
- [YYYY-MM-DD] Updated after stakeholder validation meeting #1
