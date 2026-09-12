import type { CSSProperties } from 'react';
import { ListChecks, History, BarChart3 } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { GuidedTourContextButton } from '../../features/guided-tour/GuidedTourContextButton';
import { useApprovalTab, type ApprovalTab } from './hooks/useApprovalTab';
import { PendentesTab } from './tabs/PendentesTab';
import { HistoricoTab } from './tabs/HistoricoTab';
import { AnalisesTab } from './tabs/AnalisesTab';

// v2.244.0 Approval Center V2 — shell.
// /approvals is now a three-tab workspace: PENDENTES (the live approval queue, re-homed unchanged),
// HISTÓRICO (Phase 2) and ANÁLISES (Phase 3). The active tab is URL-synchronized via useApprovalTab
// (?tab=…), so it is refresh-, Back/Forward- and same-route-navigation safe, with an unknown value
// falling back to PENDENTES. This shell owns only the page chrome + tab bar; each tab owns its own
// data and behavior. No approval business logic lives here.

interface TabDef {
    id: ApprovalTab;
    label: string;
    icon: typeof ListChecks;
}

const TABS: TabDef[] = [
    { id: 'PENDENTES', label: 'Pendentes', icon: ListChecks },
    { id: 'HISTORICO', label: 'Histórico', icon: History },
    { id: 'ANALISES', label: 'Análises', icon: BarChart3 },
];

const tabButtonBase: CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: 8, padding: '10px 18px',
    border: 'none', borderBottom: '3px solid transparent', background: 'none',
    cursor: 'pointer', fontSize: '0.9rem', fontWeight: 800, letterSpacing: '0.02em',
    color: 'var(--color-text-muted)', whiteSpace: 'nowrap', marginBottom: -1,
};

export function ApprovalCenter() {
    const [activeTab, setActiveTab] = useApprovalTab();

    return (
        <PageContainer>
            <PageHeader
                data-tour="approvals-header"
                title="Centro de Aprovações"
                subtitle="Workspace centralizado para decisões e aprovações de Procurement."
                actions={
                    <GuidedTourContextButton tourId="page-approvals-center" label="Tour da Tela" />
                }
            />

            {/* Tab bar — accessible tablist driving the URL-synced active tab */}
            <div
                role="tablist"
                aria-label="Secções do Centro de Aprovações"
                // overflowY must be pinned to 'hidden': with only overflowX:'auto' the browser computes
                // the visible cross-axis to 'auto' too, and the tabs' borderBottom + marginBottom:-1
                // spill a hair vertically → a spurious vertical scrollbar. Horizontal scroll is kept
                // for genuinely narrow viewports.
                style={{ display: 'flex', gap: 4, borderBottom: '1px solid var(--color-border)', overflowX: 'auto', overflowY: 'hidden' }}
            >
                {TABS.map(({ id, label, icon: Icon }) => {
                    const active = activeTab === id;
                    return (
                        <button
                            key={id}
                            type="button"
                            role="tab"
                            id={`approval-tab-${id}`}
                            aria-selected={active}
                            aria-controls={`approval-panel-${id}`}
                            onClick={() => setActiveTab(id)}
                            style={{
                                ...tabButtonBase,
                                ...(active ? { color: 'var(--color-primary)', borderBottomColor: 'var(--color-primary)' } : {}),
                            }}
                        >
                            <Icon size={16} /> {label}
                        </button>
                    );
                })}
            </div>

            {/* Active panel — only the selected tab is mounted. */}
            <div
                role="tabpanel"
                id={`approval-panel-${activeTab}`}
                aria-labelledby={`approval-tab-${activeTab}`}
                style={{ display: 'flex', flexDirection: 'column', gap: 16 }}
            >
                {activeTab === 'PENDENTES' && <PendentesTab />}
                {activeTab === 'HISTORICO' && <HistoricoTab />}
                {activeTab === 'ANALISES' && <AnalisesTab />}
            </div>
        </PageContainer>
    );
}

export default ApprovalCenter;
