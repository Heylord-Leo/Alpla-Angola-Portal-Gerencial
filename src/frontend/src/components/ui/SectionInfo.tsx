import { ReactNode } from 'react';
import { Info } from 'lucide-react';
import { ModernTooltip } from './ModernTooltip';

// Reusable analytical "i" affordance beside a Dashboard section title. It explains not just WHAT the
// section is, but how to read it and what to conclude. Built on the shared ModernTooltip primitive
// (hover + click/tap + keyboard focus + Enter/Space + ariaLabel + ESC/outside-click) so there is ONE
// popover implementation across the Dashboard — never a browser title-only tooltip. Dark-mode via
// defined tokens only. Absent fields render nothing (no empty headings).

export interface SectionInfoContent {
  title: string;
  /** "O que mede" — what the section measures. */
  measures?: string;
  /** "Como interpretar" — how to read the numbers. */
  interpretation?: string;
  /** "O que observar" — what to look for / what to conclude. */
  observe?: string;
  /** "Para que serve" — the practical utility. */
  utility?: string;
  /** "Exemplo" — a short concrete example. */
  example?: string;
  /** "Observação" — a caveat. */
  caveat?: string;
  /** Marks the explanation as temporary until a later slice replaces the section. */
  temporary?: boolean;
}

interface SectionInfoProps extends SectionInfoContent {
  maxWidth?: number;
}

function Block({ label, text }: { label: string; text?: string }): ReactNode {
  if (!text) return null;
  return (
    <div style={{ marginTop: 8 }}>
      <div style={{ fontSize: '0.66rem', fontWeight: 700, letterSpacing: '0.03em', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>{label}</div>
      <div style={{ marginTop: 2, color: 'var(--color-text-main)' }}>{text}</div>
    </div>
  );
}

export function SectionInfo({ title, measures, interpretation, observe, utility, example, caveat, temporary, maxWidth = 340 }: SectionInfoProps) {
  const content = (
    <div style={{ fontSize: '0.78rem', lineHeight: 1.45, color: 'var(--color-text-main)' }}>
      <div style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>{title}</div>
      <Block label="O que mede" text={measures} />
      <Block label="Como interpretar" text={interpretation} />
      <Block label="O que observar" text={observe} />
      <Block label="Para que serve" text={utility} />
      <Block label="Exemplo" text={example} />
      <Block label="Observação" text={caveat} />
      {temporary && (
        <div style={{
          marginTop: 10, fontSize: '0.64rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
          color: '#a15c1e',
        }}>Seção temporária — substituída em fase futura</div>
      )}
    </div>
  );

  return (
    <ModernTooltip side="top" align="start" openOnClick maxWidth={maxWidth} ariaLabel={`Ajuda: ${title}`} content={content}>
      <span style={{ display: 'inline-flex', cursor: 'help', color: 'var(--color-text-muted)' }}>
        <Info size={14} aria-hidden />
      </span>
    </ModernTooltip>
  );
}
