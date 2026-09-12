import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { X, Loader2, CheckCircle2, XCircle, RotateCcw, Send, FileText, Clock, Layers, ChevronRight, ChevronDown } from 'lucide-react';
import { api } from '../../../lib/api';
import type { ApprovalTimelineEvent } from '../../../types/approvalHistory';
import { groupTimelineEvents, localizeComment, type TimelineNode } from './timelineGrouping';

// v2.244.0 Approval Center V2 — Phase 2 timeline polish. Read-only per-request approval audit timeline.
// Presentation-only refinements over the backend events: (1) consecutive equivalent
// BATCH_CANDIDATES_SUBMITTED context events collapse into ONE expandable node (originals preserved);
// (2) known system comments show a PT presentation with the raw text one click away; (3) actual
// decisions read louder than context. No reorder, no data loss, no mutation.

interface Props {
  requestId: string;
  requestNumber: string;
  onClose: () => void;
}

function decisionColor(decision: string | null): string {
  switch (decision) {
    case 'APPROVED': return 'var(--color-status-green, #16a34a)';
    case 'REJECTED': return 'var(--color-status-red, #dc2626)';
    case 'RETURNED': return 'var(--color-status-amber, #d97706)';
    case 'RESUBMITTED': return 'var(--color-primary)';
    default: return 'var(--color-text-muted)';
  }
}

function EventIcon({ e }: { e: ApprovalTimelineEvent }) {
  const size = 15;
  if (e.decision === 'APPROVED') return <CheckCircle2 size={size} color={decisionColor(e.decision)} />;
  if (e.decision === 'REJECTED') return <XCircle size={size} color={decisionColor(e.decision)} />;
  if (e.decision === 'RETURNED') return <RotateCcw size={size} color={decisionColor(e.decision)} />;
  if (e.decision === 'RESUBMITTED') return <Send size={size} color={decisionColor(e.decision)} />;
  return <FileText size={size} color="var(--color-text-muted)" />;
}

