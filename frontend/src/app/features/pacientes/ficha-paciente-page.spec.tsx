import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import FichaPacientePage from './ficha-paciente-page';

const acesso = vi.hoisted(() => ({ agenda: true, financeiro: false }));
vi.mock('../../core/autenticacao/permissoes', () => ({
  usePermissoes: () => ({ acessoClinico: false, pode: (permissao: string) => (permissao === 'Agenda' && acesso.agenda) || (permissao === 'Financeiro' && acesso.financeiro) }),
}));

const paciente = {
  id: 'p1', nomeCompleto: 'Ana Silva', status: 'Ativo', dataNascimento: null,
  cpfMascarado: null, cpf: null, telefone: null, whatsApp: null, email: null,
  observacaoAdministrativa: null, valorSessao: null, modoCobranca: 'PorSessao',
  consentimentoRegistradoEm: null, contatosAdicionais: [], criadoEm: '2026-01-01T12:00:00Z',
};

function renderFicha(rota = '/pacientes/p1') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[rota]}><Routes>
    <Route path="/pacientes/:id" element={<FichaPacientePage />} />
  </Routes></MemoryRouter></QueryClientProvider>);
}

describe('ficha do paciente com acesso parcial ou erro', () => {
  afterEach(() => { acesso.agenda = true; acesso.financeiro = false; vi.unstubAllGlobals(); });

  it('sem acesso à agenda não consulta horários nem oferece agendamento ou aba de consultas', async () => {
    acesso.agenda = false;
    const fetch = vi.fn((url: string) => Promise.resolve(Response.json({ sucesso: true, dados: url.includes('/consultas?') ? [] : paciente })));
    vi.stubGlobal('fetch', fetch);
    renderFicha('/pacientes/p1?aba=consultas');
    await screen.findByRole('heading', { name: 'Ana Silva' });
    expect(screen.queryByRole('link', { name: 'Agendar consulta' })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /Consultas/ })).not.toBeInTheDocument();
    expect(screen.queryByText('Nada agendado')).not.toBeInTheDocument();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  });

  it('falha na agenda não vira ausência de consultas ou contagem zero', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string) => Promise.resolve(url.includes('/consultas?')
      ? Response.json({ sucesso: false, mensagem: 'Agenda indisponível.' }, { status: 503 })
      : Response.json({ sucesso: true, dados: paciente }))));
    renderFicha();
    await screen.findByRole('heading', { name: 'Ana Silva' });
    await screen.findByText('Não foi possível carregar as consultas deste paciente.');
    expect(screen.queryByText('Nada agendado')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tentar novamente' })).toBeVisible();
  });

  it('paciente inativo não oferece agendamento', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string) => Promise.resolve(Response.json({ sucesso: true,
      dados: url.includes('/consultas?') ? [] : { ...paciente, status: 'Inativo' } }))));
    renderFicha();
    await screen.findByRole('heading', { name: 'Ana Silva' });
    expect(screen.queryByRole('link', { name: 'Agendar consulta' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: /Consultas/ }));
    await screen.findByText('Nenhuma consulta no último ano');
    expect(screen.queryByRole('link', { name: 'Agendar consulta' })).not.toBeInTheDocument();
  });

  it('falha no financeiro é recuperável e não deixa os totais carregando para sempre', async () => {
    acesso.agenda = false;
    acesso.financeiro = true;
    vi.stubGlobal('fetch', vi.fn((url: string) => Promise.resolve(url.includes('/pagamentos?')
      ? Response.json({ sucesso: false }, { status: 503 })
      : Response.json({ sucesso: true, dados: paciente }))));
    renderFicha();
    expect(await screen.findByText('Não foi possível carregar os pagamentos deste paciente.')).toBeVisible();
    expect(screen.getByText('Indisponível')).toBeVisible();
  });
});
