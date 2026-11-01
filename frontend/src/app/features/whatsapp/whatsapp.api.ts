import { requisicaoApi } from '../../core/api/cliente-api';
import { aplicarPaginacaoAQuery, type ResultadoPaginado } from '../../core/api/paginacao';
import { ErroApi, extrairDados, type RespostaApi } from '../../core/api/resposta-api';

export type StatusIntegracao = 'Ativa' | 'Inativa' | 'Erro' | 'Pendente';
export type StatusConversaWhatsApp =
  'Aberta' | 'Finalizada' | 'Expirada' | 'AtendimentoHumano' | 'Bloqueada';

export interface IntegracaoWhatsApp {
  id: string;
  nome: string;
  provedor: string;
  instancia?: string | null;
  urlBaseBridge: string;
  tokenMascarado: string;
  numeroWhatsApp: string;
  status: StatusIntegracao;
  ativo: boolean;
  ultimoErro: string | null;
  ultimaConexaoTestadaEm: string | null;
  criadoEm: string;
  atualizadoEm: string | null;
}

export interface SalvarIntegracaoRequest {
  nome: string;
  provedor: string;
  instancia?: string | null;
  urlBaseBridge: string;
  token?: string | null;
  numeroWhatsApp: string;
  ativo: boolean;
}

export interface TesteConexaoBridge {
  sucesso: boolean;
  status: StatusIntegracao;
  mensagem: string;
  testadoEm: string;
}

export interface EventoBridgeResumo {
  id: string;
  tipoEvento: string;
  processado: boolean;
  erroProcessamento: string | null;
  criadoEm: string;
  processadoEm: string | null;
}

export interface ConversaLista {
  id: string;
  integracaoWhatsAppId: string;
  pacienteId: string | null;
  paciente: string | null;
  telefone: string;
  nomeContato: string | null;
  status: StatusConversaWhatsApp;
  ultimaMensagemEm: string | null;
  atendimentoHumanoNecessario: boolean;
  motivoAtendimentoHumano: string | null;
  previaUltimaMensagem: string | null;
  criadoEm: string;
}

export interface MensagemWhatsApp {
  id: string;
  direcao: 'Entrada' | 'Saida';
  tipoMensagem: string;
  conteudo: string | null;
  status: string;
  enviadaEm: string | null;
  recebidaEm: string | null;
  erroEnvio: string | null;
  criadoEm: string;
}

export interface ConversaDetalhe extends Omit<ConversaLista, 'previaUltimaMensagem'> {
  mensagens: MensagemWhatsApp[];
}

export interface ListarConversasFiltro {
  atendimentoHumanoNecessario?: boolean;
  pagina?: number;
  tamanhoPagina?: number;
}

export const whatsappKeys = {
  todos: ['whatsapp'] as const,
  integracao: () => [...whatsappKeys.todos, 'integracao'] as const,
  eventos: () => [...whatsappKeys.todos, 'eventos'] as const,
  conversas: (filtro: ListarConversasFiltro) =>
    [...whatsappKeys.todos, 'conversas', filtro] as const,
  conversa: (id: string) => [...whatsappKeys.todos, 'conversa', id] as const,
};

export async function obterIntegracaoWhatsApp({ signal }: { signal?: AbortSignal } = {}) {
  const resposta = await requisicaoApi<RespostaApi<IntegracaoWhatsApp | null>>(
    '/whatsapp/integracao',
    { signal },
  );
  if (!resposta.sucesso) {
    throw new ErroApi(
      resposta.erros?.[0] ?? resposta.mensagem ?? 'Não foi possível carregar a integração.',
      422,
      resposta.erros ?? [],
    );
  }
  // A API pode retornar dados nulos quando a conta ainda nao configurou o bridge.
  return resposta.dados ?? null;
}

export async function salvarIntegracaoWhatsApp(request: SalvarIntegracaoRequest) {
  const resposta = await requisicaoApi<RespostaApi<IntegracaoWhatsApp>>('/whatsapp/integracao', {
    method: 'PUT',
    body: request,
  });
  return extrairDados(resposta, 'Não foi possível salvar a integração.');
}

export async function testarConexaoWhatsApp() {
  const resposta = await requisicaoApi<RespostaApi<TesteConexaoBridge>>(
    '/whatsapp/integracao/testar-conexao',
    {
      method: 'POST',
    },
  );
  return extrairDados(resposta, 'Não foi possível testar a conexão.');
}

export async function listarEventosBridge({ signal }: { signal?: AbortSignal } = {}) {
  const resposta = await requisicaoApi<RespostaApi<EventoBridgeResumo[]>>(
    '/whatsapp/integracao/eventos',
    { signal },
  );
  return extrairDados(resposta, 'Não foi possível carregar os eventos.');
}

export async function listarConversasWhatsApp(
  filtro: ListarConversasFiltro = {},
  { signal }: { signal?: AbortSignal } = {},
) {
  const params = new URLSearchParams();
  aplicarPaginacaoAQuery(params, filtro);
  if (filtro.atendimentoHumanoNecessario !== undefined) {
    params.set('atendimentoHumanoNecessario', String(filtro.atendimentoHumanoNecessario));
  }
  const resposta = await requisicaoApi<RespostaApi<ResultadoPaginado<ConversaLista>>>(
    `/whatsapp/conversas?${params}`,
    { signal },
  );
  return extrairDados(resposta, 'Não foi possível carregar as conversas.');
}

export async function obterConversaWhatsApp(id: string, { signal }: { signal?: AbortSignal } = {}) {
  const resposta = await requisicaoApi<RespostaApi<ConversaDetalhe>>(`/whatsapp/conversas/${id}`, {
    signal,
  });
  return extrairDados(resposta, 'Não foi possível carregar a conversa.');
}

export async function assumirAtendimentoHumano(id: string, motivo?: string) {
  const resposta = await requisicaoApi<RespostaApi<ConversaDetalhe>>(
    `/whatsapp/conversas/${id}/assumir-humano`,
    {
      method: 'POST',
      body: { motivo: motivo ?? null },
    },
  );
  return extrairDados(resposta, 'Não foi possível assumir o atendimento.');
}

export async function devolverConversaAoAssistente(id: string) {
  const resposta = await requisicaoApi<RespostaApi<ConversaDetalhe>>(
    `/whatsapp/conversas/${id}/devolver-ao-assistente`,
    {
      method: 'POST',
    },
  );
  return extrairDados(resposta, 'Não foi possível devolver a conversa ao assistente.');
}

export async function finalizarConversa(id: string) {
  const resposta = await requisicaoApi<RespostaApi<ConversaDetalhe>>(
    `/whatsapp/conversas/${id}/finalizar`,
    {
      method: 'POST',
    },
  );
  return extrairDados(resposta, 'Não foi possível finalizar a conversa.');
}

export async function enviarMensagemConversa(id: string, conteudo: string) {
  const resposta = await requisicaoApi<RespostaApi<MensagemWhatsApp>>(
    `/whatsapp/conversas/${id}/mensagens`,
    {
      method: 'POST',
      body: { conteudo },
    },
  );
  return extrairDados(resposta, 'Não foi possível enviar a mensagem.');
}

export async function vincularPacienteConversa(id: string, pacienteId: string) {
  const resposta = await requisicaoApi<RespostaApi<ConversaDetalhe>>(
    `/whatsapp/conversas/${id}/vincular-paciente`,
    {
      method: 'POST',
      body: { pacienteId },
    },
  );
  return extrairDados(resposta, 'Não foi possível vincular o paciente.');
}