function StageBadge({ level }: { level: 'AREA' | 'FINAL' }) {
  return (
    <span style={{ fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', padding: '1px 6px', borderRadius: 4, border: '1px solid var(--color-border)', color: 'var(--color-text-muted)' }}>
      {level === 'FINAL' ? 'Final' : 'Área'}
    </span>
  );
}

function LoteBadge({ n }: { n: number }) {
  return (
    <span style={{ fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase', padding: '1px 6px', borderRadius: 4, background: 'color-mix(in srgb, var(--color-primary) 10%, transparent)', color: 'var(--color-primary)' }}>
      Lote #{n}
    </span>
  );
}

function StatusTransition({ prev, next }: { prev: string | null; next: string | null }) {
  if (!prev && !next) return null;
  return (
    <div style={{ marginTop: 4, fontSize: '0.66rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
      {prev ?? '—'} → {next ?? '—'}
    </div>
  );
}

// A stored comment: PT presentation when it's a known system message, with the raw text one click away.
function CommentBlock({ comment }: { comment: string | null }) {
  const [showRaw, setShowRaw] = useState(false);
  const pres = localizeComment(comment);
  if (!pres) return null;
  return (
    <div style={{ marginTop: 6 }}>
      <div style={{ fontSize: '0.8rem', color: 'var(--color-text-main)', background: 'var(--color-bg-page)', border: '1px solid var(--color-border)', borderRadius: 6, padding: '6px 10px', lineHeight: 1.5, whiteSpace: 'pre-wrap' }}>
        {pres.text}
      </div>
      {pres.localized && (
        <>
          <button
            type="button" onClick={() => setShowRaw(v => !v)}
            style={{ marginTop: 4, background: 'none', border: 'none', padding: 0, cursor: 'pointer', color: 'var(--color-text-muted)', fontSize: '0.68rem', fontWeight: 700, textDecoration: 'underline' }}
          >
            {showRaw ? 'Ocultar detalhes técnicos' : 'Ver detalhes técnicos'}
          </button>
          {showRaw && (
            <div style={{ marginTop: 4, fontSize: '0.72rem', color: 'var(--color-text-muted)', fontStyle: 'italic', whiteSpace: 'pre-wrap' }}>{pres.raw}</div>
          )}
        </>
      )}
    </div>
  );
}

function SingleEvent({ e }: { e: ApprovalTimelineEvent }) {
  return (
    <div style={{ paddingBottom: 18 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <span style={{
          fontSize: e.isDecision ? '0.9rem' : '0.82rem',
          fontWeight: e.isDecision ? 800 : 600,
          color: e.isDecision ? decisionColor(e.decision) : 'var(--color-text-muted)',
        }}>
          {e.actionLabel}
        </span>
        {e.approvalLevel && <StageBadge level={e.approvalLevel} />}
        {e.batchNumber != null && <LoteBadge n={e.batchNumber} />}
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 3, fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
        <Clock size={11} /> {new Date(e.createdAtUtc).toLocaleString('pt-PT')} · {e.actorName}
      </div>
      <CommentBlock comment={e.comment} />
      <StatusTransition prev={e.previousStatusCode} next={e.newStatusCode} />
    </div>
  );
}

function GroupEvent({ node }: { node: Extract<TimelineNode, { kind: 'group' }> }) {
  const [open, setOpen] = useState(false);
  return (
    <div style={{ paddingBottom: 18 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>{node.actionLabel}</span>
        {node.batchNumber != null && <LoteBadge n={node.batchNumber} />}
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 3, fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
        <Clock size={11} /> {new Date(node.createdAtUtc).toLocaleString('pt-PT')} · {node.actorName}
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 6, fontSize: '0.78rem', color: 'var(--color-text-main)', fontWeight: 700 }}>
        <Layers size={13} style={{ color: 'var(--color-text-muted)' }} /> {node.events.length} itens enviados para aprovação
      </div>
      <button
        type="button" onClick={() => setOpen(v => !v)} aria-expanded={open}
        style={{ marginTop: 6, display: 'inline-flex', alignItems: 'center', gap: 4, background: 'none', border: '1px solid var(--color-border)', borderRadius: 6, padding: '3px 10px', cursor: 'pointer', color: 'var(--color-text-secondary, var(--color-text-main))', fontSize: '0.72rem', fontWeight: 700 }}
      >
        {open ? <ChevronDown size={13} /> : <ChevronRight size={13} />} {open ? 'Ocultar detalhes' : 'Ver detalhes'}
      </button>
      {open && (
        <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 8, paddingLeft: 10, borderLeft: '2px solid var(--color-border)' }}>
          {node.events.map(ev => (
            <div key={ev.id}>
              <CommentBlock comment={ev.comment} />
            </div>
          ))}
        </div>
      )}
      <StatusTransition prev={node.previousStatusCode} next={node.newStatusCode} />
    </div>
  );
}

export function ApprovalTimelineDrawer({ requestId, requestNumber, onClose }: Props) {
  const [events, setEvents] = useState<ApprovalTimelineEvent[] | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let alive = true;
    setEvents(null); setError(false);
    api.approvals.getTimeline(requestId)
      .then(res => { if (alive) setEvents(res); })
      .catch(() => { if (alive) setError(true); });
    return () => { alive = false; };
  }, [requestId]);

  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => { if (ev.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  // Presentation-only grouping (order preserved; originals kept inside groups).
  const nodes = useMemo<TimelineNode[]>(() => events ? groupTimelineEvents(events) : [], [events]);

  return createPortal(
    <div style={{ position: 'fixed', inset: 0, zIndex: 1200 }}>
      <div onClick={onClose} style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.4)', backdropFilter: 'blur(3px)', cursor: 'pointer' }} />
      <div
        role="dialog" aria-modal="true" aria-label={`Linha do tempo de aprovações — ${requestNumber}`}
        style={{
          position: 'absolute', top: 0, right: 0, bottom: 0, width: '100%', maxWidth: 520,
          background: 'var(--color-bg-surface)', borderLeft: '5px solid var(--color-primary)',
          boxShadow: '-8px 0 32px rgba(0,0,0,0.2)', display: 'flex', flexDirection: 'column',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 20px', borderBottom: '1px solid var(--color-border)' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <span style={{ fontSize: '0.7rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Linha do tempo de aprovações</span>
            <span style={{ fontSize: '1rem', fontWeight: 800, color: 'var(--color-text-main)' }}>{requestNumber}</span>
          </div>
          <button type="button" onClick={onClose} aria-label="Fechar" style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)', display: 'flex', padding: 6 }}>
            <X size={20} />
          </button>
        </div>

        <div style={{ flex: 1, overflowY: 'auto', padding: '18px 20px' }}>
          {events === null && !error && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, justifyContent: 'center', padding: '32px 0', color: 'var(--color-text-muted)' }}>
              <Loader2 size={16} className="spin-icon" /> Carregando…
            </div>
          )}
          {error && (
            <div style={{ padding: '24px 0', textAlign: 'center', color: 'var(--color-status-red, #dc2626)', fontWeight: 600 }}>
              Não foi possível carregar a linha do tempo.
            </div>
          )}
          {events && events.length === 0 && (
            <div style={{ padding: '24px 0', textAlign: 'center', color: 'var(--color-text-muted)' }}>Sem eventos de aprovação para este pedido.</div>
          )}
          {events && events.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 0 }}>
              {nodes.map((node, i) => {
                const decision = node.kind === 'single' && node.event.isDecision;
                return (
                  <div key={node.key} style={{ display: 'grid', gridTemplateColumns: '22px 1fr', gap: 12 }}>
                    {/* Rail */}
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                      <div style={{
                        display: 'flex', alignItems: 'center', justifyContent: 'center', width: 22, height: 22, borderRadius: 999,
                        background: decision ? 'color-mix(in srgb, ' + decisionColor(node.kind === 'single' ? node.event.decision : null) + ' 12%, var(--color-bg-page))' : 'var(--color-bg-page)',
                        border: `1px solid ${decision ? decisionColor(node.kind === 'single' ? node.event.decision : null) : 'var(--color-border)'}`,
                      }}>
                        {node.kind === 'single' ? <EventIcon e={node.event} /> : <Layers size={13} color="var(--color-text-muted)" />}
                      </div>
                      {i < nodes.length - 1 && <div style={{ flex: 1, width: 2, background: 'var(--color-border)', minHeight: 14 }} />}
                    </div>
                    {/* Event / group */}
                    {node.kind === 'single' ? <SingleEvent e={node.event} /> : <GroupEvent node={node} />}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
}

export default ApprovalTimelineDrawer;
