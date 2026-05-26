import type { TourStep } from '../guidedTourTypes';

/**
 * Portal Main Tour Steps
 * 
 * Each step targets a `[data-tour="..."]` attribute placed on the corresponding
 * UI element. Steps whose target is missing from the DOM at tour start time are
 * automatically filtered out — this handles permission-based menu variations.
 * 
 * Content is in Portuguese (pt-PT) matching the portal's primary language.
 */
export const PORTAL_MAIN_STEPS: TourStep[] = [
    {
        target: '[data-tour="topbar"]',
        title: 'Barra Superior',
        content: 'Aqui você encontra os principais atalhos do Portal, incluindo busca, notificações e acesso ao seu perfil.',
        placement: 'bottom',
        skipBeacon: true,
    },
    {
        target: '[data-tour="module-search"]',
        title: 'Pesquisa de Módulos',
        content: 'Use a busca (Ctrl + K) para encontrar rapidamente módulos e áreas disponíveis para o seu perfil.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="notifications"]',
        title: 'Notificações',
        content: 'Aqui aparecem alertas importantes, como pedidos pendentes, aprovações, pagamentos e documentos que exigem atenção.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="user-profile"]',
        title: 'Perfil & Preferências',
        content: 'Nesta área você pode acessar seu perfil, alterar tema visual, palavra-passe e terminar sessão.',
        placement: 'bottom-end',
    },
    {
        target: '[data-tour="guided-help-button"]',
        title: 'Menu de Ajuda',
        content: 'Aqui você pode iniciar tours guiados para conhecer o Portal, o módulo atual ou a tela em que se encontra.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="main-menu"]',
        title: 'Menu Principal',
        content: 'O menu principal mostra os módulos disponíveis conforme as permissões do seu usuário. Pode ser recolhido para maximizar a área de trabalho.',
        placement: 'right',
    },
    {
        target: '[data-tour="dashboard"]',
        title: 'Dashboard',
        content: 'O dashboard reúne indicadores, atalhos e informações importantes para o acompanhamento geral.',
        placement: 'right',
    },
    {
        target: '[data-tour="purchase-requests-menu"]',
        title: 'Pedidos de Compra',
        content: 'Nesta área você cria e acompanha pedidos de compra, cotações, pagamentos e documentos relacionados.',
        placement: 'right',
    },
    {
        target: '[data-tour="approvals"]',
        title: 'Centro de Aprovações',
        content: 'Aqui ficam os pedidos aguardando aprovação de área ou aprovação final, conforme o seu papel no workflow.',
        placement: 'right',
    },
    {
        target: '[data-tour="purchasing-logistics"]',
        title: 'Compras & Logística',
        content: 'Esta área apoia o processo de cotações, ordens de compra, fornecedores, recebimentos e acompanhamento operacional.',
        placement: 'right',
    },
    {
        target: '[data-tour="finance"]',
        title: 'Finanças',
        content: 'Aqui ficam os processos financeiros, como pagamentos, comprovativos, agendamentos e análise de valores.',
        placement: 'right',
    },
    {
        target: '[data-tour="contracts"]',
        title: 'Contratos',
        content: 'Esta área concentra a gestão contratual: contratos ativos, alertas de vencimento, renovações e obrigações junto a fornecedores.',
        placement: 'right',
    },
    {
        target: '[data-tour="it-module"]',
        title: 'T.I.',
        content: 'Esta área reúne ferramentas relacionadas à gestão de tecnologia, equipamentos, suporte interno, controle de ativos e recursos técnicos do Portal.',
        placement: 'right',
    },
    {
        target: '[data-tour="hr"]',
        title: 'Recursos Humanos',
        content: 'Esta área reúne funcionalidades de RH: funcionários, assiduidade, férias, relatórios e processos internos.',
        placement: 'right',
    },
    {
        target: '[data-tour="configuration-module"]',
        title: 'Configurações',
        content: 'Esta área concentra parâmetros do sistema, regras de funcionamento, configurações operacionais e ajustes que controlam como o Portal se comporta.',
        placement: 'right',
    },
    {
        target: '[data-tour="administration-module"]',
        title: 'Administração',
        content: 'Esta área é usada para gestão administrativa do Portal, incluindo cadastros, permissões, usuários, diagnósticos e informações estruturais do sistema.',
        placement: 'right',
    },
];

/**
 * Filter steps to only include those whose target exists in the DOM.
 * This safely handles permission-based visibility (RBAC) where some
 * menu items / modules may not be rendered for certain users.
 * 
 * Returns an empty array if no valid steps are found.
 */
export function filterActiveSteps(steps: TourStep[]): TourStep[] {
    return steps.filter((step) => {
        if (typeof step.target !== 'string') {
            return true;
        }
        return Boolean(document.querySelector(step.target));
    });
}
