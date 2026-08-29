import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams, useLocation, useSearchParams } from 'react-router-dom';
import {
  ArrowLeft, Building2, User as UserIcon, CalendarClock, MapPin, ExternalLink,
  Package, FileText, Layers, Info, BadgeCheck, AlertTriangle, ShoppingCart,
  ChevronLeft, ChevronRight, Mail, Upload, Pencil, MoreVertical, Ban,
} from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { BuyerLotTimeline } from './BuyerLotTimeline';
import { BuyerApprovalBatchHost, BuyerBatchReworkHost } from './BuyerBatchActionHosts';
import { QuotationWizardModal } from './QuotationWizard/QuotationWizardModal';
import { useWorkspaceWizardHost } from './QuotationWizard/hooks/useWorkspaceWizardHost';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { BuyerSupplierFichaDrawer, BuyerSupplierFichaDrawerHandle } from './BuyerSupplierFichaDrawer';
import { CloseNotQuotedModal } from './CloseNotQuotedModal';
import { formatDateTime } from '../../lib/utils';
import { api } from '../../lib/api';
import type { BuyerWorkspace, BuyerWorkspaceItem } from '../../types/buyerWorkspace';
import { operationalStateColor, deadlineChip, NEED_LEVEL_LABEL } from './buyerQueueView';
import {
  WORKSPACE_TABS, resolveTab, backToQueueTarget, coverageChips, bucketLabel,
  formatTotalsByCurrency, metricOrAbsent, batchKindLabel, supplierStatusLabel,
  lotLineNumbersLabel, lotItemCountLabel, clampIndex,
} from './buyerWorkspaceView';
import { buildQuotationDraft, openItemsForQuotation } from './buyerQuotationRequestEmail';

