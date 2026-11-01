import { ErroApi, type RespostaApi } from './resposta-api';

const API_URL_BASE = typeof import.meta.env.VITE_API_URL === 'string' ? import.meta.env.VITE_API_URL : '/api/v1';

let obterToken: () => string | null = () => null;
let renovar: (() => Promise<boolean>) | null = null;
let obterContexto: () => number = () => 0;
let envioSessaoEmAndamento: Promise<Response> | null = null;
const caminhosComCookie = new Set([
  '/autenticacao/login', '/autenticacao/cadastro', '/autenticacao/token/renovar',
  '/autenticacao/senha/alterar', '/autenticacao/sair',
]);

function enviar(caminho: string, init: RequestInit): Promise<Response> {
  const contextoAoEnfileirar = obterContexto();
  const fetchAtual = () => {
    if (caminhosComCookie.has(caminho) && contextoAoEnfileirar !== obterContexto()) {
      return Promise.reject(new ErroApi('O acesso mudou em outra solicitação. Entre novamente.', 409));
    }
    return fetch(`${API_URL_BASE}${caminho}`, init);
  };
  if (!caminhosComCookie.has(caminho)) return fetchAtual();
  const executar = async (): Promise<Response> => typeof navigator !== 'undefined' && navigator.locks
    ? await navigator.locks.request('psique:cookie-sessao', fetchAtual)
    : await fetchAtual();
  // O navegador aplica Set-Cookie antes de resolver fetch. Preserva a ordem das intenções,
  // inclusive quando uma renovação antiga termina depois de pedir para entrar ou sair.
  const envio = envioSessaoEmAndamento
    ? envioSessaoEmAndamento.catch(() => undefined).then(executar)
    : executar();
  envioSessaoEmAndamento = envio;
  const liberar = () => { if (envioSessaoEmAndamento === envio) envioSessaoEmAndamento = null; };
  void envio.then(liberar, liberar);
  return envio;
}

export function configurarAutenticacaoApi(configuracao: {
  obterToken: () => string | null;
  renovar: () => Promise<boolean>;
  obterContexto: () => number;
}) {
  obterToken = configuracao.obterToken;
  renovar = configuracao.renovar;
  obterContexto = configuracao.obterContexto;
}

interface OpcoesApi extends Omit<RequestInit, 'body'> {
  body?: unknown;
  repetirAposRenovar?: boolean;
}

export async function requisicaoApi<T>(caminho: string, opcoes: OpcoesApi = {}): Promise<T> {
  const { body, headers, repetirAposRenovar = true, ...init } = opcoes;
  const token = obterToken();
  const contexto = obterContexto();
  const resposta = await enviar(caminho, {
    ...init,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
  });

  // Uma operação iniciada por outra sessão nunca deve ser repetida com as credenciais atuais.
  if (resposta.status === 401 && repetirAposRenovar && contexto === obterContexto() && renovar && (await renovar()) && contexto === obterContexto()) {
    return requisicaoApi<T>(caminho, { ...opcoes, repetirAposRenovar: false });
  }

  if (!resposta.ok) {
    let payload: Partial<RespostaApi<unknown>> | null = null;
    try {
      payload = (await resposta.json()) as Partial<RespostaApi<unknown>>;
    } catch {
      // Respostas sem JSON usam uma mensagem segura e previsivel.
    }
    throw new ErroApi(
      payload?.erros?.[0] ?? payload?.mensagem ?? 'Não foi possível concluir a operação. Tente novamente.',
      resposta.status,
      payload?.erros ?? [],
    );
  }

  if (resposta.status === 204) return undefined as T;
  return (await resposta.json()) as T;
}

export async function requisicaoPublica<T>(caminho: string, opcoes: OpcoesApi = {}): Promise<T> {
  const { body, headers, ...init } = opcoes;
  const resposta = await enviar(caminho, {
    ...init,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...headers,
    },
  });

  if (!resposta.ok) {
    const payload = (await resposta.json().catch(() => null)) as Partial<RespostaApi<unknown>> | null;
    throw new ErroApi(
      payload?.erros?.[0] ?? payload?.mensagem ?? 'Não foi possível concluir a operação. Tente novamente.',
      resposta.status,
      payload?.erros ?? [],
    );
  }

  if (resposta.status === 204) return undefined as T;
  return (await resposta.json()) as T;
}
