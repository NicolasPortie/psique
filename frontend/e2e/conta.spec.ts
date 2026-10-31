import AxeBuilder from '@axe-core/playwright';
import { expect, test, type APIRequestContext } from '@playwright/test';

import { chamarApi, entrar, garantirConta, observarConsole, semRuido, senhaPadrao } from './apoio';
import {
  confirmarEmailTeste,
  preencherAcessoCadastro,
  simularEmailCadastro,
} from './cadastro-apoio';

/** Se uma execução anterior parou no meio, a conta pode estar com a senha trocada: devolve à padrão. */
async function restaurarSenhaPadrao(
  request: APIRequestContext,
  email: string,
  trocada: string,
  padrao: string,
) {
  const comPadrao = await chamarApi(request, 'POST', '/autenticacao/login', undefined, {
    email,
    senha: padrao,
  });
  if (comPadrao.ok()) return;
  const comTrocada = await chamarApi(request, 'POST', '/autenticacao/login', undefined, {
    email,
    senha: trocada,
  });
  if (!comTrocada.ok()) return;
  const token = ((await comTrocada.json()).dados as { accessToken: string }).accessToken;
  const resposta = await chamarApi(request, 'POST', '/autenticacao/senha/alterar', token, {
    senhaAtual: trocada,
    novaSenha: padrao,
  });
  expect(resposta.ok()).toBeTruthy();
}

