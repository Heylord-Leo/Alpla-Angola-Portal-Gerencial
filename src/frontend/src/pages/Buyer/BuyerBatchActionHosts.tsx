import React, { useEffect, useState } from 'react';
import { ModalWrapper } from '../../components/common/ModalWrapper';
import { PartialApprovalBatchModal } from './PartialApprovalBatchModal';
import { BatchReworkModal } from './BatchReworkModal';
import { api } from '../../lib/api';
import { toWizardActiveRequest } from './QuotationWizard/workspaceWizardRequest';
import type { BatchItemInput, ExtraItemDecisionPayload } from '../../types';

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3C — thin Workspace hosts that mount the EXISTING, unmodified batch modals
// (PartialApprovalBatchModal / BatchReworkModal) so "Enviar itens para aprovação" and
// "Revisar e reenviar lote" run INSIDE the Workspace instead of navigating to the classic screen.
// The single coupling to the classic screen (its client-side grouping) is removed by loading the
// canonical request via api.requests.get(requestId) — the same RequestDetailsDto the modals consume
// as their `group`. No modal internals, eligibility rules, batch-creation logic, or endpoints are
// duplicated; persistence stays in the existing modals/handlers.
// ─────────────────────────────────────────────────────────────────────────────

function LoadingModal({ title, onClose }: { title: string; onClose: () => void }) {
  return <ModalWrapper title={title} onClose={onClose}><div style={{ padding: 32, color: 'var(--color-text-muted)' }}>Carregando pedido…</div></ModalWrapper>;
}
function InfoModal({ title, message, onClose }: { title: string; message: string; onClose: () => void }) {
  return (
    <ModalWrapper title={title} onClose={onClose}>
      <div style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div style={{ color: 'var(--color-text-main)', fontSize: '0.88rem' }}>{message}</div>
        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button onClick={onClose} style={{ padding: '9px 14px', borderRadius: 10, border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', fontWeight: 700, cursor: 'pointer' }}>Fechar</button>
        </div>
      </div>
    </ModalWrapper>
  );
}

function useRequest(requestId: string) {
  const [request, setRequest] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    setLoading(true); setError(null);
    // RequestDetailsDto exposes the GUID as `id` only — the classic group contract the batch modals
    // follow (BatchReworkModal calls updateApprovalBatch/resubmitApprovalBatch with `group.requestId`)
    // expects `requestId`. Stamp it once here, at the host boundary, from the authoritative fetch key —
    // same normalization as the wizard host (REQ-24/08/2026-293); without it the rework calls hit
    // /api/v1/requests/undefined/... (HTTP 404, route `{requestId:guid}` never matches).
    api.requests.get(requestId)
      .then(r => { if (alive) setRequest(toWizardActiveRequest(r, requestId)); })
      .catch(e => { if (alive) setError(e?.message || 'Falha ao carregar o pedido.'); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [requestId]);
  return { request, loading, error };
}

/** Host B — send covered/eligible items to approval (reuses PartialApprovalBatchModal). */
export function BuyerApprovalBatchHost({ requestId, onClose, onCompleted }: { requestId: string; onClose: () => void; onCompleted: (message: string) => void }) {
  const { request, loading, error } = useRequest(requestId);

  const submit = async (items: BatchItemInput[], extraItemDecisions?: Record<string, ExtraItemDecisionPayload>) => {
    // Must throw on failure so the modal renders the error inline (mirrors the classic handler).
    await api.requests.createApprovalBatch(requestId, items, undefined, extraItemDecisions);
    onCompleted('Itens enviados para aprovação.');
  };

  if (loading) return <LoadingModal title="Enviar itens para aprovação" onClose={onClose} />;
  if (error || !request) return <InfoModal title="Enviar itens para aprovação" message={error || 'Pedido não encontrado.'} onClose={onClose} />;
  return <PartialApprovalBatchModal isOpen onClose={onClose} group={request} onSubmit={submit} />;
}

/** Host C — rework a returned batch (reuses BatchReworkModal; it owns its own update/resubmit calls). */
export function BuyerBatchReworkHost({ requestId, onClose, onCompleted, onManageQuotations }: { requestId: string; onClose: () => void; onCompleted: (message: string) => void; onManageQuotations?: () => void }) {
  const { request, loading, error } = useRequest(requestId);
  const adjustmentBatch = (request?.approvalBatches || []).find((b: any) => b.status === 'AREA_ADJUSTMENT' || b.status === 'FINAL_ADJUSTMENT') || null;

  if (loading) return <LoadingModal title="Revisar e reenviar lote" onClose={onClose} />;
  if (error || !request) return <InfoModal title="Revisar e reenviar lote" message={error || 'Pedido não encontrado.'} onClose={onClose} />;
  if (!adjustmentBatch) return <InfoModal title="Revisar e reenviar lote" message="Nenhum lote em ajuste foi encontrado para este pedido." onClose={onClose} />;
  return <BatchReworkModal isOpen onClose={onClose} group={request} batch={adjustmentBatch} onSuccess={(message) => onCompleted(message)} onManageQuotations={onManageQuotations} />;
}