export function BuyerRequestWorkspace() {
  const { requestId } = useParams<{ requestId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const tab = resolveTab(params.get('tab'));

  const [ws, setWs] = useState<BuyerWorkspace | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadWorkspace = useCallback((silent = false) => {
    if (!requestId) return;
    if (!silent) setLoading(true);
    setError(null);
    api.buyerQueue.getWorkspace(requestId)
      .then(setWs)
      .catch(e => setError(e?.message || 'Falha ao carregar o workspace.'))
      .finally(() => setLoading(false));
  }, [requestId]);
  useEffect(() => { loadWorkspace(); }, [loadWorkspace]);

  const backTarget = useMemo(() => backToQueueTarget((location.state as any)?.from), [location.state]);
  const setTab = (id: string) => { const p = new URLSearchParams(params); p.set('tab', id); setParams(p, { replace: true }); };
  const [feedback, setFeedback] = useState<string | null>(null);
  const [activeHost, setActiveHost] = useState<'approval' | 'rework' | null>(null);
  const flash = (msg: string) => { setFeedback(msg); window.setTimeout(() => setFeedback(null), 5000); };

  // After an in-Workspace mutation (approval/rework), refresh the canonical projection silently so
  // operational state, next action, coverage, items, quotations, suppliers, batches and timeline all
  // reflect the change without a full reload (current tab + route + back-state are preserved).
  const afterMutation = (msg: string) => { setActiveHost(null); flash(msg); loadWorkspace(true); };

  // In-Workspace Quotation Wizard host (Stage 2B-R): reuses the accepted shared controller with
  // Workspace-local state. Successful saves silently refresh the whole projection.
  const wizardHost = useWorkspaceWizardHost({
    onSaved: () => loadWorkspace(true),
    onFeedback: (f) => flash(f.message),
  });

  // In-Workspace Supplier Sheet drawer (Stage 3D). Opened imperatively from the supplier carousel so the
  // carousel's own index/scroll state is preserved; a successful save silently refreshes the projection
  // (updates the carousel card) without a full reload.
  const supplierDrawerRef = useRef<BuyerSupplierFichaDrawerHandle>(null);

  // "Desconsiderar item" / close-not-quoted (Stage 3E.1) — reuses the SAME shared modal + endpoint the
  // classic screen uses. Eligibility is the server-computed item.canCloseNotQuoted flag; a successful
  // close silently refreshes the canonical projection (coverage, next action, item eligibility).
  const [closeItem, setCloseItem] = useState<{ lineItemId: string; description: string; isLastPending: boolean } | null>(null);

  // "Solicitar cotação" (Outlook helper — NOT the Wizard). One canonical message; NO copy/paste:
  // short drafts open via mailto (compose + signature); long drafts are delivered as an .eml draft
  // (X-Unsent) that opens Outlook as an editable compose carrying the COMPLETE body.
  const solicitarCotacao = () => {
    if (!ws) return;
    const draft = buildQuotationDraft(ws, ws.items);
    if (draft.fits) {
      window.location.href = draft.mailtoFull;
      flash('Rascunho de e-mail aberto no Outlook.');
    } else {
      const blob = new Blob([draft.eml], { type: 'message/rfc822' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = draft.emlFilename;
      document.body.appendChild(a); a.click(); a.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 4000);
      flash('Rascunho de e-mail transferido — abra o ficheiro para editar e enviar no Outlook (contém todos os itens).');
    }
  };

  if (loading) return <PageContainer><div style={{ padding: 40, color: 'var(--color-text-muted)' }}>Carregando workspace…</div></PageContainer>;
  if (error || !ws) return (
    <PageContainer>
      <button onClick={() => navigate(backTarget)} style={ghostBtn}><ArrowLeft size={15} /> Voltar para fila</button>
      <div style={{ padding: 24, background: 'var(--color-status-red-surface)', color: 'var(--color-status-red)', borderRadius: 10 }}>{error || 'Pedido não encontrado.'}</div>
    </PageContainer>
  );

  const stateColor = operationalStateColor(ws);
  const dChip = deadlineChip(ws);
  const nextAction = ws.nextActions.find(a => a.actionable) || ws.nextActions[0];
  const openItems = openItemsForQuotation(ws.items);
  const hasOpenItems = openItems.length > 0;
  // Server-authorized next step (ADD_QUOTATION | SUBMIT_BATCH | RESOLVE_ADJUSTMENT); AWAITING_* → null.
  const actionableCode = nextAction && nextAction.actionable && nextAction.code !== 'NONE' ? nextAction.code : null;

  return (
    <PageContainer>
      {/* Top bar: back + link to the classic workbench for the actions NOT yet migrated to the Workspace
          (editar/excluir cotação, remover proforma, encerrar sem cotação, reutilizar cotação, cancelar lote,
          cancelar pedido). Retained until those flows land in the Workspace. */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
        <button onClick={() => navigate(backTarget)} style={ghostBtn}><ArrowLeft size={15} /> Voltar para fila</button>
        <button onClick={() => navigate(`/buyer/items/classic?search=${encodeURIComponent(ws.requestNumber)}`)} style={classicLink} title="Ações ainda não migradas: editar/excluir cotação, remover proforma, encerrar sem cotação, reutilizar cotação, cancelar lote/pedido">
          <ExternalLink size={13} /> Abrir ferramentas clássicas
        </button>
      </div>

      {feedback && (
        <div role="status" style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 14px', borderRadius: 10, background: 'color-mix(in srgb, var(--color-primary) 8%, var(--color-bg-surface))', border: '1px solid color-mix(in srgb, var(--color-primary) 30%, var(--color-border))', color: 'var(--color-primary)', fontSize: '0.84rem', fontWeight: 600 }}>
          <Info size={15} /> {feedback}
        </div>
      )}

      {/* Header */}
      <div style={{ background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderLeft: `4px solid ${stateColor}`, borderRadius: 14, padding: '20px 22px', display: 'flex', flexDirection: 'column', gap: 14 }}>
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
          <div style={{ minWidth: 0 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <h1 style={{ fontSize: '1.4rem', fontWeight: 900, color: 'var(--color-primary)', margin: 0 }}>{ws.requestNumber}</h1>
              <span style={{ padding: '3px 10px', borderRadius: 8, background: `color-mix(in srgb, ${stateColor} 12%, transparent)`, color: stateColor, fontWeight: 700, fontSize: '0.78rem' }}>{ws.operationalStateLabel}</span>
              {dChip && <span style={{ padding: '3px 10px', borderRadius: 999, background: `color-mix(in srgb, ${dChip.color} 12%, transparent)`, color: dChip.color, fontWeight: 700, fontSize: '0.72rem' }}>{dChip.label}</span>}
              {ws.requiresAttention && <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: 'var(--color-status-red)', fontSize: '0.74rem', fontWeight: 700 }}><AlertTriangle size={13} /> Requer atenção</span>}
            </div>
            <div style={{ color: 'var(--color-text-main)', fontWeight: 600, marginTop: 4 }}>{ws.title}</div>
          </div>
          {/* Next action + real operations. "Solicitar cotação" (Outlook) is fully in-Workspace; the
              workflow step bridges to the existing classic Wizard/batch host (no logic duplicated). */}
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8, minWidth: 0 }}>
            {nextAction && (
              <div style={{ textAlign: 'right' }}>
                <div style={{ fontSize: '0.62rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)' }}>Próxima ação</div>
                <div style={{ fontSize: '0.92rem', fontWeight: 700, color: actionableCode ? 'var(--color-text-main)' : 'var(--color-text-muted)' }}>
                  {nextAction.label || 'Sem ação do comprador'}
                </div>
              </div>
            )}
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
              {hasOpenItems && (
                <button onClick={solicitarCotacao} style={outlineBtn}><Mail size={15} /> Solicitar cotação</button>
              )}
              {/* SUBMIT_BATCH / RESOLVE_ADJUSTMENT run IN the Workspace (Hosts B/C). ADD_QUOTATION still
                  bridges to the classic Wizard host until the Wizard host lands. */}
              {actionableCode === 'SUBMIT_BATCH' && (
                <button onClick={() => setActiveHost('approval')} style={primaryBtn}>{nextAction!.label}</button>
              )}
              {actionableCode === 'RESOLVE_ADJUSTMENT' && (
                <>
                  <button onClick={() => setActiveHost('rework')} style={primaryBtn}>{nextAction!.label}</button>
                  {/* QF4: the SAME quotation wizard the classic screen already allows during
                      adjustment states (canMutateQuotation includes AREA/FINAL_ADJUSTMENT) — outline
                      styling keeps the rework action primary. New quotation lines mapped to the
                      batch's items surface as addable options in the rework modal. */}
                  <button onClick={() => wizardHost.openAddQuotation(ws.requestId, 'UPLOAD')} style={outlineBtn} title="Importar documento (PDF/imagem) e extrair a cotação por OCR">
                    <Upload size={15} /> Importar Cotação
                  </button>
                  <button onClick={() => wizardHost.openAddQuotation(ws.requestId, 'MANUAL')} style={outlineBtn} title="Introduzir os valores da cotação manualmente">
                    <Pencil size={15} /> Inserir Manualmente
                  </button>
                </>
              )}
              {actionableCode === 'ADD_QUOTATION' && (
                // The server-derived next action (Adicionar cotação / Completar cotações) stays as the
                // PRÓXIMA AÇÃO label above; here we expose the TWO explicit ENTRY METHODS. Both launch the
                // SAME "REGISTRAR NOVA COTAÇÃO" wizard through the SAME shared controller, differing only in
                // the canonical Wizard source ('UPLOAD' → document/OCR, 'MANUAL' → priceable rows). No new
                // operational-state codes; no classic navigation.
                <>
                  <button onClick={() => wizardHost.openAddQuotation(ws.requestId, 'UPLOAD')} style={primaryBtn} title="Importar documento (PDF/imagem) e extrair a cotação por OCR">
                    <Upload size={15} /> Importar Cotação
                  </button>
                  <button onClick={() => wizardHost.openAddQuotation(ws.requestId, 'MANUAL')} style={outlineBtn} title="Introduzir os valores da cotação manualmente">
                    <Pencil size={15} /> Inserir Manualmente
                  </button>
                </>
              )}
            </div>
          </div>
        </div>

        {/* Meta grid */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 12 }}>
          <Meta icon={<AlertTriangle size={13} />} label="Grau de necessidade">{ws.needLevelCode ? (NEED_LEVEL_LABEL[ws.needLevelCode] ?? ws.needLevelCode) : '—'}</Meta>
          <Meta icon={<UserIcon size={13} />} label="Solicitante">{ws.requesterName || '—'}</Meta>
          <Meta icon={<UserIcon size={13} />} label="Comprador">{ws.buyerName || 'Não atribuído'}</Meta>
          <Meta icon={<Building2 size={13} />} label="Empresa / Planta">{[ws.companyName, ws.plantName].filter(Boolean).join(' · ') || '—'}</Meta>
          <Meta icon={<MapPin size={13} />} label="Departamento">{ws.departmentName || '—'}</Meta>
          <Meta icon={<CalendarClock size={13} />} label="Necessário até">{fmtDate(ws.needByDateUtc)}</Meta>
        </div>
      </div>

      {/* Supplier Intelligence — contextual (only suppliers involved in THIS request) */}
      <SupplierCarousel ws={ws} onOpenProfile={(id) => supplierDrawerRef.current?.open(id)} />

      {/* Two-column: tabs+content (left) · request details + timeline (right) */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, alignItems: 'flex-start' }}>
        <div style={{ flex: '2 1 520px', minWidth: 0, display: 'flex', flexDirection: 'column', gap: 14 }}>
          {/* Tabs */}
          <div style={{ display: 'flex', gap: 4, borderBottom: '1px solid var(--color-border)' }}>
            {WORKSPACE_TABS.map(t => {
              const active = tab === t.id;
              const icon = t.id === 'items' ? <Package size={15} /> : t.id === 'quotes' ? <FileText size={15} /> : <Layers size={15} />;
              return (
                <button key={t.id} onClick={() => setTab(t.id)} style={{
                  display: 'inline-flex', alignItems: 'center', gap: 6, padding: '10px 14px', border: 'none', background: 'transparent', cursor: 'pointer',
                  fontSize: '0.85rem', fontWeight: 700, color: active ? 'var(--color-primary)' : 'var(--color-text-muted)',
                  borderBottom: `2px solid ${active ? 'var(--color-primary)' : 'transparent'}`, marginBottom: -1,
                }}>{icon} {t.label}</button>
              );
            })}
          </div>

          {tab === 'items' && <TabItems ws={ws} onDisregard={(it) => setCloseItem({ lineItemId: it.id, description: it.description, isLastPending: ws.coverage.pending === 1 })} />}
          {tab === 'quotes' && <TabQuotes ws={ws} />}
          {tab === 'batches' && <TabBatches ws={ws} />}
        </div>

        <div style={{ flex: '1 1 300px', minWidth: 280, display: 'flex', flexDirection: 'column', gap: 14 }}>
          <Panel title="Detalhes do pedido" icon={<Info size={15} />}>
            <DetailRow label="Criado em">{fmtDateTime(ws.createdAtUtc)}</DetailRow>
            <DetailRow label="Criado por">{ws.createdByName || ws.requesterName || '—'}</DetailRow>
            {ws.description && <div style={{ marginTop: 8, fontSize: '0.82rem', color: 'var(--color-text-main)', whiteSpace: 'pre-wrap' }}>{ws.description}</div>}
            {/* Tertiary technical metadata — demoted below the Buyer operational state. */}
            <div style={{ marginTop: 10, paddingTop: 8, borderTop: '1px dashed var(--color-border)' }}>
              <DetailRow label="Estado do sistema"><span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', fontFamily: 'monospace' }}>{ws.requestStatusCode}</span></DetailRow>
            </div>
          </Panel>
          <Panel title="Linha do Tempo dos Lotes" icon={<CalendarClock size={15} />}>
            <BuyerLotTimeline requestId={ws.requestId} />
          </Panel>
        </div>
      </div>

      {/* In-Workspace batch action hosts (reuse the existing modals unmodified). */}
      {activeHost === 'approval' && (
        <BuyerApprovalBatchHost requestId={ws.requestId} onClose={() => setActiveHost(null)} onCompleted={afterMutation} />
      )}
      {activeHost === 'rework' && (
        <BuyerBatchReworkHost
          requestId={ws.requestId}
          onClose={() => setActiveHost(null)}
          onCompleted={afterMutation}
          // QF4: bridge to the existing quotation tools — close the rework host and land on the
          // Workspace quotations tab (route-backed, so returning to the rework action is one click).
          onManageQuotations={() => { setActiveHost(null); setTab('quotes'); }}
        />
      )}

      {/* In-Workspace Supplier Sheet drawer — mounts the SHARED SupplierFichaDetailContent (Stage 3D). */}
      <BuyerSupplierFichaDrawer ref={supplierDrawerRef} onSaved={() => loadWorkspace(true)} />

      {/* "Desconsiderar item" — the SAME shared modal + endpoint as classic (Stage 3E.1). */}
      {closeItem && (
        <CloseNotQuotedModal
          isOpen
          requestId={ws.requestId}
          lineItemId={closeItem.lineItemId}
          itemDescription={closeItem.description}
          isLastPendingItem={closeItem.isLastPending}
          onClose={() => setCloseItem(null)}
          onSuccess={() => { setCloseItem(null); loadWorkspace(true); flash('Item encerrado sem cotação.'); }}
        />
      )}

      {/* In-Workspace Quotation Wizard — the SAME modal + shared controller the classic screen uses.
          Rendered UNCONDITIONALLY (like BuyerItemsList): the modal is the single source of visibility
          via wizardState.isOpen internally (it early-returns null when closed and owns its own mounted
          + portal + body-scroll-lock lifecycle). A host-level `{isOpen && ...}` guard remounts the
          modal on every open — causing the mount flash, the scroll-lock leak, and the disrupted OCR
          flow that surfaced the generic "Erro ao processar documento via OCR." banner. */}
      <QuotationWizardModal
        request={wizardHost.wizardActiveRequest}
        wizardState={wizardHost.wizardState}
        onSaveQuotation={wizardHost.controller.handleWizardSaveQuotation}
        onReconcilePreview={wizardHost.controller.handleReconcilePreview}
        isProcessingOcr={!!(wizardHost.wizardActiveRequest && wizardHost.isProcessingOcr[wizardHost.wizardActiveRequest.requestId])}
        onUploadFile={wizardHost.onUploadFile}
        onCancelWizard={wizardHost.controller.onCancelWizard}
        onReplaceDocument={wizardHost.controller.handleReplaceDocumentForWizard}
        ivaRates={wizardHost.ivaRates}
        units={wizardHost.units}
        currencies={wizardHost.currencies}
        onRequestLineItemUpserted={wizardHost.controller.handleWizardLineItemUpserted}
      />

      {/* Duplicate-file protection preserved (same api.attachments.checkDuplicate decision), surfaced
          as a standard confirmation — NOT a copy of the classic 60-line modal/countdown. */}
      {wizardHost.dupWarning && (
        <ConfirmationDialog
          title="Documento possivelmente duplicado"
          variant="warning"
          confirmText="Prosseguir mesmo assim"
          cancelText="Cancelar"
          onConfirm={wizardHost.confirmDupUpload}
          onCancel={wizardHost.dismissDup}
          message={
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: '0.85rem' }}>
              <div>O ficheiro <strong>{wizardHost.dupWarning.fileName}</strong> já foi carregado{wizardHost.dupWarning.requestNumber ? ` no pedido ${wizardHost.dupWarning.requestNumber}` : ''}.</div>
              {wizardHost.dupWarning.uploadedBy && <div style={{ color: 'var(--color-text-muted)' }}>Enviado por {wizardHost.dupWarning.uploadedBy}{wizardHost.dupWarning.createdAtUtc ? ` em ${formatDateTime(wizardHost.dupWarning.createdAtUtc)}` : ''}.</div>}
              <div>Deseja prosseguir com o carregamento?</div>
            </div>
          }
        />
      )}
    </PageContainer>
  );
}

