/**
 * Shared source of truth for role keys.
 * Used for consistency across permission checks and UI logic.
 */
export const ROLES = {
    SYSTEM_ADMINISTRATOR: 'System Administrator',
    LOCAL_MANAGER: 'Local Manager',
    AREA_APPROVER: 'Area Approver',
    FINAL_APPROVER: 'Final Approver',
    REQUESTER: 'Requester',
    BUYER: 'Buyer',
    FINANCE: 'Finance',
    RECEIVING: 'Receiving',
    IMPORT: 'Import',
    CONTRACTS: 'Contracts',
    VIEWER_MANAGEMENT: 'Viewer / Management',
    HR: 'HR',
    IT: 'IT',
    OPERATIONS: 'OPERATIONS'
};

/**
 * Centralized mapping for UI display names.
 * Used when the backend technical name should be formatted differently for users.
 */
export const ROLE_DISPLAY_NAMES: Record<string, string> = {
    [ROLES.OPERATIONS]: 'Operações'
};

/**
 * Centralized mapping for user role descriptions.
 * Used to provide contextual help tooltips in the administration modules.
 */
export const ROLE_DESCRIPTIONS: Record<string, string> = {
    [ROLES.AREA_APPROVER]: 'Aprova pedidos da sua área antes da aprovação final. Role derivada: concedida automaticamente aos managers cadastrados em Dados Mestres → Departamentos.',
    [ROLES.BUYER]: 'Gere cotações, fornecedores e o andamento do processo de compra.',
    [ROLES.CONTRACTS]: 'Acompanha e gere contratos relacionados ao processo de compras.',
    [ROLES.FINAL_APPROVER]: 'Realiza a aprovação final dos pedidos conforme as regras do sistema.',
    [ROLES.FINANCE]: 'Atua nas etapas financeiras, como pagamento, agendamento e confirmação.',
    [ROLES.IMPORT]: 'Acompanha processos e documentos ligados à importação.',
    [ROLES.LOCAL_MANAGER]: 'Gere utilizadores dentro do seu escopo permitido de planta e departamento.',
    [ROLES.RECEIVING]: 'Atua no recebimento físico e validação de entregas.',
    [ROLES.REQUESTER]: 'Cria e acompanha os próprios pedidos.',
    [ROLES.SYSTEM_ADMINISTRATOR]: 'Possui permissões administrativas amplas no sistema.',
    [ROLES.VIEWER_MANAGEMENT]: 'Pode consultar informações conforme o escopo permitido, sem atuar diretamente no processo.',
    [ROLES.HR]: 'Acede às funcionalidades de Recursos Humanos (cadastro, crachás) dentro do escopo de planta e departamento atribuído.',
    [ROLES.IT]: 'Gere o inventário de equipamentos de T.I, incluindo atribuições, devoluções, manutenções e histórico de movimentações.',
    [ROLES.OPERATIONS]: 'Acesso ao módulo de Operações e transferências logísticas'
};