test.describe('Conta e documentos legais', () => {
  test('acesso no celular prioriza o formulário em telas pequenas e na horizontal', async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== 'mobile', 'Adaptação específica do celular.');
    await simularEmailCadastro(page);
    for (const viewport of [
      { width: 320, height: 568 },
      { width: 390, height: 844 },
      { width: 844, height: 390 },
    ]) {
      await page.setViewportSize(viewport);
      for (const rota of ['/login', '/cadastro', '/recuperar-senha']) {
        await page.goto(rota);
        await expect(page.locator('h1')).toBeVisible();
        await expect(page.locator('.acesso-fotografia')).toBeHidden();
        const titulo = await page.locator('h1').boundingBox();
        expect(titulo?.y).toBeLessThan(160);
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
        ).toBeTruthy();
        const acao = page.locator('button[type="submit"]');
        await acao.scrollIntoViewIfNeeded();
        await expect(acao).toBeInViewport();
        if (rota === '/cadastro') {
          await preencherAcessoCadastro(page, 'rascunho.mobile@psique.local');
          expect(
            await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
          ).toBeTruthy();
          await acao.scrollIntoViewIfNeeded();
          await expect(acao).toBeInViewport();
        }
      }
    }
  });

  test('cadastro valida o e-mail antes de enviar a criação e preserva dados em caso de recusa', async ({
    page,
  }, testInfo) => {
    await simularEmailCadastro(page);
    await page.route('**/autenticacao/cadastro', (rota) =>
      rota.fulfill({
        status: 409,
        json: { sucesso: false, mensagem: 'Já existe uma conta associada a este e-mail.' },
      }),
    );
    const email = `e2e.cadastro.${testInfo.project.name}.${Date.now()}@psique.local`;
    const cadastros: string[] = [];
    const errosConsole: string[] = [];
    observarConsole(page, errosConsole);
    page.on('request', (requisicao) => {
      if (requisicao.method() === 'POST' && requisicao.url().endsWith('/autenticacao/cadastro'))
        cadastros.push(requisicao.url());
    });
    await page.goto('/cadastro');
    await preencherAcessoCadastro(page, email);
    expect(cadastros).toHaveLength(0);
    await expect(page.getByRole('button', { name: /Reenviar em/ })).toBeDisabled();
    await page.getByLabel('Código de confirmação').fill('000000');
    await expect(page.getByText('Código inválido ou expirado.')).toBeVisible();
    expect(cadastros).toHaveLength(0);
    await confirmarEmailTeste(page);
    await expect(page.getByText('Já existe uma conta associada a este e-mail.')).toBeVisible();
    expect(cadastros).toHaveLength(1);
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
    ).toBeTruthy();
    expect(
      (
        await new AxeBuilder({ page })
          .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
          .analyze()
      ).violations,
    ).toEqual([]);
    await page.getByRole('button', { name: 'Corrigir e-mail' }).click();
    await expect(page.getByLabel('E-mail')).toHaveValue(email);
    await expect(page.getByLabel('Senha', { exact: true })).toHaveValue(senhaPadrao);
    await expect(page.getByRole('checkbox')).toBeChecked();
    expect(semRuido(errosConsole).filter((mensagem) => !/400|409/.test(mensagem))).toEqual([]);
  });

  test('troca de senha: continua conectado, encerra as outras sessões e a senha antiga deixa de valer', async ({
    page,
    request,
  }) => {
    const errosConsole: string[] = [];
    observarConsole(page, errosConsole);
    // Conta própria e fixa por projeto: a troca é desfeita no fim, então reexecutar não cria contas
    // (o cadastro tem limite por hora) nem invalida as credenciais das demais suítes.
    const conta = {
      nomeCompleto: 'Psicologo E2E Senha',
      email: `e2e.senha.${test.info().project.name}@psique.local`,
      crp: '06/888888',
      senha: senhaPadrao,
    };
    const novaSenha = 'NovaSenhaSegura456!';
    await restaurarSenhaPadrao(request, conta.email, novaSenha, conta.senha);
    await garantirConta(request, conta);

    // Uma segunda sessão (outro dispositivo) que deve ser encerrada pela troca.
    const outraSessao = await chamarApi(request, 'POST', '/autenticacao/login', undefined, {
      email: conta.email,
      senha: conta.senha,
    });
    expect(outraSessao.ok(), `Login da segunda sessão: ${outraSessao.status()} ${await outraSessao.text()}`).toBeTruthy();
    const tokenAntigo = ((await outraSessao.json()).dados as { accessToken: string }).accessToken;

    await entrar(page, conta.email, conta.senha);
    await page.goto('/configuracoes/conta');
    await expect(page.getByRole('heading', { level: 1, name: 'Conta e segurança' })).toBeVisible();
    await expect(page.getByText(conta.email)).toBeVisible();

    // Senha atual errada: erro do servidor, sessão intacta.
    await page.getByLabel('Senha atual').fill('senha-errada-123');
    await page.getByLabel('Nova senha', { exact: true }).fill(novaSenha);
    await page.getByLabel('Confirmar nova senha').fill(novaSenha);
    await page.getByRole('button', { name: 'Alterar senha' }).click();
    await expect(page.getByText('A senha atual está incorreta.')).toBeVisible();
    await expect(page).toHaveURL(/\/configuracoes\/conta$/);

    await page.getByLabel('Senha atual').fill(conta.senha);
    await page.getByRole('button', { name: 'Alterar senha' }).click();
    await expect(
      page.getByText('Senha alterada. As outras sessões foram encerradas.'),
    ).toBeVisible();
    await expect(page.getByLabel('Senha atual')).toHaveValue('');

    // Esta sessão segue válida.
    await page.goto('/painel');
    await expect(page.getByRole('heading', { level: 1, name: /^(Bom dia|Boa tarde|Boa noite)/ })).toBeVisible();

    // A outra sessão perdeu o acesso, a senha antiga não entra mais e a nova entra.
    const comTokenAntigo = await chamarApi(request, 'GET', '/pacientes', tokenAntigo);
    expect(comTokenAntigo.status()).toBe(401);
    const antiga = await chamarApi(request, 'POST', '/autenticacao/login', undefined, {
      email: conta.email,
      senha: conta.senha,
    });
    expect(antiga.status()).toBe(401);
    const nova = await chamarApi(request, 'POST', '/autenticacao/login', undefined, {
      email: conta.email,
      senha: novaSenha,
    });
    expect(nova.ok()).toBeTruthy();
    await restaurarSenhaPadrao(request, conta.email, novaSenha, conta.senha);

    // O 400 da senha atual errada é provocado de propósito acima; qualquer outro erro é regressão.
    const inesperados = semRuido(errosConsole).filter(
      (mensagem) => !mensagem.includes('status of 400'),
    );
    expect(inesperados, `Erros de console: ${inesperados.join(' | ')}`).toEqual([]);
  });

  test('cadastro leva aos termos e à política de privacidade em nova aba', async ({
    page,
    context,
  }) => {
    await simularEmailCadastro(page);
    await page.goto('/cadastro');

    const [termos] = await Promise.all([
      context.waitForEvent('page'),
      page.locator('form').getByRole('link', { name: 'termos de uso', exact: true }).click(),
    ]);
    await expect(termos.getByRole('heading', { level: 1, name: 'Termos de uso' })).toBeVisible();
    await termos.getByRole('link', { name: 'Política de privacidade' }).first().click();
    await expect(
      termos.getByRole('heading', { level: 1, name: 'Política de privacidade' }),
    ).toBeVisible();
    await termos.close();

    // O formulário original segue intacto, sem aceite marcado.
    await expect(page.getByRole('checkbox', { name: /Li e aceito/ })).not.toBeChecked();
  });
});
