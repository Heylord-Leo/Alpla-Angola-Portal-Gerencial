# Manual Test Guide - Portal Gerencial

> [!NOTE]
> This guide is designed to facilitate the manual execution of workflow, OCR, and financial validation tests in the Portal Gerencial using the generated test mass. **No automated seeding or database modifications are required.** All tests should be performed manually via the Portal's user interface.

## 1. Overview & Objectives

The purpose of this guide is to provide a structured, scenario-based approach for testing the Portal Gerencial. By manually entering data and uploading the generated PDF documents, you will validate:
- **Supplier & Catalog Item Creation**: UI usability, data validation.
- **Request Creation**: Quotation and Payment request flows.
- **OCR Extraction**: Accuracy of data extraction from uploaded PDFs.
- **Financial Validation**: Calculation of IVA, Discounts, Tolerances, and Currency rules.
- **Price History Validation**: Historical price behavior based on `ItemCode + SupplierId + Currency`.
- **Workflow & Approval Center**: Correct routing, dashboard counters, and status updates.

All test data and documents are located in `C:\dev\alpla-portal\docs\test-data\`.

---

## 2. Preparation & Reference Data

Before starting the tests, familiarize yourself with the reference datasets:

1. **`01_Suppliers.csv`**: Contains NIFs, names, categories, and payment terms. Use this to create suppliers as needed.
2. **`02_CatalogItems.csv`**: Contains Item Codes, descriptions, unit prices, and IVA percentages. Use this to create catalog items.
3. **`03_Scenarios.csv`**: The master list of all 50 scenarios, detailing expected financial calculations and specific edge case rules.
4. **`04_DocumentMatrix.csv`**: Details the generated PDF documents for each scenario.
5. **`05_Inconsistencies.csv`**: Identifies specific scenarios containing intentional data errors for OCR/validation testing.
6. **`06_CatalogItemPriceHistory.csv`**: Reference data to understand expected historical pricing. Note that actual price history in the Portal is built dynamically from paid requests.

> [!IMPORTANT]
> **Chronology Rule:** All generated documents fall within the date range of **26/03/2026 to 26/04/2026**. Ensure you use these dates during manual entry.
> The sequence is always: Proforma <= P.O. <= Payment Scheduling Proof <= Payment Proof <= Receipt.

---

## 3. General Execution Steps

For each scenario, follow this general workflow in the Portal Gerencial:

### Step 3.1: Pre-requisites (Master Data)
1. Check `03_Scenarios.csv` for the required Supplier and Item.
2. **First, try to use suppliers and catalog items that already exist in the Portal.** The generated test data has been updated to reflect existing master data found in your local environment.
3. **Only create a supplier or item manually if the scenario explicitly says it is a supplier/item creation test.**
4. The main objective is to test request creation, OCR, workflow, calculations and efficiency, not master data creation.

### Step 3.2: Request Creation
1. Navigate to the Requests (Requisições) module.
2. Initiate a new request (Quotation or Payment, as defined by the scenario's `Type`).
3. Fill in the Department, Plant, Cost Center, and Justification as per `03_Scenarios.csv`.
4. Add the Catalog Item(s) with the specified Quantity and Unit Price.

### Step 3.3: Document Upload & OCR
1. Upload the specific PDF document(s) for the current workflow step from the corresponding scenario folder (e.g., `docs/test-data/SCN-001/PRO-2026-1.pdf`).
2. Verify the OCR extraction against the document contents.
3. Manually correct any OCR fields if testing a scenario with intentional inconsistencies.

### Step 3.4: Financial Validation
1. Verify the UI's calculation of Subtotal, IVA, Discounts, and Grand Total against the expected values in `03_Scenarios.csv`.
2. For Payment requests, verify that any difference between the approved amount and the actual paid amount triggers a divergence warning.

### Step 3.5: Workflow & Approvals
1. Submit the request.
2. Log in with the appropriate approver roles (Area, Financial, Direction) as required by the workflow.
3. Verify the Approval Center dashboard counters and lists accurately reflect the pending items.
4. Approve or reject the request as needed to advance the workflow.

---

## 4. Test Execution Batches

To systematically test the application, proceed through the scenarios in the following batches:

### Batch 1: Initial & Simple Cases (SCN-001 to SCN-010)
**Focus**: Basic workflow, standard calculations, and primary discounts.

*   **SCN-001**: Simple payment request (1 item, AOA, standard IVA).
*   **SCN-002**: Multiple items payment request.
*   **SCN-003 & SCN-004**: Quotation requests (testing conversion/selection of suppliers).
*   **SCN-005**: Item-level discount validation.
*   **SCN-006**: Global discount validation.
*   **SCN-007**: Complex discount validation (Item-level + Global discount combined).
*   **SCN-008, SCN-009, SCN-010**: Different IVA rates (No IVA, 14%, 0%).

### Batch 2: Currencies & Process Variations (SCN-011 to SCN-020)
**Focus**: Multi-currency handling, receiving, and payment divergence detection.

*   **SCN-011 to SCN-014**: Validate handling of different currencies (AOA, USD, EUR) and mixed IVA.
*   **SCN-015**: Partial receiving workflow.
*   **SCN-016**: Full receiving workflow.
*   **SCN-017**: Scheduled payment workflow validation.
*   **SCN-018 to SCN-020**: Payment Proof validations.
    *   SCN-018: Exact payment amount.
    *   SCN-019: Payment slightly different from approved amount (Expect payment divergence warning — zero-tolerance policy).
    *   SCN-020: Payment significantly different from approved amount (Expect payment divergence warning with large variance).

### Batch 3: OCR & Data Inconsistencies (SCN-021 to SCN-024)
**Focus**: System robustness against malformed data and OCR error handling.

> [!WARNING]
> These scenarios contain intentional errors in the PDF documents. The goal is to verify the Portal's validation logic and manual override capabilities.

*   **SCN-021**: Missing/incorrect document data. Verify that the Portal flags missing required fields.
*   **SCN-022**: Supplier NIF formatting inconsistencies. Verify NIF validation rules.
*   **SCN-023**: Repeated supplier names but different NIFs.
*   **SCN-024**: Same NIF but slightly different supplier name (Duplicate NIF check).

### Batch 4: Price History & Complex Rules (SCN-025 to SCN-039)
**Focus**: Validating dynamic price history tracking based on `ItemCode + SupplierId + Currency`.

> [!NOTE]
> Price history tests rely on prior "Paid" requests. Ensure previous batches have completed workflows to build historical data.

*   **SCN-025**: Proforma uses old price, catalog has newer price.
*   **SCN-029**: Price changed between quotation and PO stages.
*   **SCN-030**: Price is above catalog average (Expect system warning/justification requirement).
*   **SCN-031**: Price is below catalog average (due to negotiated discount).
*   **SCN-032**: System indicates a lower price exists from an alternative supplier.
*   **SCN-037 & SCN-038**: Verify strict boundaries (No cross-supplier or cross-currency direct comparisons).
*   **SCN-039**: System distinguishes between item-level discounts and actual unit price base changes.

### Batch 5: Volume & Dashboard Load Testing (SCN-040 to SCN-050)
**Focus**: Approval Center performance, list filtering, and dashboard visibility under load.

*   Create all remaining requests (SCN-040 to SCN-050) rapidly.
*   Leave them in various pending states (e.g., Pending Area Approval, Pending Financial Approval).
*   **Verification**: Navigate to the Approval Center and validate general performance, list loading, filters, pagination, dashboard visibility, and whether pending items are visible according to their expected workflow state.

---

## 5. Reporting Issues

During manual execution, if you encounter bugs (e.g., calculation errors, OCR failures, missing validation warnings, or Approval Center counter discrepancies), document them with:
1. Scenario ID (e.g., SCN-007)
2. Workflow Step (e.g., Attaching Payment Proof)
3. Expected Behavior (from `03_Scenarios.csv`)
4. Actual Behavior observed in the Portal.
