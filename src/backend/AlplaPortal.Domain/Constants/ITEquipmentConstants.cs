namespace AlplaPortal.Domain.Constants;

public static class ITEquipmentConstants
{
    /// <summary>Internal stable status codes for equipment lifecycle.</summary>
    public static class EquipmentStatus
    {
        public const string Available = "AVAILABLE";
        public const string InUse = "IN_USE";
        public const string Reserved = "RESERVED";
        public const string InRepair = "IN_REPAIR";
        public const string Returned = "RETURNED";
        public const string Lost = "LOST";
        public const string Retired = "RETIRED";
        public const string Disposed = "DISPOSED";
        public const string Damaged = "DAMAGED";
        public const string Unknown = "UNKNOWN";

        public static readonly string[] All = {
            Available, InUse, Reserved, InRepair, Returned,
            Lost, Retired, Disposed, Damaged, Unknown
        };

        /// <summary>Statuses that block new assignments.</summary>
        public static readonly string[] NonAssignable = { Lost, Retired, Disposed };

        /// <summary>Portuguese display names.</summary>
        public static string DisplayName(string code) => code switch
        {
            Available => "Disponível",
            InUse => "Em uso",
            Reserved => "Reservado",
            InRepair => "Em conserto",
            Returned => "Devolvido",
            Lost => "Perdido",
            Retired => "Baixado",
            Disposed => "Descartado",
            Damaged => "Danificado",
            Unknown => "Desconhecido",
            _ => code
        };
    }

    /// <summary>Equipment type codes.</summary>
    public static class EquipmentType
    {
        public const string Laptop = "LAPTOP";
        public const string Desktop = "DESKTOP";
        public const string Monitor = "MONITOR";
        public const string Printer = "PRINTER";
        public const string Nvr = "NVR";
        public const string UnknownType = "UNKNOWN";

        public static readonly string[] All = { Laptop, Desktop, Monitor, Printer, Nvr, UnknownType };
    }

    /// <summary>Movement log action types for audit trail.</summary>
    public static class MovementType
    {
        public const string Created = "CREATED";
        public const string Imported = "IMPORTED";
        public const string Assigned = "ASSIGNED";
        public const string Returned = "RETURNED";
        public const string SentToRepair = "SENT_TO_REPAIR";
        public const string ReturnedFromRepair = "RETURNED_FROM_REPAIR";
        public const string MarkedAsLost = "MARKED_AS_LOST";
        public const string Reserved = "RESERVED";
        public const string ReleasedFromReservation = "RELEASED_FROM_RESERVATION";
        public const string RetiredMovement = "RETIRED";
        public const string Updated = "UPDATED";
        public const string PhotoUpdated = "PHOTO_UPDATED";
        public const string NotesUpdated = "NOTES_UPDATED";
        public const string AgreementGenerated = "AGREEMENT_GENERATED";
        public const string EmailSent = "EMAIL_SENT";
        public const string EmailFailed = "EMAIL_FAILED";
        public const string ReturnDocumentGenerated = "RETURN_DOCUMENT_GENERATED";
        public const string ReturnEmailSent = "RETURN_EMAIL_SENT";
        public const string ReturnEmailFailed = "RETURN_EMAIL_FAILED";
        public const string UserChanged = "USER_CHANGED";
        public const string UserChangeReturned = "USER_CHANGE_RETURNED";
        public const string UserChangeAssigned = "USER_CHANGE_ASSIGNED";
        public const string SignedTermUploaded = "SIGNED_TERM_UPLOADED";
    }

    /// <summary>Assignment lifecycle statuses.</summary>
    public static class AssignmentStatus
    {
        public const string Active = "ACTIVE";
        public const string Returned = "RETURNED";
        public const string Lost = "LOST";
        public const string Replaced = "REPLACED";
        public const string Cancelled = "CANCELLED";
    }

    /// <summary>How the equipment entered the system.</summary>
    public static class SourceType
    {
        public const string ImportedLegacy = "IMPORTED_LEGACY";
        public const string ManualPurchase = "MANUAL_PURCHASE";
        public const string ManualRegistration = "MANUAL_REGISTRATION";
    }

    /// <summary>Document type codes for acquisition/equipment documents.</summary>
    public static class DocumentType
    {
        public const string PaymentProof = "PAYMENT_PROOF";
        public const string Invoice = "INVOICE";
        public const string Proforma = "PROFORMA";
        public const string PurchaseOrder = "PURCHASE_ORDER";
        public const string Warranty = "WARRANTY";
        public const string Receipt = "RECEIPT";
        public const string DeliveryNote = "DELIVERY_NOTE";
        public const string AssignmentAgreement = "ASSIGNMENT_AGREEMENT";
        public const string ReturnAgreement = "RETURN_AGREEMENT";
        public const string SignedAssignmentAgreement = "SIGNED_ASSIGNMENT_AGREEMENT";
        public const string SignedReturnAgreement = "SIGNED_RETURN_AGREEMENT";
        public const string Other = "OTHER";

        public static string DisplayName(string code) => code switch
        {
            PaymentProof => "Comprovativo de Pagamento",
            Invoice => "Fatura",
            Proforma => "Proforma",
            PurchaseOrder => "Ordem de Compra / P.O",
            Warranty => "Garantia",
            Receipt => "Recibo",
            DeliveryNote => "Guia de Entrega",
            AssignmentAgreement => "Termo de Responsabilidade",
            ReturnAgreement => "Termo de Devolução",
            SignedAssignmentAgreement => "Termo de Responsabilidade Assinado",
            SignedReturnAgreement => "Termo de Devolução Assinado",
            Other => "Outro",
            _ => code
        };
    }

    /// <summary>Normalizes CSV status values to internal codes.</summary>
    public static string NormalizeCsvStatus(string? csvStatus)
    {
        if (string.IsNullOrWhiteSpace(csvStatus)) return EquipmentStatus.Unknown;
        return csvStatus.Trim().ToLowerInvariant() switch
        {
            "in use" => EquipmentStatus.InUse,
            "available" => EquipmentStatus.Available,
            "reserved" => EquipmentStatus.Reserved,
            "in repair" => EquipmentStatus.InRepair,
            "retired" => EquipmentStatus.Retired,
            _ => EquipmentStatus.Unknown
        };
    }

    /// <summary>Normalizes CSV equipment type values to internal codes.</summary>
    public static string NormalizeCsvType(string? csvType)
    {
        if (string.IsNullOrWhiteSpace(csvType)) return EquipmentType.UnknownType;
        return csvType.Trim().ToUpperInvariant() switch
        {
            "LAPTOP" => EquipmentType.Laptop,
            "DESKTOP" => EquipmentType.Desktop,
            "MONITOR" => EquipmentType.Monitor,
            "PRINTER" => EquipmentType.Printer,
            "NVR" => EquipmentType.Nvr,
            _ => EquipmentType.UnknownType
        };
    }
}
