import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { FinanceiroDoPaciente } from './financeiro-do-paciente';

afterEach(() => vi.unstubAllGlobals());

it('permite alcançar cobranças além das primeiras 50 mantendo paciente e página na consulta', async () => {
  const chamadas: string[] = [];
  vi.stubGlobal('fetch', vi.fn((url: string) => {
    chamadas.push(url);
    const pagina = Number(new URL(url, 'http://localhost').searchParams.get('pagina'));
    return Promise.resolve(Response.json({ sucesso: true, dados: {
      itens: [{ id: `pg${pagina}`, pacienteId: 'p1', paciente: 'Ana Silva', consultaId: null, valor: 180, status: 'Pendente', formaPagamento: 'Pix',
        vencimentoEm: null, pagoEm: null, observacao: `Cobrança da página ${pagina}` }],
      resumo: { recebido: 0, pendente: 9180, vencido: 0, quantidade: 51 }, total: 51, pagina, tamanhoPagina: 50,
    } }));
  }));
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/pacientes/p1?aba=financeiro']}>
    <FinanceiroDoPaciente pacienteId="p1" />
  </MemoryRouter></QueryClientProvider>);
  await screen.findByRole('table', { name: 'Cobranças do paciente' });
  await userEvent.click(screen.getByRole('button', { name: 'Próxima página' }));
  await waitFor(() => expect(chamadas.some((url) => url.includes('pagina=2') && url.includes('pacienteId=p1'))).toBe(true));
  expect(await screen.findByText('Cobrança da página 2')).toBeVisible();
});
