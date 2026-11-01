import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { AutenticacaoResponse } from './autenticacao.types';

function autenticacao(id: string): AutenticacaoResponse {
  return { accessToken: `token-${id}`, expiraEm: '2030-01-01', usuario: { id, nomeCompleto: id, email: `${id}@teste.local`, perfil: 'Psicologo' } };
}

function resposta(id: string) {
  return Response.json({ sucesso: true, dados: autenticacao(id) });
}

function adiarResposta() {
  let resolver!: (value: Response) => void;
  const promessa = new Promise<Response>((resolve) => { resolver = resolve; });
  return { promessa, resolver };
}

describe('concorrência da sessão', () => {
  beforeEach(() => vi.resetModules());
  afterEach(() => vi.unstubAllGlobals());

  it('cancela alteração de cookie ainda aguardando a trava quando outra aba muda o acesso', async () => {
    let liberar!: () => void;
    vi.stubGlobal('navigator', { locks: { request: vi.fn((_nome: string, executar: () => Promise<Response>) =>
      new Promise<Response>((resolve, reject) => { liberar = () => { void executar().then(resolve, reject); }; })) } });
    let receber!: (evento: MessageEvent) => void;
    vi.stubGlobal('BroadcastChannel', class {
      set onmessage(valor: (evento: MessageEvent) => void) { receber = valor; }
      postMessage = vi.fn();
    });
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    const sessao = await import('./autenticacao.service');
    const entrada = sessao.entrar({ email: 'ana@teste.local', senha: 'senha' });
    const recusada = expect(entrada).rejects.toMatchObject({ status: 409 });
    receber(new MessageEvent('message', { data: { tipo: 'acesso-alterado' } }));
    liberar();
    await recusada;
    expect(fetch).not.toHaveBeenCalled();
    expect(sessao.obterEstadoSessao().status).toBe('anonimo');
  });

  it('não muda silenciosamente de pessoa ao renovar um cookie compartilhado com outra aba', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(resposta('ana')).mockResolvedValueOnce(resposta('bia')));
    const sessao = await import('./autenticacao.service');
    await sessao.entrar({ email: 'ana@teste.local', senha: 'senha' });
    expect(await sessao.renovarSessao()).toBe(false);
    expect(sessao.obterEstadoSessao().status).toBe('anonimo');
  });

  it('limpa a sessão quando outra aba altera o acesso, sem compartilhar tokens', async () => {
    const canais: Array<{ onmessage: ((evento: MessageEvent) => void) | null; postMessage: ReturnType<typeof vi.fn> }> = [];
    vi.stubGlobal('BroadcastChannel', class {
      onmessage: ((evento: MessageEvent) => void) | null = null;
      postMessage = vi.fn();
      constructor() { canais.push(this); }
    });
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(resposta('ana'))));
    const sessao = await import('./autenticacao.service');
    await sessao.entrar({ email: 'ana@teste.local', senha: 'senha' });
    expect(canais).toHaveLength(1);
    expect(JSON.stringify(canais[0]!.postMessage.mock.calls)).not.toContain('token-ana');
    canais[0]!.onmessage?.(new MessageEvent('message', { data: { tipo: 'acesso-alterado' } }));
    expect(sessao.obterEstadoSessao().status).toBe('anonimo');
    expect(await sessao.restaurarSessao()).toBe(false);
    expect(await sessao.renovarSessao()).toBe(false);
  });

  it('descarta renovação que chega depois da saída', async () => {
    const pendente = adiarResposta();
    const fetch = vi.fn().mockResolvedValueOnce(resposta('ana')).mockReturnValueOnce(pendente.promessa).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);
    const sessao = await import('./autenticacao.service');
    await sessao.entrar({ email: 'ana@teste.local', senha: 'senha' });
    const renovacao = sessao.renovarSessao();
    const saida = sessao.sair();
    expect(fetch).toHaveBeenCalledTimes(2);
    pendente.resolver(resposta('ana'));
    expect(await renovacao).toBe(false);
    await saida;
    expect(sessao.obterEstadoSessao().status).toBe('anonimo');
  });

  it.each([true, false])('renovação antiga (sucesso=%s) preserva o login de outra pessoa', async (sucesso) => {
    const pendente = adiarResposta();
    const fetch = vi.fn().mockReturnValueOnce(pendente.promessa).mockResolvedValueOnce(resposta('bia'));
    vi.stubGlobal('fetch', fetch);
    const sessao = await import('./autenticacao.service');
    const renovacao = sessao.renovarSessao();
    const entrada = sessao.entrar({ email: 'bia@teste.local', senha: 'senha' });
    expect(fetch).toHaveBeenCalledTimes(1);
    pendente.resolver(sucesso ? resposta('ana') : new Response(null, { status: 401 }));
    await renovacao;
    await entrada;
    expect(sessao.obterEstadoSessao().usuario?.id).toBe('bia');
  });

  it('saída lenta não encerra um novo login e limpa a sessão imediatamente', async () => {
    const pendente = adiarResposta();
    const fetch = vi.fn().mockResolvedValueOnce(resposta('ana')).mockReturnValueOnce(pendente.promessa).mockResolvedValueOnce(resposta('bia'));
    vi.stubGlobal('fetch', fetch);
    const sessao = await import('./autenticacao.service');
    await sessao.entrar({ email: 'ana@teste.local', senha: 'senha' });
    const saida = sessao.sair();
    expect(sessao.obterEstadoSessao().status).toBe('anonimo');
    const entrada = sessao.entrar({ email: 'bia@teste.local', senha: 'senha' });
    expect(fetch).toHaveBeenCalledTimes(2);
    pendente.resolver(new Response(null, { status: 204 }));
    await saida;
    await entrada;
    expect(sessao.obterEstadoSessao().usuario?.id).toBe('bia');
  });

  it('compartilha uma única renovação entre requisições concorrentes', async () => {
    const pendente = adiarResposta();
    const fetch = vi.fn().mockReturnValue(pendente.promessa);
    vi.stubGlobal('fetch', fetch);
    const sessao = await import('./autenticacao.service');
    const primeira = sessao.renovarSessao();
    const segunda = sessao.renovarSessao();
    pendente.resolver(resposta('ana'));
    expect(await primeira).toBe(true);
    expect(await segunda).toBe(true);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('não reenvia uma operação da sessão anterior depois de trocar a pessoa conectada', async () => {
    const pendente = adiarResposta();
    const fetch = vi.fn().mockResolvedValueOnce(resposta('ana')).mockReturnValueOnce(pendente.promessa).mockResolvedValueOnce(resposta('bia')).mockImplementation(() => Promise.resolve(resposta('bia')));
    vi.stubGlobal('fetch', fetch);
    const sessao = await import('./autenticacao.service');
    const { requisicaoApi } = await import('../api/cliente-api');
    await sessao.entrar({ email: 'ana@teste.local', senha: 'senha' });
    const operacao = requisicaoApi('/pacientes', { method: 'POST', body: { nomeCompleto: 'Paciente da Ana' } });
    await sessao.entrar({ email: 'bia@teste.local', senha: 'senha' });
    pendente.resolver(new Response(null, { status: 401 }));
    await expect(operacao).rejects.toMatchObject({ status: 401 });
    expect(fetch).toHaveBeenCalledTimes(3);
    expect(sessao.obterEstadoSessao().usuario?.id).toBe('bia');
  });

  it('uma resposta de login pendente não restaura a sessão depois de sair', async () => {
    const pendente = adiarResposta();
    vi.stubGlobal('fetch', vi.fn().mockReturnValueOnce(pendente.promessa).mockResolvedValueOnce(new Response(null, { status: 204 })));
    const sessao = await import('./autenticacao.service');
    const entrada = sessao.entrar({ email: 'ana@teste.local', senha: 'senha' });
    const recusada = expect(entrada).rejects.toThrow();
    const saida = sessao.sair();
    pendente.resolver(resposta('ana'));
    await recusada;
    await saida;
    expect(sessao.obterEstadoSessao().status).toBe('anonimo');
  });
});
