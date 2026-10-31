import type { DescricaoEstado } from '../../core/dominio/estado';
import type {
  CanalComunicacao,
  DiaSemana,
  ModalidadeAtendimento,
  OrigemConsulta,
  StatusConsulta,
} from './agenda.types';

/** Cada status vira uma marca fixa: anel (aberto), meio (aguarda), cheio (firme), check (concluído), riscado/x (perdido). */
export const estadosConsulta: Record<StatusConsulta, DescricaoEstado> = {
  Agendada: { rotulo: 'Agendada', tom: 'neutro', marca: 'anel' },
  AguardandoConfirmacao: { rotulo: 'Aguardando confirmação', tom: 'aviso', marca: 'meio' },
  Confirmada: { rotulo: 'Confirmada', tom: 'acento', marca: 'cheio' },
  Realizada: { rotulo: 'Realizada', tom: 'sucesso', marca: 'check' },
  Cancelada: { rotulo: 'Cancelada', tom: 'neutro', marca: 'riscado' },
  Remarcada: { rotulo: 'Remarcada', tom: 'neutro', marca: 'seta' },
  NaoCompareceu: { rotulo: 'Não compareceu', tom: 'perigo', marca: 'x' },
};

export const estadoAtivo = (ativo: boolean): DescricaoEstado =>
  ativo ? { rotulo: 'Ativo', tom: 'sucesso', marca: 'cheio' } : { rotulo: 'Inativo', tom: 'neutro', marca: 'anel' };

/** Canais que o backend aceita para lembretes automáticos (a API valida e rejeita os demais). */
/** Consultas que ainda ocupam o horário (para destaque visual e contagens de apresentação). */
export const consultaAtiva = (status: StatusConsulta) =>
  status === 'Agendada' || status === 'AguardandoConfirmacao' || status === 'Confirmada';

export const rotulosModalidade: Record<ModalidadeAtendimento, string> = {
  Presencial: 'Presencial',
  Online: 'Online',
  Hibrido: 'Híbrido',
};

export const rotulosCanal: Record<CanalComunicacao, string> = {
  Email: 'E-mail',
  WhatsApp: 'WhatsApp',
  Sms: 'SMS',
  Sistema: 'Sistema',
  Telefone: 'Telefone',
};

export const rotulosOrigem: Record<OrigemConsulta, string> = {
  Painel: 'Painel',
  WhatsApp: 'WhatsApp',
  Chatbot: 'Assistente do WhatsApp',
  Recepcionista: 'Recepção',
  Paciente: 'Paciente',
  Outro: 'Outra origem',
};

/** Ordem de `Date.getDay()`. */
export const diasSemana: DiaSemana[] = ['Domingo', 'Segunda', 'Terca', 'Quarta', 'Quinta', 'Sexta', 'Sabado'];

export const rotulosDiaSemana: Record<DiaSemana, string> = {
  Domingo: 'Domingo',
  Segunda: 'Segunda-feira',
  Terca: 'Terça-feira',
  Quarta: 'Quarta-feira',
  Quinta: 'Quinta-feira',
  Sexta: 'Sexta-feira',
  Sabado: 'Sábado',
};

export const fusosHorarios: Array<{ valor: string; rotulo: string }> = [
  { valor: 'America/Noronha', rotulo: 'Fernando de Noronha (UTC−2)' },
  { valor: 'America/Belem', rotulo: 'Belém (UTC−3)' },
  { valor: 'America/Fortaleza', rotulo: 'Fortaleza (UTC−3)' },
  { valor: 'America/Recife', rotulo: 'Recife (UTC−3)' },
  { valor: 'America/Bahia', rotulo: 'Salvador (UTC−3)' },
  { valor: 'America/Sao_Paulo', rotulo: 'Brasília e São Paulo (UTC−3)' },
  { valor: 'America/Campo_Grande', rotulo: 'Campo Grande (UTC−4)' },
  { valor: 'America/Cuiaba', rotulo: 'Cuiabá (UTC−4)' },
  { valor: 'America/Manaus', rotulo: 'Manaus (UTC−4)' },
  { valor: 'America/Rio_Branco', rotulo: 'Rio Branco (UTC−5)' },
];
