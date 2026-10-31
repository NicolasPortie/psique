import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const API = 'http://localhost:5178/api/v1';
export const senhaPadrao = 'SenhaSegura123!';

export async function chamarApi(request: APIRequestContext, metodo: string, caminho: string, token?: string, data?: unknown) {
  return request.fetch(`${API}${caminho}`, {
    method: metodo,
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    data,
  });
}

/**
 * Cria a conta de teste pelo mesmo fluxo do cadastro: código por e-mail, comprovante e criação. Com a API em
 * Development e sem SMTP, o código é sempre 123456. Conta já existente (400/409/422) ou limite horário (429)
 * são aceitos: o login seguinte confirma.
 */
export async function garantirConta(request: APIRequestContext, conta: { nomeCompleto: string; email: string; crp: string; senha: string }) {
  const solicitacao = await chamarApi(request, 'POST', '/autenticacao/cadastro/codigo', undefined, { email: conta.email });
  if (!solicitacao.ok()) {
    if ([400, 409, 422, 429].includes(solicitacao.status())) return completarPerfilProfissional(request, conta);
    throw new Error(`Pedido de código de teste falhou (${solicitacao.status()}): ${await solicitacao.text()}`);
  }
  const { identificador } = (await solicitacao.json()).dados as { identificador: string };
  const confirmacao = await chamarApi(request, 'POST', '/autenticacao/cadastro/confirmar-email', undefined, {
    email: conta.email,
    identificador,
    codigo: '123456',
  });
  if (!confirmacao.ok()) throw new Error(`Confirmação de e-mail de teste falhou (${confirmacao.status()}): ${await confirmacao.text()}`);
  const { tokenEmail } = (await confirmacao.json()).dados as { tokenEmail: string };
  const resposta = await chamarApi(request, 'POST', '/autenticacao/cadastro', undefined, { ...conta, aceitouTermos: true, tokenEmail });
  if (!resposta.ok() && ![400, 409, 422, 429].includes(resposta.status())) {
    throw new Error(`Cadastro de teste falhou (${resposta.status()}): ${await resposta.text()}`);
  }
  await completarPerfilProfissional(request, conta);
}

/**
 * Conta recém-criada abre o convite "Complete seu perfil profissional" sobre as telas. As suítes partem de
 * uma conta já configurada; o convite em si é coberto por `perfil-profissional-inicial.spec.ts`.
 */
async function completarPerfilProfissional(request: APIRequestContext, conta: { nomeCompleto: string; email: string; crp: string; senha: string }) {
  // Sem login (senha trocada por uma execução interrompida), quem restaura a senha completa depois.
  const sessao = await chamarApi(request, 'POST', '/autenticacao/login', undefined, { email: conta.email, senha: conta.senha });
  if (!sessao.ok()) return;
  const token = ((await sessao.json()).dados as { accessToken: string }).accessToken;
  const situacao = await chamarApi(request, 'GET', '/dados-profissionais/situacao', token);
  if (situacao.ok() && !((await situacao.json()).dados as { pendente: boolean }).pendente) return;
  const salvo = await chamarApi(request, 'PUT', '/dados-profissionais', token, { nomeProfissional: conta.nomeCompleto, crp: conta.crp });
  if (!salvo.ok()) throw new Error(`Perfil profissional de teste falhou (${salvo.status()}): ${await salvo.text()}`);
}

export async function loginApi(request: APIRequestContext, email: string, senha: string) {
  const resposta = await chamarApi(request, 'POST', '/autenticacao/login', undefined, { email, senha });
  expect(resposta.ok()).toBeTruthy();
  return ((await resposta.json()).dados as { accessToken: string }).accessToken;
}

