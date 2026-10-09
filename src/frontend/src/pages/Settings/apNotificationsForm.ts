/**
 * Pure form-state helpers for the Accounts Payable e-mail panel (Master Data › Accounts Payable Email).
 * Kept free of React so the mapping between the API shape and the form (incl. the two opt-in switches
 * `notifyOnPoRegistered` and `notifyFinanceUsersByEmail`, both default OFF) is unit-testable.
 */

export interface ApConfig {
    id: number;
    companyId: number;
    companyName: string;
    email: string;
    ccEmails: string | null;
    label: string | null;
    isActive: boolean;
    notifyOnScheduled: boolean;
    notifyOnCompleted: boolean;
    /** Notify the AP address when a P.O. is registered / re-registered ("review required"). Default false. */
    notifyOnPoRegistered: boolean;
    /** Also e-mail Finance-role users individually (plant-scoped). In-app notifications are unaffected. Default false. */
    notifyFinanceUsersByEmail: boolean;
    createdAtUtc: string;
    updatedAtUtc: string;
}

export interface ApConfigForm {
    companyId: number;
    email: string;
    ccEmails: string;
    label: string;
    notifyOnScheduled: boolean;
    notifyOnCompleted: boolean;
    notifyOnPoRegistered: boolean;
    notifyFinanceUsersByEmail: boolean;
}

/** New-configuration defaults: scheduling/completion ON (existing behaviour), the two new switches OFF. */
export function defaultApForm(): ApConfigForm {
    return {
        companyId: 0,
        email: '',
        ccEmails: '',
        label: '',
        notifyOnScheduled: true,
        notifyOnCompleted: true,
        notifyOnPoRegistered: false,
        notifyFinanceUsersByEmail: false
    };
}

/** Form state for editing an existing configuration. Older API payloads without the new flags read as OFF. */
export function formFromConfig(config: ApConfig): ApConfigForm {
    return {
        companyId: config.companyId,
        email: config.email,
        ccEmails: config.ccEmails || '',
        label: config.label || '',
        notifyOnScheduled: config.notifyOnScheduled,
        notifyOnCompleted: config.notifyOnCompleted,
        notifyOnPoRegistered: config.notifyOnPoRegistered === true,
        notifyFinanceUsersByEmail: config.notifyFinanceUsersByEmail === true
    };
}

export interface ApConfigPayload {
    companyId?: number;
    email: string;
    ccEmails: string | null;
    label: string | null;
    notifyOnScheduled: boolean;
    notifyOnCompleted: boolean;
    notifyOnPoRegistered: boolean;
    notifyFinanceUsersByEmail: boolean;
}

/** API payload. `companyId` is sent only on create (it cannot change on update). */
export function toApConfigPayload(form: ApConfigForm, mode: 'create' | 'update'): ApConfigPayload {
    const payload: ApConfigPayload = {
        email: form.email.trim(),
        ccEmails: form.ccEmails.trim() || null,
        label: form.label.trim() || null,
        notifyOnScheduled: form.notifyOnScheduled,
        notifyOnCompleted: form.notifyOnCompleted,
        notifyOnPoRegistered: form.notifyOnPoRegistered,
        notifyFinanceUsersByEmail: form.notifyFinanceUsersByEmail
    };
    if (mode === 'create') payload.companyId = form.companyId;
    return payload;
}

/** Client-side validation mirrored from the panel; returns an error message or null. */
export function validateApForm(form: ApConfigForm, mode: 'create' | 'update'): string | null {
    if (!form.email.trim()) return 'O e-mail principal é obrigatório.';
    if (!form.email.includes('@')) return 'O e-mail principal não tem um formato válido.';
    if (form.ccEmails.trim()) {
        const ccList = form.ccEmails.split(/[;,]/).map(s => s.trim()).filter(Boolean);
        if (ccList.length > 10) return 'O número máximo de e-mails CC é 10.';
        for (const cc of ccList) {
            if (!cc.includes('@')) return `O endereço CC '${cc}' não é um e-mail válido.`;
        }
    }
    if (mode === 'create' && !form.companyId) return 'Selecione uma empresa.';
    return null;
}