// ── Tab 1: Itens & Cobertura ──
function TabItems({ ws, onDisregard }: { ws: BuyerWorkspace; onDisregard: (item: BuyerWorkspaceItem) => void }) {
  const c = ws.coverage;
  const [openKebab, setOpenKebab] = useState<string | null>(null);
  // Render the row action only for items the SERVER marks eligible.
  const anyActionable = ws.items.some(it => it.canCloseNotQuoted);
  // Secondary buckets as compact chips; legacy not-quoted states only when present.
  const secondary = coverageChips(c).filter(ch => !['total', 'treated', 'pending'].includes(ch.key));
  const prominent = [
    { label: 'Total de itens', value: c.totalItems, color: 'var(--color-text-main)' },
    { label: 'Tratados', value: c.treated, color: 'var(--color-status-green)' },
    { label: 'Pendentes', value: c.pending, color: c.pending > 0 ? 'var(--color-status-orange)' : 'var(--color-text-muted)' },
  ];
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 10 }}>
        {prominent.map(p => (
          <div key={p.label} style={{ background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 10, padding: '12px 14px' }}>
            <div style={{ fontSize: '1.6rem', fontWeight: 800, lineHeight: 1, color: p.color }}>{p.value}</div>
            <div style={{ fontSize: '0.68rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.03em', color: 'var(--color-text-muted)', marginTop: 4 }}>{p.label}</div>
          </div>
        ))}
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
        {secondary.map(ch => (
          <span key={ch.key} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '5px 10px', borderRadius: 999, background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
            {ch.label}<strong style={{ color: 'var(--color-text-main)' }}>{ch.value}</strong>
          </span>
        ))}
      </div>
      {ws.items.length === 0 ? <Empty>Sem itens.</Empty> : (
        <div style={{ overflowX: 'auto' }}>
          <table style={tableStyle}>
            <thead><tr>
              <Th>#</Th><Th>Código</Th><Th>Descrição</Th><Th style={{ textAlign: 'right' }}>Qtd.</Th><Th>Un.</Th><Th>Cobertura</Th><Th>Fornecedor / seleção</Th>
              {anyActionable && <Th style={{ width: 40 }}></Th>}
            </tr></thead>
            <tbody>
              {ws.items.map(it => (
                <tr key={it.id} style={{ borderTop: '1px solid var(--color-border)' }}>
                  <Td>{it.lineNumber}</Td>
                  <Td>{it.itemCatalogCode || '—'}</Td>
                  <Td>{it.description}</Td>
                  <Td style={{ textAlign: 'right' }}>{it.quantity}</Td>
                  <Td>{it.unitName || '—'}</Td>
                  <Td><span style={bucketChip(it.coverageBucket)}>{bucketLabel(it.coverageBucket)}</span></Td>
                  <Td>{it.selectedQuotationSummary || it.supplierName || '—'}</Td>
                  {anyActionable && (
                    <Td style={{ textAlign: 'right', position: 'relative' }}>
                      {it.canCloseNotQuoted && (
                        <>
                          <button
                            onClick={() => setOpenKebab(prev => (prev === it.id ? null : it.id))}
                            aria-label="Ações do item"
                            style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 28, height: 28, borderRadius: 6, border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: 'var(--color-text-muted)', cursor: 'pointer' }}
                          >
                            <MoreVertical size={15} />
                          </button>
                          {openKebab === it.id && (
                            <>
                              <div onClick={() => setOpenKebab(null)} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
                              <div style={{ position: 'absolute', right: 0, top: 32, zIndex: 41, minWidth: 190, background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 8, boxShadow: '0 8px 20px rgba(0,0,0,0.18)', padding: 4 }}>
                                <button
                                  onClick={() => { setOpenKebab(null); onDisregard(it); }}
                                  style={{ display: 'flex', alignItems: 'center', gap: 8, width: '100%', textAlign: 'left', padding: '8px 10px', borderRadius: 6, border: 'none', background: 'none', color: 'var(--color-text-main)', fontSize: '0.82rem', fontWeight: 600, cursor: 'pointer' }}
                                >
                                  <Ban size={14} /> Desconsiderar item
                                </button>
                              </div>
                            </>
                          )}
                        </>
                      )}
                    </Td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ── Tab 2: Cotações & Documentos ──
function TabQuotes({ ws }: { ws: BuyerWorkspace }) {
  if (ws.quotations.length === 0) return <Empty>Nenhuma cotação registada para este pedido.</Empty>;
  const nowrap: React.CSSProperties = { whiteSpace: 'nowrap' };
  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ ...tableStyle, tableLayout: 'fixed', minWidth: 680 }}>
        <colgroup>
          <col style={{ width: 'auto' }} />{/* Fornecedor — largest flexible */}
          <col style={{ width: 120 }} />{/* Documento */}
          <col style={{ width: 96 }} />{/* Data */}
          <col style={{ width: 64 }} />{/* Itens */}
          <col style={{ width: 130 }} />{/* Total */}
          <col style={{ width: 56 }} />{/* Docs */}
          <col style={{ width: 118 }} />{/* Estado */}
        </colgroup>
        <thead><tr>
          <Th>Fornecedor</Th><Th style={nowrap}>Documento</Th><Th style={nowrap}>Data</Th><Th style={{ ...nowrap, textAlign: 'center' }}>Itens</Th><Th style={{ ...nowrap, textAlign: 'right' }}>Total</Th><Th style={{ ...nowrap, textAlign: 'center' }}>Docs</Th><Th style={nowrap}>Estado</Th>
        </tr></thead>
        <tbody>
          {ws.quotations.map(q => (
            <tr key={q.id} style={{ borderTop: '1px solid var(--color-border)' }}>
              <Td style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={q.supplierName ?? undefined}>{q.supplierName || '—'}</Td>
              <Td style={{ ...nowrap, overflow: 'hidden', textOverflow: 'ellipsis' }} title={q.documentNumber ?? undefined}>{q.documentNumber || '—'}</Td>
              <Td style={nowrap}>{fmtDate(q.documentDate)}</Td>
              <Td style={{ textAlign: 'center' }}>{q.itemsQuotedCount}</Td>
              <Td style={{ ...nowrap, textAlign: 'right' }}>{q.currency ? `${fmtAmount(q.totalAmount)} ${q.currency}` : fmtAmount(q.totalAmount)}</Td>
              <Td style={{ textAlign: 'center' }}>{q.documentCount}</Td>
              <Td style={nowrap}>{q.isSelected ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: 'var(--color-status-green)', fontWeight: 700, fontSize: '0.76rem' }}><BadgeCheck size={13} /> Selecionada</span> : <span style={{ color: 'var(--color-text-muted)', fontSize: '0.78rem' }}>—</span>}</Td>
            </tr>
          ))}
        </tbody>
      </table>
      <p style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: 8 }}>Valores apresentados por moeda; moedas diferentes nunca são somadas.</p>
    </div>
  );
}

