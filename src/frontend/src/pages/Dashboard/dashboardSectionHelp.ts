import type { SectionInfoContent } from '../../components/ui/SectionInfo';

// Centralized Dashboard section help copy (PT-AO/PT business Portuguese). One definition per section so
// wording stays consistent and legacy/temporary descriptions can be swapped safely in B7/B8/B9. Business
// language only — no backend terminology (RequestPoGroup, ActionClass, projections). Help is header-level.

export const DASHBOARD_SECTION_HELP: Record<string, SectionInfoContent> = {
  personal: {
    title: 'Sobre Minha Operação',
    measures: 'A quantidade de ações atualmente atribuídas diretamente a si.',
    interpretation: 'Mostra apenas responsabilidades pessoais. As filas compartilhadas de Compras, Finanças, Recebimento e a Aprovação Final não aumentam este número.',
    observe: 'Se o número estiver elevado, há várias ações que dependem diretamente de si para o processo continuar.',
    utility: 'Priorizar o seu trabalho individual.',
    example: 'Se tiver 1 pedido de Compras atribuído a si aguardando envio para aprovação, Minha Operação mostra 1 ação.',
  },
  buyerPersonal: {
    title: 'Sobre Minha Operação — Compras',
    measures: 'O trabalho de Compras explicitamente atribuído a si como comprador.',
    interpretation: 'Atribuídos são os pedidos sob a sua responsabilidade; acionáveis são aqueles em que existe uma ação disponível agora. Itens pendentes e prontos descrevem a cobertura de cotação.',
    observe: 'A diferença entre atribuídos e acionáveis, quantos itens ainda estão sem cobertura e quantos pedidos estão prontos para avançar.',
    utility: 'Organizar a sua carga como comprador e identificar o próximo trabalho.',
    example: 'Um pedido pode estar atribuído a si mas ainda não estar acionável, dependendo da situação operacional.',
  },
  buyerShared: {
    title: 'Sobre a Fila Compartilhada de Compras',
    measures: 'Os pedidos de Compras disponíveis no pool compartilhado, ainda sem comprador atribuído.',
    interpretation: 'Não são tarefas pessoais de nenhum comprador até que ocorra a atribuição.',
    observe: 'O volume ainda sem comprador e quantos já estão acionáveis.',
    utility: 'Ajudar a equipa a distribuir novos pedidos e evitar acumular trabalho não atribuído.',
    example: '8 pedidos "sem comprador" significa 8 pedidos disponíveis para distribuição — não 8 tarefas suas.',
  },
  buyerWorkload: {
    title: 'Sobre a Carga da Equipe de Compras',
    measures: 'A distribuição do trabalho entre os compradores dentro do seu escopo.',
    interpretation: 'Compara atribuídos, acionáveis, itens pendentes, itens prontos e atenção por comprador.',
    observe: 'Desequilíbrio de carga entre compradores e o volume ainda não atribuído.',
    utility: 'Apoiar a distribuição de trabalho e a gestão da equipa.',
    example: 'Se um comprador tem muitos pedidos acionáveis e outro poucos, o gestor pode avaliar uma redistribuição.',
    caveat: 'Isto não é um ranking de produtividade.',
  },
  finance: {
    title: 'Sobre a Fila Compartilhada — Finanças',
    measures: 'Os grupos de P.O. com obrigações financeiras disponíveis para a equipa.',
    interpretation: '"Para agendar" indica pagamentos a serem agendados; "a confirmar" indica pagamentos já agendados; "pago" indica pagamentos concluídos, já em transição para o Recebimento.',
    observe: 'O volume acionável e onde se concentra o trabalho financeiro.',
    utility: 'Planear e priorizar a operação financeira do dia.',
    example: 'Se houver muitos grupos a agendar, a equipa trata primeiro desse grupo antes de confirmar pagamentos.',
    caveat: 'Não é uma visão pessoal e não apresenta valores monetários.',
  },
  receiving: {
    title: 'Sobre a Fila Compartilhada — Recebimento',
    measures: 'Os grupos de P.O. atualmente numa etapa operacional de Recebimento.',
    interpretation: 'Entrada = pagamento concluído e o recebimento pode começar; aguardando recebimento = recebimento em aberto; acompanhamento parcial = recebimento parcial; aguardando fornecedor = passo pendente do fornecedor.',
    observe: 'Onde se concentra o trabalho de recebimento.',
    utility: 'Coordenar entregas e acompanhamento após o pagamento.',
    example: '47 em "Entrada em recebimento" significa 47 grupos prontos para o processo de Recebimento começar.',
    caveat: 'Não indica prazos nem atraso.',
  },
  gerencial: {
    title: 'Sobre a Visão Gerencial',
    measures: 'Indicadores analíticos consolidados dentro do seu escopo permitido.',
    interpretation: 'Descrevem o processo e a equipa, não o trabalho pessoalmente atribuído a si.',
    observe: 'Concentração, distribuição e posição do trabalho no processo.',
    utility: 'Apoiar decisões de gestão e a investigação do processo.',
    example: 'Um gestor pode comparar a concentração do Pipeline com a Carga da Equipe antes de decidir onde investigar.',
  },
  pipeline: {
    title: 'Sobre a Visão do Pipeline',
    measures: 'A distribuição do trabalho pelas etapas operacionais do processo.',
    interpretation: 'Cada etapa usa a sua unidade própria — pedidos, lotes de aprovação ou grupos de P.O. Um mesmo pedido pode aparecer em várias etapas.',
    observe: 'Etapas com grande concentração de entidades e acumulações inesperadas.',
    utility: 'Entender onde o trabalho está no processo de ponta a ponta.',
    example: 'Um pedido pode ter um grupo aguardando P.O., outro com pagamento agendado e outro em recebimento: conta uma vez em Pedidos Ativos, mas aparece nas três etapas.',
    caveat: 'A soma das etapas pode ser maior que o total de pedidos ativos. Um número alto não é, por si só, um gargalo.',
  },
  // 'bottlenecks' (legacy BottleneckTable help) removed in B9.6 — replaced by the canonical
  // 'stageAging' entry above when the legacy Gargalos/cockpit path was retired.
  financialSummary: {
    title: 'Sobre o Resumo Financeiro',
    measures: 'A exposição financeira atual por etapa e o histórico recente de pagamentos confirmados, ambos separados por moeda.',
    interpretation: 'Exposição atual = valor em etapas ativas do processo (inclui IVA quando aplicável). Histórico de pagamentos = pagamentos concluídos dentro do período. Valores em moedas diferentes nunca são somados entre si.',
    observe: 'Compare a exposição atual com os pagamentos recentes confirmados, sempre por moeda.',
    utility: 'Apoiar a leitura da carga financeira atual em relação à evidência recente de saídas de caixa.',
    example: 'AOA 18M em processamento financeiro e AOA 35M pagos nos últimos 30 dias são medidas diferentes e não devem ser somadas automaticamente.',
    caveat: 'Não há conversão cambial; reembolsos não são deduzidos; o histórico usa a data de pagamento; os valores são evidência de pagamento, não conciliação contábil.',
  },
  alerts: {
    title: 'Sobre Atenção Necessária',
    measures: 'Condições de risco ou prazo sobre entidades canônicas com ação em aberto.',
    interpretation: 'Alertas não representam toda a fila de trabalho. Eles destacam exceções e prazos que exigem atenção. Compras usa a data de necessidade; Finanças usa a data de pagamento agendada. O Dashboard apresenta apenas uma prévia dos alertas mais prioritários; a lista completa retornada pode ser consultada em "Ver todos os alertas".',
    observe: 'Quantidade de alertas críticos, itens em atenção e há quanto tempo uma condição está vencida.',
    utility: 'Priorizar riscos e prazos importantes sem repetir todas as filas operacionais.',
    example: 'Um pagamento agendado cuja data já passou e continua em aberto aparece como alerta crítico. Um pedido já pago não gera esse alerta.',
    caveat: 'O envelhecimento de Aprovação, P.O. e Recebimento ainda não é medido aqui. Esses indicadores dependem do B9. A data de criação do pedido não é usada como idade da etapa.',
  },
  stageAging: {
    title: 'Sobre os Gargalos do Processo',
    measures: 'Quanto tempo as entidades estão na etapa operacional atual.',
    interpretation: 'A idade representa o tempo na etapa atual, e não a idade do pedido.',
    observe: 'Etapas com entidades críticas, em atenção ou com permanência elevada.',
    utility: 'Identificar onde o processo está acumulando trabalho e por quanto tempo.',
    example: 'Um grupo pode pertencer a um pedido criado há 60 dias, mas se entrou em recebimento ontem sua idade nesta etapa é 1 dia.',
    caveat: 'Os limites são orientação operacional, não SLA formal. Alguns registros anteriores à implantação da medição podem exibir idade não disponível.',
  },
  quickActions: {
    title: 'Sobre as Ações Rápidas',
    interpretation: 'Mostra atalhos disponíveis conforme as permissões do utilizador. Não é um indicador — é navegação.',
    utility: 'Chegar rapidamente aos módulos operacionais usados com frequência.',
    example: 'Um utilizador de Finanças pode ver Pagamentos, enquanto um de Recebimento pode ver Recebimentos.',
  },
};
