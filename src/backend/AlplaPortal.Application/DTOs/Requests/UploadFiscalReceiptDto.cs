using System;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Binds an already-stored TYPE_FISCAL_RECEIPT attachment to a PO group as its terminal closing
/// document (Release 4 Phase 4B). The attachment is created first through the standard upload
/// path; this DTO carries only its identity — the binding endpoint validates everything else.
/// </summary>
public class UploadFiscalReceiptDto
{
    public Guid AttachmentId { get; set; }
}
