import { expect, test, type APIRequestContext } from '@playwright/test';

import { chamarApi, entrar, garantirConta, horarioLivre, loginApi, observarConsole, semRuido, senhaPadrao } from './apoio';

// O cadastro novo exige confirmar o e-mail; os testes reaproveitam a conta da jornada, criada antes dessa regra.
const conta = {
  nomeCompleto: 'Psicologo QA Consultorio',
  email: process.env.E2E_QA_EMAIL ?? 'qa.consultorio@psique.local',
  crp: '06/123456',
  senha: process.env.E2E_QA_SENHA ?? senhaPadrao,
};

async function dados<T>(resposta: Awaited<ReturnType<APIRequestContext['fetch']>>) {
  expect(resposta.ok(), await resposta.text()).toBeTruthy();
  return ((await resposta.json()) as { dados: T }).dados;
}

/** Conta com horários, um tipo de atendimento, nome e CRP (exigidos pelo recibo) e um paciente cobrado por sessão. */
async function prepararConta(request: APIRequestContext) {
  await garantirConta(request, conta);
  const token = await loginApi(request, conta.email, conta.senha);
  for (const diaSemana of ['Segunda', 'Terca', 'Quarta', 'Quinta', 'Sexta']) {
    await chamarApi(request, 'POST', '/agenda/horarios-disponiveis', token, { diaSemana, horaInicio: '07:00:00', horaFim: '21:00:00', ativo: true });
  }
  await chamarApi(request, 'PUT', '/dados-profissionais', token, { nomeProfissional: 'Psicóloga Rotina', crp: '06/123456' });
  const tipo = await dados<{ id: string }>(
    await chamarApi(request, 'POST', '/agenda/tipos-atendimento', token, { nome: `Rotina ${Date.now()}`, duracaoMinutos: 50, modalidadePadrao: 'Presencial', ativo: true }),
  );
  const sufixo = Date.now().toString().slice(-6);
  const paciente = await dados<{ id: string; nomeCompleto: string }>(
    await chamarApi(request, 'POST', '/pacientes', token, {
      nomeCompleto: `Rotina Paciente ${sufixo}`,
      whatsApp: '11977776666',
      valorSessao: 210,
      modoCobranca: 'PorSessao',
    }),
  );
  return { token, tipoId: tipo.id, paciente };
}

test.describe('Rotina clínica', () => {
  test('sessão realizada gera cobrança, que é recebida e vira recibo', async ({ page, request }) => {
    const erros: string[] = [];
    observarConsole(page, erros);
    const { token, tipoId, paciente } = await prepararConta(request);

    const horario = await horarioLivre(request, token, 2, 13, 0);
    const consulta = await dados<{ id: string }>(
      await chamarApi(request, 'POST', '/consultas', token, {
        pacienteId: paciente.id,
        tipoAtendimentoId: tipoId,
        inicioEm: horario.iso,
        fimEm: new Date(new Date(horario.iso).getTime() + 50 * 60_000).toISOString(),
        modalidade: 'Presencial',
      }),
    );
    // Realizada pela API: a cobrança nasce do desfecho, com o valor combinado com o paciente.
    await dados(await chamarApi(request, 'PATCH', `/consultas/${consulta.id}/status`, token, { status: 'Realizada' }));

    await entrar(page, conta.email, conta.senha);
    await page.goto(`/pacientes/${paciente.id}?aba=financeiro`);
    await expect(page.getByRole('table', { name: 'Cobranças do paciente' }).getByText(/R\$\s*210,00/)).toBeVisible();

    await page.getByRole('button', { name: /^Receber: / }).first().click();
    await page.getByRole('button', { name: 'Confirmar recebimento' }).click();
    await expect(page.getByText('Pagamento recebido.')).toBeVisible();

    await page.getByRole('button', { name: /Mais ações do pagamento/ }).first().click();
    await page.getByRole('menuitem', { name: 'Emitir recibo' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Recibo' })).toBeVisible();
    await expect(page.getByText('duzentos e dez reais')).toBeVisible();
    await expect(page.getByText('CRP 06/123456')).toBeVisible();

    expect(semRuido(erros), semRuido(erros).join(' | ')).toEqual([]);
  });

  test('nova consulta repetida toda semana cria a série', async ({ page, request }) => {
    const { token, paciente } = await prepararConta(request);
    const horario = await horarioLivre(request, token, 3, 7, 0);

    await entrar(page, conta.email, conta.senha);
    await page.goto(`/consultas/nova?pacienteId=${paciente.id}&data=${horario.data}&hora=${horario.hora}`);
    await page.getByLabel('Tipo de atendimento').selectOption({ index: 1 });
    await page.getByText('Toda semana', { exact: true }).click();
    await page.getByLabel('Quantas consultas').fill('3');
    await page.getByRole('button', { name: 'Agendar 3 consultas' }).click();

    await expect(page).toHaveURL(/\/consultas\/[0-9a-f-]{36}$/);
    await expect(page.getByText(/^Série · /)).toBeVisible();
  });

  test('busca global abre a ficha do paciente', async ({ page, request }) => {
    const { paciente } = await prepararConta(request);
    await entrar(page, conta.email, conta.senha);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await page.getByRole('button', { name: /^Buscar/ }).first().click();
    const busca = page.getByRole('combobox', { name: 'Buscar pacientes, áreas e ações' });
    await busca.fill(paciente.nomeCompleto);
    await expect(page.getByRole('option', { name: new RegExp(paciente.nomeCompleto) })).toBeVisible();
    await busca.press('Enter');
    await expect(page).toHaveURL(new RegExp(`/pacientes/${paciente.id}$`));
  });
});
