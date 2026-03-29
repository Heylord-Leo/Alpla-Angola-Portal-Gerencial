export interface IvaRate {
    id: number;
    code: string;
    name: string;
    ratePercent: number;
    isActive: boolean;
}

export interface Unit {
    id: number;
    code: string;
    name: string;
    allowsDecimalQuantity: boolean;
}

export interface QuotationDraftItem {
    lineNumber: number;
    description: string;
    quantity: number;
    unitId: number | null;
    unit?: string; // Raw extracted unit string from OCR
    unitPrice: number;
    ivaRateId: number | null;
    taxRate?: number; // Raw extracted tax percentage for suggestion hint
    totalPrice: number; // Front-end calculated preview
}

export interface QuotationDraft {
    supplierId: number | null;
    supplierNameSnapshot: string;
    supplierTaxId?: string;
    documentNumber: string;
    documentDate: string;
    currency: string;
    extractedCurrency?: string; // Raw extracted currency for suggestion hint
    discountAmount: number; // Front-end user input
    totalAmount: number; // Front-end calculated preview
    proformaAttachmentId?: string; // Links attachment implicitly
    items: QuotationDraftItem[];
}
