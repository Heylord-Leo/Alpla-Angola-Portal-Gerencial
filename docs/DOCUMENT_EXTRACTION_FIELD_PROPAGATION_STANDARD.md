# Document Extraction Field Propagation Standard

## 1. Purpose / Scope

This document defines the mandatory standard for adding, propagating, and validating any new data field extracted from documents (e.g., Quotations, Invoices) within the Alpla Portal Gerencial.

Adding a new field is not just a provider-level change. To ensure data is not lost in intermediate layers, every field must follow the **Canonical Flow** described below. This prevents regressions where a field is successfully "extracted" by an AI provider but never reaches the user interface.

## 2. Canonical Flow of a Field

Every extracted field must transition through the following layers without exception:

1. Provider Raw Response: The raw JSON returned by the external AI (e.g., OpenAI, Azure).
2. Provider Mapping: The logic that parses the raw JSON into the internal extraction DTO.
3. Internal Extraction DTO (`ExtractionResultDto`): The provider-agnostic model used by the backend services.
4. Legacy/Compatibility OCR DTO (`OcrExtractionResultDto`): The model used for backward compatibility with the frontend's expected shape.
5. API Response Mapper (`ExtractionMapper`): The utility that converts internal DTOs to legacy DTOs.
6. Frontend State: The React state (e.g., `quotationDrafts` in `BuyerItemsList.tsx`) that stores the extracted data.
7. UI Component/Modal: The final consumer (e.g., `QuickSupplierModal.tsx`) that prefills the form for the user.
8. Validation/Plausibility: Rules to filter out OCR noise before prefilling.
9. Persistence: The backend entity and save logic that stores the final confirmed value.

## 3. Mandatory Checklist for Adding a New Field

When adding a new field (e.g., `PaymentTerms`, `ValidityDate`), check off every item:

-AI Prompt: Update the provider system prompt to include the new field and its extraction rules.
-Internal DTO: Add the field to `ExtractionHeaderDto` or `ExtractionLineItemDto`.
-Provider Mapper: Update the provider (e.g., `OpenAiDocumentExtractionProvider.cs`) to map the new JSON property.
-Legacy DTO: Add the field to `OcrHeaderSuggestionsDto` or equivalent in `OcrExtractionResultDto.cs`.
-Legacy Mapper: Update `ExtractionMapper.cs` to propagate the field from the internal DTO to the legacy DTO.
-Frontend Type: Update the `QuotationDraft` interface in the frontend.
-Frontend State Logic: Update the extraction result handler (e.g., `handleImportFiles` in `BuyerItemsList.tsx`) to pull the new field.
-UI Component: Add the field to the target modal/form (e.g., `QuickSupplierModal.tsx`) and ensure it prefills correctly.
-Validation: Implement plausibility checks (e.g., min length, regex) to avoid prefilling noise.
-Documentation: Update this standard or the field matrix if the field is a permanent core addition.

## 4. Naming and Ownership Rules

- Consistency: Use the same camelCase name across all layers (e.g., `supplierTaxId`) whenever possible.
- Canonical Source: The `ExtractionResultDto` in the Application layer is the source of truth for field names.
- JSON Property Names: Always use explicit `[JsonPropertyName]` attributes in C# DTOs to match the frontend expectations.

## 5. Validation / Plausibility Guidance

Extracted data is inherently probabilistic. Follow these rules for prefilling:

- Prefill Automatically: Only if the confidence is high and the value passes basic syntax validation (e.g., a tax ID with enough digits).
- Leave Blank: If the value looks like noise (e.g., single characters, random symbols) or fails validation.
- Allow Edit: Every prefilled field **MUST** remain editable by the user.
- Reject Garbage: Implement filters to discard known "hallucinations" or OCR artifacts.

## 6. Compatibility Guidance

While the system moves towards a provider-agnostic architecture, **Legacy Compatibility DTOs** still exist. You must update both the internal `ExtractionResultDto` and the legacy `OcrExtractionResultDto` until the frontend is fully migrated to the new contract.

## 7. Suggested Field Matrix

Use this matrix pattern to track complex field additions:

| Field | Provider (Prompt) | Internal DTO | Legacy DTO | API Mapper | Frontend State | UI Consumer |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `supplierName` | Yes | `SupplierName` | `SupplierName` | Yes | `supplierNameSnapshot` | `QuickSupplierModal` |
| `supplierTaxId`| Yes | `SupplierTaxId` | `SupplierTaxId` | Yes | `supplierTaxId` | `QuickSupplierModal` |
| `docNumber` | Yes | `DocumentNumber`| `DocumentNumber`| Yes | `documentNumber` | `QuotationForm` |

## 8. Concrete Worked Example: `supplierTaxId`

**The Problem**: OpenAI was extracting the Tax ID correctly, but it was appearing empty in the Quick Supplier Modal.
**The Cause**: The field existed in the Provider and Internal DTO, but was missing from the `OcrHeaderSuggestionsDto` (legacy) and wasn't being mapped in `ExtractionMapper`.
**The Fix**:
1. Added `SupplierTaxId` to `OcrHeaderSuggestionsDto`.
2. Updated `ExtractionMapper.MapToLegacyOcrResult` to include the mapping.
3. The frontend was then able to see the field in `result.integration.headerSuggestions.supplierTaxId`.

## 9. Rules for Future Provider Additions

Adding a new provider (e.g., AWS Textract) **does not** change the UI flow. A new provider must simply implement `IDocumentExtractionProvider` and map its specific output to the canonical `ExtractionResultDto`. The rest of the propagation path remains identical.

## 10. Definition of Done

A new extracted field is only "Done" when:

1. It is extracted by at least one provider.
2. It passes through the internal and legacy mappers.
3. It appears as a prefilled (but editable) value in the target UI component.
4. It is successfully saved to the database upon user confirmation.
