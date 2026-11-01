import { useSyncExternalStore } from 'react';

import { configurarAutenticacaoApi, requisicaoApi, requisicaoPublica } from '../api/cliente-api';
import { extrairDados, type RespostaApi } from '../api/resposta-api';
import type { AutenticacaoResponse, CriarContaRequest, EstadoSessao, LoginRequest } from './autenticacao.types';

let estado: EstadoSessao = { status: 'verificando', usuario: null, accessToken: null };
const ouvintes = new Set<() => void>();
let renovacaoEmAndamento: Promise<boolean> | null = null;
let versaoSessao = 0;
let contextoAcesso = 0;
let acessoInvalidado = false;
const canalSessao = typeof window !== 'undefined' && typeof window.BroadcastChannel === 'function'
  ? new window.BroadcastChannel('psique:sessao')
  : null;

if (canalSessao) canalSessao.onmessage = (evento: MessageEvent<unknown>) => {
  if (typeof evento.data !== 'object' || evento.data === null || !('tipo' in evento.data) || evento.data.tipo !== 'acesso-alterado') return;
  versaoSessao++;
  contextoAcesso++;
  acessoInvalidado = true;
  publicar({ status: 'anonimo', usuario: null, accessToken: null });
};

function publicar(novoEstado: EstadoSessao) {
  estado = novoEstado;
  ouvintes.forEach((ouvinte) => ouvinte());
}

function iniciarTrocaDeAcesso() {
  acessoInvalidado = false;
  versaoSessao++;
  canalSessao?.postMessage({ tipo: 'acesso-alterado' });
  return ++contextoAcesso;
}

function aplicarAutenticacao(resposta: AutenticacaoResponse, contextoEsperado = contextoAcesso) {
  if (contextoEsperado !== contextoAcesso) throw new Error('Esta solicitação de acesso foi substituída. Entre novamente.');
  versaoSessao++;
  publicar({ status: 'autenticado', usuario: resposta.usuario, accessToken: resposta.accessToken });
}

export function obterEstadoSessao() {
  return estado;
}

/** Atualiza apenas o nome exibido, depois de salvar os dados profissionais na API. */
export function atualizarNomeProfissionalNaSessao(nomeCompleto: string) {
  if (estado.status === 'autenticado' && estado.usuario.perfil === 'Psicologo') {
    publicar({ ...estado, usuario: { ...estado.usuario, nomeCompleto } });
  }
}

export function observarSessao(ouvinte: () => void) {
  ouvintes.add(ouvinte);
  return () => ouvintes.delete(ouvinte);
}

export function useSessao() {
  return useSyncExternalStore(observarSessao, obterEstadoSessao, obterEstadoSessao);
}

export async function entrar(request: LoginRequest) {
  const contexto = iniciarTrocaDeAcesso();
  const envelope = await requisicaoPublica<RespostaApi<AutenticacaoResponse>>('/autenticacao/login', {
    method: 'POST',
    body: request,
  });
  const autenticacao = extrairDados(envelope, 'Não foi possível entrar.');
  aplicarAutenticacao(autenticacao, contexto);
  return autenticacao;
}

export async function criarConta(request: CriarContaRequest) {
  const contexto = iniciarTrocaDeAcesso();
  const envelope = await requisicaoPublica<RespostaApi<AutenticacaoResponse>>('/autenticacao/cadastro', {
    method: 'POST',
    body: request,
  });
  const autenticacao = extrairDados(envelope, 'Não foi possível criar a conta.');
  aplicarAutenticacao(autenticacao, contexto);
  return autenticacao;
}

export interface CodigoCadastro {
  identificador: string;
  expiraEm: string;
  reenviarAposSegundos: number;
}

export async function solicitarCodigoCadastro(email: string, identificadorAnterior?: string) {
  const resposta = await requisicaoPublica<RespostaApi<CodigoCadastro>>('/autenticacao/cadastro/codigo', {
    method: 'POST',
    body: { email, identificadorAnterior },
  });
  return extrairDados(resposta, 'Não foi possível enviar o código.');
}

export async function confirmarEmailCadastro(email: string, identificador: string, codigo: string) {
  const resposta = await requisicaoPublica<RespostaApi<{ tokenEmail: string; expiraEm: string }>>(
    '/autenticacao/cadastro/confirmar-email',
    {
      method: 'POST',
      body: { email, identificador, codigo },
    },
  );
  return extrairDados(resposta, 'Não foi possível confirmar o e-mail.');
}

/** Troca a senha da sessão ativa. O servidor encerra as outras sessões e devolve uma nova para esta. */
export async function alterarSenha(senhaAtual: string, novaSenha: string) {
  const contexto = iniciarTrocaDeAcesso();
  const envelope = await requisicaoApi<RespostaApi<AutenticacaoResponse>>('/autenticacao/senha/alterar', {
    method: 'POST',
    body: { senhaAtual, novaSenha },
  });
  aplicarAutenticacao(extrairDados(envelope, 'Não foi possível alterar a senha.'), contexto);
}

export async function renovarSessao(): Promise<boolean> {
  if (acessoInvalidado) return false;
  if (renovacaoEmAndamento) return renovacaoEmAndamento;
  const versaoAoIniciar = versaoSessao;
  const usuarioAoIniciar = estado.usuario?.id;

  renovacaoEmAndamento = (async () => {
    try {
      const envelope = await requisicaoPublica<RespostaApi<AutenticacaoResponse>>('/autenticacao/token/renovar', {
        method: 'POST',
        body: {},
      });
      if (versaoAoIniciar !== versaoSessao) return false;
      const autenticacao = extrairDados(envelope, 'Sessão expirada.');
      if (usuarioAoIniciar && autenticacao.usuario.id !== usuarioAoIniciar) {
        acessoInvalidado = true;
        versaoSessao++;
        contextoAcesso++;
        publicar({ status: 'anonimo', usuario: null, accessToken: null });
        return false;
      }
      aplicarAutenticacao(autenticacao);
      return true;
    } catch {
      if (versaoAoIniciar === versaoSessao) publicar({ status: 'anonimo', usuario: null, accessToken: null });
      return false;
    } finally {
      renovacaoEmAndamento = null;
    }
  })();

  return renovacaoEmAndamento;
}

export async function restaurarSessao() {
  if (estado.status === 'autenticado') return true;
  return renovarSessao();
}

export async function sair() {
  iniciarTrocaDeAcesso();
  // A requisição captura o token atual antes de limpar a sessão e seu cache.
  const encerramento = requisicaoApi<void>('/autenticacao/sair', { method: 'POST', body: {}, repetirAposRenovar: false });
  acessoInvalidado = true;
  publicar({ status: 'anonimo', usuario: null, accessToken: null });
  await encerramento;
}

export async function solicitarRecuperacaoSenha(email: string) {
  await requisicaoPublica('/autenticacao/recuperacao-senha/solicitar', {
    method: 'POST',
    body: { email },
  });
}

export async function redefinirSenha(token: string, novaSenha: string) {
  await requisicaoPublica('/autenticacao/recuperacao-senha/redefinir', {
    method: 'POST',
    body: { token, novaSenha },
  });
}

configurarAutenticacaoApi({
  obterContexto: () => contextoAcesso,
  obterToken: () => (estado.status === 'autenticado' ? estado.accessToken : null),
  renovar: renovarSessao,
});
