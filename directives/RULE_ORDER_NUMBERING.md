# RULE - Request Numbering

## Objective

To review, correct, and validate the logic for generating request numbers in the **Alpla Angola Portal Gerencial** project, ensuring uniqueness, non-reusability, and secure behavior even after draft deletion or concurrent creation.

## Expected Inputs

- Source code in `C:\dev\alpla-portal`.
- Current implementation of `RequestNumber` generation.
- Formatted business number: `REQ-DD/MM/YYYY-SequentialNumber`.
- Current flow for request creation and draft deletion.

## Expected Outputs

- Consistent and revised numbering logic (Unified `REQ` format).
- Unique and non-reusable request number (Global persistent counter).
- Protection against concurrency duplicates.

## Functional Rules

### 1. Request Number Must Not Be Reused
Even if a draft is deleted:
- The used number must not become available again.
- The next request must continue the correct sequence.
- Gaps in the sequence are acceptable.

Example:
- REQ-01/03/2026-023
- REQ-01/03/2026-024
- REQ-01/03/2026-025
- Delete 024 and 025.
- The next request must be 026, never 024 or 025.

### 2. Numbering Should Not Depend Only on Remaining Records
Do not base the logic on:
- Searching for the last existing record (Scanning tables).
- Counting current records in the table.
- Recalculating the sequence from the remaining set.
The source of truth must be the persistent `GLOBAL_REQUEST_COUNTER`.

### 3. Generation Must Be Controlled by the Backend
- The frontend must not assemble or suggest the final request number.
- The backend must be the source of truth.
- The number must be allocated transactionally.

### 4. Business Identifiers Must Use Stable Keys
- The `GLOBAL_REQUEST_COUNTER` must be unique and independent of the request type.
- Do not depend on `RequestTypeId` for the counter key, as the prefix is now unified (`REQ`).

### 5. Final Barrier Against Duplicity
Always add database protection for `RequestNumber`, such as:
- A unique index.
- A unique constraint.
The application must not rely solely on code logic.

## Technical Implementation

### Backend
Verify and adjust:
- `CreateRequest`.
- Final format: `$"REQ-{today:dd/MM/yyyy}-{counter:D3}"`.
- Usage of `GLOBAL_REQUEST_COUNTER` in `SystemCounters`.
- Proper lock on `SaveChangesAsync` of the counter before request insertion.

### Database
- `SystemCounter` entity.
- Unique index on `RequestNumber`.

### Frontend
- Unified number display.
- Never infer request type from the `REQ` prefix; use `requestTypeCode`.

## Definition of Done
The task is complete if:
1. Deleting drafts does not allow number reuse.
2. Subsequent creations follow the correct global sequence.
3. Concurrent creations do not generate duplicates.
4. The `REQ-DD/MM/YYYY-###` format is respected.
5. Database unique index protection is present.
