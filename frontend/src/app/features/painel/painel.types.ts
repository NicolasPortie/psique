export interface PainelResumo {
  usuario: { nomeCompleto: string; primeiroNome: string; perfil: string };
  indicadores: {
    consultasHoje: number;
    consultasRestantesHoje: number;
    consultasAguardandoConfirmacao: number;
    pacientesAtivos: number;
    pagamentosPendentes: number;
    lembretesFalhosHoje: number;
    conversasWhatsappAbertas: number;
    conversasWhatsappAtendimentoHumano: number;
    notificacoesNaoLidas: number;
    progressoMensalPercentual: number;
  };
  consultasHoje: Array<{
    id: string;
    paciente: string;
    horarioInicio: string;
    duracaoMinutos: number;
    tipoAtendimento: string;
    modalidade: string;
    status: string;
    podeRegistrarSessao: boolean;
  }>;
  visaoSemana: Array<{ data: string; rotulo: string; consultas: number }>;
  prioridades: Array<{ titulo: string; mensagem: string; tom: 'alerta' | 'erro' | 'atencao' | 'neutro' | 'ok'; rota: string; acao: string }>;
  /** As próximas consultas em aberto, mesmo de outros dias (para "próxima sessão" no fim de semana). */
  proximasConsultas?: PainelConsulta[] | null;
  /** Consultas que já terminaram sem desfecho (nem realizada, nem falta, nem cancelada), dos últimos 60 dias. */
  consultasSemDesfecho?: PainelConsulta[] | null;
  aniversariantes?: PainelAniversariante[] | null;
  /** Só para quem gere a conta. */
  primeirosPassos?: PainelPrimeirosPassos | null;
}

export interface PainelAniversariante {
  pacienteId: string;
  paciente: string;
  /** Data do aniversário (AAAA-MM-DD) dentro dos próximos 7 dias. */
  data: string;
  idade: number;
}

export interface PainelPrimeirosPassos {
  dadosProfissionais: boolean;
  horariosAtendimento: boolean;
  tiposAtendimento: boolean;
  primeiroPaciente: boolean;
  whatsApp: boolean;
}

export interface PainelConsulta {
  id: string;
  pacienteId: string;
  paciente: string;
  inicioEm: string;
  fimEm: string;
  tipoAtendimento: string;
  modalidade: string;
  status: string;
  linkOnline: string | null;
}
