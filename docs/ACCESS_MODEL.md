# Access & Permission Model (V1)

## Document Info

- **Project:** Alpla Angola Internal Portal
- **Module:** Purchase Requests & Payments
- **Version:** 0.1.0 (Draft)
- **Status:** Draft
- **Related Specs:** `REQUIREMENTS_V1.md`, `UI_UX_GUIDELINES.md`, `ARCHITECTURE.md`

---

## 1. Purpose and Scope

This document defines the permission and access model for the **V1 Purchase Requests & Payments** module. It outlines role-based access controls (RBAC), data visibility rules, action authorizations, and UI behavior principles to guide both frontend and backend implementation.

**Out of Scope for V1:**

- Dynamic or custom role creation by end-users.
- Highly granular attribute-based access control (ABAC) beyond basic plant/department scoping.
- Matrix approvals (multiple approvers needing consensus on a single step).
- Delegation rules (e.g., temporary reassignment due to vacation).

---

## 2. Roles (V1)

The system operates using the following foundational roles:

1. **Solicitante (Requester):** Any employee who initiates a purchase or payment request.
2. **Aprovador de Área (Area Approver):** The manager or designated authority responsible for reviewing and approving requests from their assigned department(s)/area(s).
3. **Comprador (Purchaser):** Procurement staff responsible for advancing approved purchase requests to quotation or PO stages based on operational rules.
4. **Financeiro (Finance):** Staff handling the payment side of operations, monitoring payment requests, and updating financial tracking/statuses.
5. **Administrador do Sistema (Admin):** IT or module administrator who maintains lookup values, user-role associations, and provides overarching operational support.

---

## 3. Permission Dimensions

Permissions within the system are evaluated across the following dimensions:

- **Screen/Page Access:** Which routes/views the user can load.
- **Action Permissions:** Which discrete actions (Submit, Approve, Reject, Cancel) the user can trigger.
- **Status-Dependent Permissions:** Actions constrained not just by role, but by the current state of a Request (e.g., cannot edit a "Pending Approval" request).
- **Data Visibility Permissions:** Which specific records appear in list views and reports based on ownership or department assignment.
- **Attachment Permissions:** Who can upload, view, or remove files on a specific request.
- **Audit Trail Visibility:** Who can see the history and comments on a request.

---

## 4. Role x Screen Matrix

| Screen \ Role | Solicitante | Aprovador | Comprador | Financeiro | Admin |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **New/Edit Request (Form)** | Yes | No* | No | No | Read-Only |
| **My Requests (List)** | Yes | Yes (As Requester)| Yes (As Requester)| Yes (As Requester)| Yes |
| **Pending Approvals (List)**| No | Yes | No | No | No |
| **Work Queue (List)**| No | No | Yes (Procurement)| Yes (Payments) | Yes |
| **Request Detail** | Yes (Own)| Yes (Assigned) | Yes | Yes | Yes |
| **Admin/Settings** | No | No | No | No | Yes |

*\*Approvers can view details in a read-only context, but editing the core form structure during the pending state is generally restricted to prevent unapproved alterations.*

---

## 5. Role x Action Matrix

| Action \ Role | Solicitante | Aprovador | Comprador | Financeiro | Admin |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Create Request** | Yes | Yes | Yes | Yes | No |
| **Save Draft** | Yes | Yes | Yes | Yes | No |
| **Edit Draft** | Yes (Own)| Yes (Own) | Yes (Own) | Yes (Own) | No |
| **Submit** | Yes (Own)| Yes (Own) | Yes (Own) | Yes (Own) | No |
| **Approve** | No | Yes (Assigned)| No | No | No |
| **Reject** | No | Yes (Assigned)| No | No | No |
| **Cancel** | Yes (Own)| No | Yes | Yes | Yes (Override) |
| **Rework / Resubmit** | Yes (Own)| No | Yes | No | No |
| **Add Attachment** | Yes | Yes (During approval)| Yes | Yes | No |
| **Remove Attachment** | Yes (Draft/Rework)| No | Yes | Yes | No |
| **View/Download Attach** | Yes (Own)| Yes | Yes | Yes | Yes |
| **View Audit Trail** | Yes (Own)| Yes | Yes | Yes | Yes |
| **Change Lookup/Config** | No | No | No | No | Yes |
| **Force Status Change** | No | No | No | No | Yes |

