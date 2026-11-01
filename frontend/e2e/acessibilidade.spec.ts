import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

import { entrar, garantirConta, loginApi, semearDadosBasicos, senhaPadrao } from './apoio';
import { preencherAcessoCadastro, simularEmailCadastro } from './cadastro-apoio';

const conta = {
  nomeCompleto: 'Psicologo E2E Acessibilidade',
  email: process.env.E2E_A11Y_EMAIL ?? 'e2e.acessibilidade@psique.local',
  crp: '06/777777',
  senha: process.env.E2E_A11Y_SENHA ?? senhaPadrao,
};

/** Regras WCAG 2.x A e AA. As violações saem no relatório do teste com o seletor de cada elemento. */
async function violacoes(pagina: Page, escopo?: string) {
  const verificador = new AxeBuilder({ page: pagina })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']);
  // Com um modal aberto, o fundo está coberto pelo scrim. A página é verificada
  // separadamente acima; aqui medimos a superfície disponível para interação.
  if (escopo) verificador.include(escopo);
  const resultado = await verificador.analyze();
  return resultado.violations.map((violacao) => ({
    regra: violacao.id,
    impacto: violacao.impact,
    ocorrencias: violacao.nodes.map((no) => no.target.join(' ')).slice(0, 5),
  }));
}

const publicas = ['/login', '/cadastro', '/recuperar-senha', '/termos', '/privacidade'];
const autenticadas = [
  '/painel',
  '/agenda',
  '/consultas/nova',
  '/pacientes',
  '/pacientes/novo',
  '/pagamentos',
  '/lista-espera',
  '/conversas',
  '/lembretes',
  '/documentos',
  '/relatorios',
  '/notificacoes',
  '/configuracoes/conta',
  '/configuracoes/agenda',
  '/configuracoes/profissional',
  '/configuracoes/equipe',
  '/configuracoes/auditoria',
  '/configuracoes/mensagens',
  '/configuracoes/whatsapp',
  '/isto-nao-existe',
];

for (const tema of ['light', 'dark'] as const) {
  test.describe(`Acessibilidade (WCAG AA) · tema ${tema === 'light' ? 'claro' : 'escuro'}`, () => {
    // Sem animações, o contraste medido não depende do instante em que as entradas escalonadas terminam.
    test.use({ colorScheme: tema, contextOptions: { reducedMotion: 'reduce' } });

    test('telas públicas não têm violações', async ({ page }, testInfo) => {
      await simularEmailCadastro(page);
      for (const rota of publicas) {
        await page.goto(rota);
        await page.waitForLoadState('networkidle');
        expect(await violacoes(page), rota).toEqual([]);
        if (rota === '/cadastro') {
          await page.screenshot({ path: `../output/playwright/cadastro-${tema}-${testInfo.project.name}.png`, fullPage: true });
          await preencherAcessoCadastro(page, 'rascunho.acessibilidade@psique.local');
          expect(await violacoes(page), 'cadastro · código do e-mail').toEqual([]);
          await page.screenshot({ path: `../output/playwright/cadastro-codigo-${tema}-${testInfo.project.name}.png`, fullPage: true });
        }
      }
    });

    test.describe('com dados do consultório', () => {
      test.beforeAll(async ({ request }) => {
        await garantirConta(request, conta);
        await semearDadosBasicos(request, await loginApi(request, conta.email, conta.senha));
      });

      test('telas autenticadas não têm violações', async ({ page }) => {
        // O mesmo teste navega por 20 telas e executa uma análise axe em cada uma.
        test.setTimeout(120_000);
        await entrar(page, conta.email, conta.senha);
        for (const rota of autenticadas) {
          await page.goto(rota);
          await page.waitForLoadState('networkidle');
          expect(await violacoes(page), rota).toEqual([]);
        }
      });

      test('gavetas, diálogos e menus não têm violações', async ({ page }) => {
        await entrar(page, conta.email, conta.senha);

        await page.goto('/pagamentos');
        await page.getByRole('button', { name: 'Nova cobrança' }).first().click();
        await expect(page.getByRole('dialog', { name: 'Nova cobrança' })).toBeVisible();
        expect(await violacoes(page, '[role="dialog"]'), 'gaveta de pagamento').toEqual([]);
        await page.keyboard.press('Escape');

        await page.goto('/documentos');
        await page.getByRole('button', { name: 'Novo documento' }).first().click();
        await expect(page.getByRole('dialog', { name: 'Novo documento' })).toBeVisible();
        expect(await violacoes(page, '[role="dialog"]'), 'gaveta de documento').toEqual([]);
        await page.keyboard.press('Escape');

        await page.goto('/configuracoes/equipe');
        await page
          .getByRole('button', { name: /Adicionar|Novo|Convidar/ })
          .first()
          .click();
        await expect(page.getByRole('dialog')).toBeVisible();
        expect(await violacoes(page, '[role="dialog"]'), 'gaveta de usuário').toEqual([]);
      });
    });
  });
}