export async function entrar(page: Page, email: string, senha: string) {
  await page.goto('/login');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha', { exact: true }).fill(senha);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

export function observarConsole(page: Page, erros: string[]) {
  page.on('console', (mensagem) => {
    if (mensagem.type() === 'error') erros.push(mensagem.text());
  });
  page.on('pageerror', (erro) => erros.push(erro.message));
}

/** Descarta ruído esperado: favicon, devtools e a renovação de sessão sem cookie em cold start. */
export function semRuido(erros: string[]) {
  return erros.filter((mensagem) => {
    const texto = mensagem.toLowerCase();
    if (texto.includes('favicon') || texto.includes('react devtools') || texto.includes('token/renovar')) return false;
    if (texto.includes('401') && texto.includes('unauthorized')) return false;
    return true;
  });
}

const dois = (n: number) => String(n).padStart(2, '0');

/** Próximo dia útil a partir de `dias` dias, no horário local informado. */
export function diaUtilFuturo(dias: number, hora: number, minuto: number) {
  const base = new Date(Date.now() + dias * 24 * 3600 * 1000);
  while (base.getDay() === 0 || base.getDay() === 6) base.setTime(base.getTime() + 24 * 3600 * 1000);
  base.setHours(hora, minuto, 0, 0);
  return {
    iso: base.toISOString(),
    data: `${base.getFullYear()}-${dois(base.getMonth() + 1)}-${dois(base.getDate())}`,
    hora: `${dois(base.getHours())}:${dois(base.getMinutes())}`,
    dma: `${dois(base.getDate())}/${dois(base.getMonth() + 1)} ${dois(base.getHours())}:${dois(base.getMinutes())}`,
  };
}

/**
 * A conta de teste acumula consultas de execuções anteriores: sonda a API e avança de 7 em 7 dias
 * até achar uma janela sem sobreposição.
 */
export async function horarioLivre(request: APIRequestContext, token: string, diasBase: number, hora: number, minuto: number) {
  for (let tentativa = 0; tentativa < 40; tentativa++) {
    const candidato = diaUtilFuturo(diasBase + tentativa * 7, hora, minuto);
    const instante = new Date(candidato.iso).getTime();
    const lista = await chamarApi(
      request,
      'GET',
      `/consultas?inicio=${new Date(instante - 3600000).toISOString()}&fim=${new Date(instante + 3600000).toISOString()}`,
      token,
    );
    const itens = ((await lista.json()).dados as unknown[]) ?? [];
    if (itens.length === 0) return candidato;
  }
  throw new Error('Sem janela livre para o teste de ponta a ponta');
}

/**
 * Garante que a conta tenha o mínimo de dados para as telas aparecerem preenchidas
 * (lista, agenda, financeiro e documentos), útil para verificações visuais e de acessibilidade.
 */
export async function semearDadosBasicos(request: APIRequestContext, token: string) {
  for (const dia of ['Segunda', 'Terca', 'Quarta', 'Quinta', 'Sexta']) {
    await chamarApi(request, 'POST', '/agenda/horarios-disponiveis', token, { diaSemana: dia, horaInicio: '08:00:00', horaFim: '18:00:00', ativo: true });
  }
  const paciente = await chamarApi(request, 'POST', '/pacientes', token, { nomeCompleto: `Paciente Semente ${Date.now()}`, whatsApp: '11988887777', email: 'semente@exemplo.com.br' });
  expect(paciente.ok()).toBeTruthy();
  const pacienteId = ((await paciente.json()).dados as { id: string }).id;

  const tipo = await chamarApi(request, 'POST', '/agenda/tipos-atendimento', token, { nome: `Sessão semente ${Date.now()}`, duracaoMinutos: 50, modalidadePadrao: 'Presencial', ativo: true });
  expect(tipo.ok()).toBeTruthy();
  const tipoId = ((await tipo.json()).dados as { id: string }).id;

  const horario = await horarioLivre(request, token, 3, 9, 0);
  const consulta = await chamarApi(request, 'POST', '/consultas', token, {
    pacienteId,
    tipoAtendimentoId: tipoId,
    inicioEm: horario.iso,
    fimEm: new Date(new Date(horario.iso).getTime() + 50 * 60000).toISOString(),
    modalidade: 'Presencial',
  });
  expect(consulta.ok()).toBeTruthy();

  await chamarApi(request, 'POST', '/pagamentos', token, { pacienteId, valor: 180, formaPagamento: 'Pix', vencimentoEm: horario.iso });
  await chamarApi(request, 'POST', '/documentos', token, {
    pacienteId,
    tipo: 'Declaracao',
    titulo: 'Declaração de comparecimento',
    finalidade: 'Apresentação ao empregador',
    solicitante: 'Paciente',
    conteudo: 'Declaro, para os devidos fins, que a pessoa compareceu a atendimento psicológico nesta data.',
  });
}