---

## 6. Status-Dependent Rules

Even if a role has the base permission, actions are globally restricted by the request's current status:

- **Draft:** Only the creator can Edit, Add/Remove Items/Attachments, Submit, or Cancel.
- **Submitted:** Form and Items are **Locked (Read-Only)** for the Requester. The request enters the approval workflow (V2). Actions like Delete Item, Update Line Item, and Update Draft are now blocked by backend status checks.
- **Approved:** Form is locked. Terminal state for V1.
- **Cancelled:** Fully locked. Terminal state. No edits, status changes, or attachment modifications allowed by anyone except via Admin override.

---

## 7. Permission-Aware UI Rules (Implementation Note)

The UI must elegantly handle unauthorized states to prevent user frustration, aligning with `UI_UX_GUIDELINES.md`.

1. **Action Visibility (Hide vs Disable):**
   - **Hide:** If a user's role *never* has access to an action (e.g., Requester never sees "Approve/Reject" buttons; Admin link is completely hidden from non-admins).
   - **Disable:** If a user *has* the role permission in theory, but the action is invalid for the *current state* (e.g., A Requester looking at their "Pending Approval" request sees the "Edit" and "Cancel" buttons, but they are disabled).
2. **Helper Text / Tooltips:**
   - Disabled buttons must include a tooltip explaining why. (e.g., Hovering over a disabled "Approve" button might say, "Only pending requests can be approved." Hovering over a disabled "Cancel" button might say, "Cannot cancel a request that has already been paid.")
3. **Consistency:** Action availability logic must be mathematically identical across List views (row-level quick actions), Detail views (action panels), and Forms.

---

## 8. Data Visibility Rules (V1 Draft)

*Assumption: Row-level security is required immediately to prevent unauthorized viewing of cross-department spending.*

- **Solicitante (Requester):** Can only view requests where they are the explicit `Requester`.
- **Aprovador de Área:** Can view requests currently assigned to them for approval, plus historical access to any requests submitted by users within their assigned Department/Plant scope *(Assumption - to confirm scoping hierarchy)*.
- **Comprador (Purchaser):** Can view all Purchase Requests universally across departments to facilitate procurement routing. Cannot view Payment Requests unrelated to procurement.
- **Financeiro (Finance):** Can view all Payment Requests universally, and approved Purchase Requests requiring capital allocation.
- **Admin:** Can view all requests universally for support/troubleshooting. Cannot approve/reject on behalf of others unless explicitly utilizing a "Force Change" override with a mandatory logged reason.

---

## 9. Open Questions

*Points requiring stakeholder confirmation before final backend implementation:*

- **OQ-100:** Do Area Approvers need to see all historical requests for their department, or only those requiring their active approval?
- **OQ-101:** Does Finance need visibility into Draft requests, or only after Area Approval?
- **OQ-102:** Can an Area Approver edit the content of a Request (e.g., correcting the requested amount) instead of rejecting it back to the Requester for rework?
- **OQ-103:** Should Admin support "super-user" assignment changes (e.g., reassigning an approval if a manager is sick/on leave)?

---

## 10. Implementation Guidance (V1)

- **Server-Side Authorization is Mandatory:** The backend API must independently verify role, ownership, and request status before executing any mutation or returning data. Do not trust the client.
- **UI Checks are Convenience Only:** The frontend hides/disables UI elements strictly to improve UX and prevent bad input, not as a security measure.
- **Suggested Architecture Strategy:**
  - API endpoints should return an `allowedActions: ["EDIT", "CANCEL"]` array attached to the request payload based on the authenticated user's context.
  - The backend calculates what actions the current user can perform on that specific record, considering their Role + the Record's Status + Ownership. The frontend simply maps this array to render enabled/disabled/hidden buttons without replicating complex state machines.