// ── Tab 3: Lotes & Aprovações ──
function TabBatches({ ws }: { ws: BuyerWorkspace }) {
  if (ws.batches.length === 0) return <Empty>Nenhum lote de aprovação criado.</Empty>;
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {ws.batches.map(b => (
        <div key={b.id} style={{ background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 10, padding: '12px 14px', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 0 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <span style={{ fontWeight: 800, color: 'var(--color-primary)' }}>Lote {b.batchNumber}</span>
              <span style={batchKindChip(b.kind)}>{batchKindLabel(b.kind)}</span>
              <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>{lotItemCountLabel(b.itemCount)}</span>
            </div>
            <div style={{ fontSize: '0.74rem', color: 'var(--color-text-muted)' }}>{lotLineNumbersLabel(b.itemLineNumbers)}</div>
          </div>
          <div style={{ textAlign: 'right', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
            <div>Criado {fmtDate(b.createdAtUtc)}</div>
            {b.areaDecisionAtUtc && <div>Decisão de área {fmtDate(b.areaDecisionAtUtc)}</div>}
            {b.approvedTotalAmount != null && <div>Aprovado: {fmtAmount(b.approvedTotalAmount)}</div>}
          </div>
        </div>
      ))}
    </div>
  );
}

function SupplierCarousel({ ws, onOpenProfile }: { ws: BuyerWorkspace; onOpenProfile: (supplierId: number) => void }) {
  const scroller = useRef<HTMLDivElement>(null);
  const [active, setActive] = useState(0);
  const suppliers = ws.suppliers;
  if (suppliers.length === 0) return null;

  const CARD = 272; // card + gap
  const go = (dir: -1 | 1) => {
    const next = clampIndex(active + dir, suppliers.length);
    setActive(next);
    scroller.current?.scrollTo({ left: next * CARD, behavior: 'smooth' });
  };
  const jump = (i: number) => { setActive(i); scroller.current?.scrollTo({ left: i * CARD, behavior: 'smooth' }); };
  const onScroll = () => { if (scroller.current) setActive(clampIndex(Math.round(scroller.current.scrollLeft / CARD), suppliers.length)); };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: '0.8rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
          <ShoppingCart size={16} /> Inteligência dos Fornecedores deste Pedido
          <span style={{ fontSize: '0.72rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>· {suppliers.length} {suppliers.length === 1 ? 'fornecedor' : 'fornecedores'}</span>
        </div>
        {suppliers.length > 1 && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <button onClick={() => go(-1)} disabled={active <= 0} style={navBtn(active <= 0)} aria-label="Fornecedor anterior"><ChevronLeft size={16} /></button>
            <span style={{ fontSize: '0.72rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>{active + 1} / {suppliers.length}</span>
            <button onClick={() => go(1)} disabled={active >= suppliers.length - 1} style={navBtn(active >= suppliers.length - 1)} aria-label="Próximo fornecedor"><ChevronRight size={16} /></button>
          </div>
        )}
      </div>
      <div ref={scroller} onScroll={onScroll} style={{ display: 'flex', gap: 12, overflowX: 'auto', paddingBottom: 6, scrollbarWidth: 'thin' }}>
        {suppliers.map((s, i) => (
          <div key={s.supplierId ?? i} style={{ flex: '0 0 260px', background: 'var(--color-bg-surface)', border: `1px solid ${s.involvedSelected ? 'var(--color-status-green)' : 'var(--color-border)'}`, borderRadius: 12, padding: 14, display: 'flex', flexDirection: 'column', gap: 8 }}>
            {/* Hierarchy: name → NIF · status → commercial metrics */}
            <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 8 }}>
              <span style={{ fontWeight: 800, fontSize: '0.9rem', color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={s.name}>{s.name}</span>
              {s.involvedSelected && <span style={{ fontSize: '0.6rem', fontWeight: 800, color: 'var(--color-status-green)', whiteSpace: 'nowrap' }}>SELECIONADO</span>}
            </div>
            <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>NIF {s.nif || '—'} · {supplierStatusLabel(s)}</div>
            <div style={{ height: 1, background: 'var(--color-border)' }} />
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, fontSize: '0.74rem' }}>
              <Stat label="Nº compras">{metricOrAbsent(s.purchaseCount)}</Stat>
              <Stat label="Última compra">{s.lastPurchaseUtc ? fmtDate(s.lastPurchaseUtc) : 'Sem histórico'}</Stat>
              <Stat label="Cotações recebidas">{s.quotationsReceived}</Stat>
              <Stat label="Cotações selecionadas">{s.quotationsSelected}</Stat>
            </div>
            <div style={{ fontSize: '0.74rem' }}>
              <div style={statLabel}>Total comprado</div>
              <div style={{ color: 'var(--color-text-main)', fontWeight: 600 }}>{formatTotalsByCurrency(s.totalsByCurrency)}</div>
            </div>
            {/* Perfil completo (Stage 3D): opens the SHARED Supplier Sheet in a right-side drawer for this
                involved supplier. Backend capabilities decide what the Buyer may view/edit. */}
            {s.supplierId != null && (
              <button
                onClick={() => onOpenProfile(s.supplierId!)}
                style={{ fontSize: '0.72rem', fontWeight: 700, color: 'var(--color-primary)', background: 'none', border: 'none', padding: 0, cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 4, marginTop: 2 }}
              >
                <ExternalLink size={11} /> Ver Perfil Completo
              </button>
            )}
          </div>
        ))}
      </div>
      {suppliers.length > 1 && (
        <div style={{ display: 'flex', justifyContent: 'center', gap: 6 }}>
          {suppliers.map((_, i) => (
            <button key={i} onClick={() => jump(i)} aria-label={`Fornecedor ${i + 1}`} style={{ width: 7, height: 7, borderRadius: 999, border: 'none', padding: 0, cursor: 'pointer', background: i === active ? 'var(--color-primary)' : 'color-mix(in srgb, var(--color-text-muted) 30%, transparent)' }} />
          ))}
        </div>
      )}
    </div>
  );
}

// ── small presentational helpers ──
function Meta({ icon, label, children }: { icon: React.ReactNode; label: string; children: React.ReactNode }) {
  return (
    <div style={{ minWidth: 0 }}>
      <div style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.03em', color: 'var(--color-text-muted)' }}>{icon} {label}</div>
      <div style={{ fontSize: '0.84rem', fontWeight: 600, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{children}</div>
    </div>
  );
}
function Panel({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <div style={{ background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 12, padding: 14 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: '0.78rem', fontWeight: 800, color: 'var(--color-text-main)', marginBottom: 10 }}>{icon} {title}</div>
      {children}
    </div>
  );
}
function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return <div style={{ display: 'flex', justifyContent: 'space-between', gap: 10, fontSize: '0.8rem', padding: '3px 0' }}><span style={{ color: 'var(--color-text-muted)' }}>{label}</span><span style={{ color: 'var(--color-text-main)', fontWeight: 600, textAlign: 'right' }}>{children}</span></div>;
}
function Stat({ label, children }: { label: string; children: React.ReactNode }) {
  return <div><div style={statLabel}>{label}</div><div style={{ color: 'var(--color-text-main)', fontWeight: 700 }}>{children}</div></div>;
}
function Empty({ children }: { children: React.ReactNode }) {
  return <div style={{ padding: '40px 20px', textAlign: 'center', border: '1px dashed var(--color-border)', borderRadius: 12, color: 'var(--color-text-muted)', fontSize: '0.85rem' }}>{children}</div>;
}
const Th = ({ children, style }: { children?: React.ReactNode; style?: React.CSSProperties }) => <th style={{ textAlign: 'left', padding: '8px 10px', fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.03em', color: 'var(--color-text-muted)', ...style }}>{children}</th>;
const Td = ({ children, style, title }: { children?: React.ReactNode; style?: React.CSSProperties; title?: string }) => <td title={title} style={{ padding: '9px 10px', fontSize: '0.82rem', color: 'var(--color-text-main)', ...style }}>{children}</td>;

function fmtDate(iso?: string | null): string { if (!iso) return '—'; try { return new Date(iso).toLocaleDateString('pt-PT', { day: '2-digit', month: 'short', year: 'numeric' }); } catch { return '—'; } }
function fmtDateTime(iso?: string | null): string { if (!iso) return '—'; try { return new Date(iso).toLocaleString('pt-PT', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }); } catch { return '—'; } }
function fmtAmount(n: number): string { return new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n); }

const tableStyle: React.CSSProperties = { width: '100%', borderCollapse: 'collapse', background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 12, overflow: 'hidden' };
const statLabel: React.CSSProperties = { fontSize: '0.64rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.02em', color: 'var(--color-text-muted)' };
const ghostBtn: React.CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 6, padding: '8px 12px', borderRadius: 8, border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', fontSize: '0.82rem', fontWeight: 700, cursor: 'pointer' };
const classicLink: React.CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 5, padding: '5px 10px', borderRadius: 8, border: '1px dashed var(--color-border)', background: 'transparent', color: 'var(--color-text-muted)', fontSize: '0.74rem', fontWeight: 600, cursor: 'pointer' };
const primaryBtn: React.CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 6, padding: '9px 14px', borderRadius: 10, border: 'none', background: 'var(--color-primary)', color: '#fff', fontSize: '0.8rem', fontWeight: 700, cursor: 'pointer', whiteSpace: 'nowrap' };
const outlineBtn: React.CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 6, padding: '9px 14px', borderRadius: 10, border: '1px solid var(--color-primary)', background: 'transparent', color: 'var(--color-primary)', fontSize: '0.8rem', fontWeight: 700, cursor: 'pointer', whiteSpace: 'nowrap' };
const navBtn = (disabled: boolean): React.CSSProperties => ({ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 30, height: 30, borderRadius: 8, border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: disabled ? 'var(--color-text-muted)' : 'var(--color-text-main)', cursor: disabled ? 'not-allowed' : 'pointer', opacity: disabled ? 0.5 : 1 });

function bucketChip(bucket: string): React.CSSProperties {
  const green = ['APPROVED']; const primary = ['IN_ACTIVE_BATCH', 'QUOTED_READY_FOR_BATCH']; const muted = ['CLOSED_NOT_QUOTED', 'CANCELLED_DELETED', 'NOT_QUOTED_ACCEPTED', 'NOT_QUOTED_PROPOSED'];
  const color = green.includes(bucket) ? 'var(--color-status-green)' : primary.includes(bucket) ? 'var(--color-primary)' : muted.includes(bucket) ? 'var(--color-text-muted)' : 'var(--color-status-blue)';
  return { display: 'inline-block', padding: '2px 8px', borderRadius: 999, fontSize: '0.7rem', fontWeight: 700, background: `color-mix(in srgb, ${color} 12%, transparent)`, color };
}
function batchKindChip(kind: string): React.CSSProperties {
  const color = kind === 'APPROVED' ? 'var(--color-status-green)' : kind === 'ACTIVE' ? 'var(--color-status-indigo)' : kind === 'REJECTED' ? 'var(--color-status-red)' : 'var(--color-text-muted)';
  return { padding: '2px 8px', borderRadius: 999, fontSize: '0.68rem', fontWeight: 700, background: `color-mix(in srgb, ${color} 12%, transparent)`, color };
}

export default BuyerRequestWorkspace;
