import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import PagamentosPage from './pagamentos-page';

function renderPagamentos(rota = '/pagamentos') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[rota]}>
        <PagamentosPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const pagamento = (sobrescrever: Record<string, unknown> = {}) => ({
  id: 'pg1',
  pacienteId: 'p1',
  paciente: 'Ana Silva',
  consultaId: null,
  valor: 180,
  status: 'Pendente',
  formaPagamento: 'Pix',
  vencimentoEm: '2026-10-20T12:00:00Z',
  pagoEm: null,
  observacao: 'Pagamento criado no agendamento.',
  ...sobrescrever,
});

const listagem = (itens: unknown[]) => ({
  sucesso: true,
  dados: { itens, resumo: { recebido: 400, pendente: 180, reembolsado: 0, quantidade: itens.length, vencido: 0 }, total: itens.length, pagina: 1, tamanhoPagina: 20 },
});

function responder(corpoLista: unknown, ok = true) {
  const chamadas: Array<{ url: string; metodo: string; corpo: unknown }> = [];
  const fetchSimulado = vi.fn((url: string, init?: RequestInit) => {
    const metodo = init?.method ?? 'GET';
    chamadas.push({ url, metodo, corpo: typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined });
    const corpo = metodo === 'PATCH' ? { sucesso: true, dados: pagamento({ status: 'Pago' }) } : corpoLista;
    return Promise.resolve({ ok: metodo === 'PATCH' ? true : ok, status: ok ? 200 : 500, json: () => Promise.resolve(corpo) });
  });
  vi.stubGlobal('fetch', fetchSimulado);
  return chamadas;
}

describe('PagamentosPage', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('exporta os últimos 12 meses dentro do limite da API ao listar todo o período', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 31, 12));
    const chamadas: string[] = [];
    vi.stubGlobal('fetch', vi.fn((url: string) => {
      chamadas.push(url);
      return Promise.resolve(Response.json(url.includes('/exportacao?') ? { sucesso: true, dados: [] } : listagem([])));
    }));
    renderPagamentos('/pagamentos?mes=todos');
    await screen.findByText('Nenhuma cobrança neste período');
    await userEvent.click(screen.getByRole('button', { name: 'Mais ações do financeiro' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: /Exportar recebimentos/ }));
    await waitFor(() => expect(chamadas.some((url) => url.includes('/exportacao?'))).toBe(true));
    const url = new URL(chamadas.find((item) => item.includes('/exportacao?'))!, 'http://localhost');
    expect(url.searchParams.get('de')).toBe('2025-11-01');
    expect(url.searchParams.get('ate')).toBe('2026-10-31');
  });

  it('mostra os totais e lista o pagamento com estado em texto', async () => {
    responder(listagem([pagamento()]));
    renderPagamentos();

    const lista = within(await screen.findByRole('table', { name: 'Pagamentos' }));
    expect(lista.getByRole('link', { name: 'Ana Silva' })).toHaveAttribute('href', '/pacientes/p1?aba=financeiro');
    expect(lista.getAllByText(/Pendente|Vencido/).length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Totais do período')).toHaveTextContent('Recebido');
  });

  it('recebe o pagamento com a forma escolhida, enviando o estado ao servidor', async () => {
    const chamadas = responder(listagem([pagamento()]));
    renderPagamentos();

    await userEvent.click(await screen.findByRole('button', { name: /Receber: .* de Ana Silva/ }));
    await userEvent.click(await screen.findByRole('radio', { name: 'Dinheiro' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar recebimento' }));

    await waitFor(() => expect(chamadas.some((c) => c.metodo === 'PATCH' && c.url.endsWith('/pagamentos/pg1/status'))).toBe(true));
    const envio = chamadas.find((c) => c.metodo === 'PATCH');
    expect(envio?.corpo).toMatchObject({ status: 'Pago', formaPagamento: 'Dinheiro' });
  });

  it('não oferece "Receber" para pagamento já pago', async () => {
    responder(listagem([pagamento({ status: 'Pago', pagoEm: '2026-10-11T12:00:00Z' })]));
    renderPagamentos();

    await screen.findByRole('table', { name: 'Pagamentos' });
    expect(screen.queryByRole('button', { name: /Receber/ })).not.toBeInTheDocument();
  });

  it('mostra como vencido o pendente que a API marca como vencido', async () => {
    responder(listagem([pagamento({ vencimentoEm: '2020-01-10T12:00:00Z', vencido: true })]));
    renderPagamentos('/pagamentos?mes=todos');

    const lista = within(await screen.findByRole('table', { name: 'Pagamentos' }));
    expect(lista.getAllByText('Vencido').length).toBeGreaterThan(0);
  });

  it('diferencia lista vazia de filtro sem resultados', async () => {
    responder(listagem([]));
    renderPagamentos();
    expect(await screen.findByText('Nenhuma cobrança neste período')).toBeVisible();
  });

  it('com filtro ativo e sem resultados, oferece limpar o filtro', async () => {
    responder(listagem([]));
    renderPagamentos('/pagamentos?estado=Pago');

    expect(await screen.findByText('Nada neste filtro')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Limpar filtros' })).toBeVisible();
  });

  it('exibe erro recuperável quando a API falha', async () => {
    responder({ sucesso: false, mensagem: 'Falha temporária.', erros: ['Falha temporária.'] }, false);
    renderPagamentos();

    expect(await screen.findByText('Financeiro indisponível')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Tentar novamente' })).toBeVisible();
  });
});
