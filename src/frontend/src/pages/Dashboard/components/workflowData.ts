export interface WorkflowStage {
    id: string;
    label: string;
    role: string;
    responsible: string;
    goal: string;
    actions: string[];
    documents: string[];
    nextStage: string;
    adjustmentPath?: {
        responsible: string;
        goal: string;
        rule: string;
    };
}

export const WORKFLOW_STAGES: WorkflowStage[] = [
    {
        id: 'rascunho',
        label: 'Rascunho',
        role: 'Solicitante',
        responsible: 'Solicitante',
        goal: 'Criar o pedido e preencher os dados iniciais',
        actions: ['Informar os dados gerais do pedido conforme o tipo do processo'],
        documents: ['Conforme a natureza da solicitação'],
        nextStage: 'Cotação'
    },
    {
        id: 'cotacao',
        label: 'Cotação',
        role: 'Comprador',
        responsible: 'Comprador',
        goal: 'Complementar as informações comerciais e documentais do pedido',
        actions: [
            'Adicionar/editar itens',
            'Inserir valores',
            'Preencher fornecedor quando aplicável',
            'Gerir proforma/documentos conforme a necessidade'
        ],
        documents: [
            'Proforma',
            'Outros documentos de suporte'
        ],
        nextStage: 'Aprovação de Área',
        adjustmentPath: {
            responsible: 'Aprovador de Área ou Aprovação Final',
            goal: 'Pedir correção do pedido antes da continuidade',
            rule: 'A justificativa do reajuste é obrigatória'
        }
    },
    {
        id: 'aprovacao_area',
        label: 'Aprovação de Área',
        role: 'Aprovador Área',
        responsible: 'Aprovador de Área',
        goal: 'Validar a necessidade e a consistência do pedido',
        actions: [
            'Analisar dados do pedido',
            'Validar documentos',
            'Aprovar, rejeitar ou solicitar reajuste'
        ],
        documents: ['Proforma e dados do pedido'],
        nextStage: 'Aprovação Final',
        adjustmentPath: {
            responsible: 'Aprovador de Área',
            goal: 'Solicitar correções ao comprador',
            rule: 'Gera o status "Reajuste A.A"'
        }
    },
    {
        id: 'aprovacao_final',
        label: 'Aprovação Final',
        role: 'Aprovador Final',
        responsible: 'Aprovador Final',
        goal: 'Realizar a validação final do processo',
        actions: [
            'Revisar o pedido',
            'Confirmar orçamento/aderência',
            'Aprovar, rejeitar ou solicitar reajuste'
        ],
        documents: ['Documentação consolidada do pedido'],
        nextStage: 'Execução',
        adjustmentPath: {
            responsible: 'Aprovador Final',
            goal: 'Solicitar correções ao comprador',
            rule: 'Gera o status "Reajuste A.F"'
        }
    },
    {
        id: 'execucao',
        label: 'Execução',
        role: 'Processo',
        responsible: 'Fluxo operacional seguinte / áreas envolvidas',
        goal: 'Dar continuidade ao processo aprovado',
        actions: [
            'Seguir com emissão / pagamento / continuidade operacional conforme o tipo'
        ],
        documents: [
            'P.O',
            'Comprovativos',
            'Outros documentos da etapa seguinte'
        ],
        nextStage: 'Conclusão do fluxo operacional'
    }
];
