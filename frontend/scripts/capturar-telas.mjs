/** Capturas reais do README. Requer API, Vite e seed:demo locais. */
import { resolve } from 'node:path';
import { chromium, devices } from '@playwright/test';

const base = 'http://localhost:4200';
const saida = resolve(import.meta.dirname, '../../docs/screenshots');
const navegador = await chromium.launch();
try {
  for (const celular of [false, true]) {
    const contexto = await navegador.newContext(celular
      ? { ...devices['Pixel 7'], viewport: { width: 390, height: 844 }, colorScheme: 'light' }
      : { viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, colorScheme: 'light' });
    const pagina = await contexto.newPage();
    const erros = [];
    pagina.on('pageerror', erro => erros.push(erro.message));
    await pagina.goto(`${base}/login`);
    await pagina.getByLabel('E-mail').waitFor();
    await pagina.locator('img').evaluateAll(imagens => Promise.all(imagens.map(imagem => imagem.decode())));
    await pagina.evaluate(() => document.fonts.ready);
    if (!celular) await pagina.screenshot({ path: `${saida}/acesso.png` });
    await pagina.getByLabel('E-mail').fill(process.env.DEMO_EMAIL ?? 'helena.duarte@demo.psique.local');
    await pagina.getByLabel('Senha', { exact: true }).fill(process.env.DEMO_SENHA ?? 'DemoPsique#2026');
    await pagina.getByRole('button', { name: 'Entrar', exact: true }).click();
    await pagina.waitForURL('**/painel');
    const telas = celular ? [['painel', 'painel-celular'], ['agenda', 'agenda-celular']]
      : [['painel', 'painel'], ['agenda', 'agenda'], ['pacientes', 'pacientes'], ['pagamentos', 'financeiro']];
    for (const [rota, arquivo] of telas) {
      await pagina.goto(`${base}/${rota}`);
      await pagina.waitForLoadState('networkidle');
      await pagina.getByRole('main').getByRole('heading').first().waitFor();
      await pagina.evaluate(() => document.fonts.ready);
      if (await pagina.getByRole('button', { name: 'Tentar novamente', exact: true }).count())
        throw new Error(`A tela ${rota} apresentou erro.`);
      await pagina.screenshot({ path: `${saida}/${arquivo}.png` });
      console.log(`Capturada: ${arquivo}`);
    }
    if (!celular) {
      await pagina.evaluate(() => localStorage.setItem('psique:tema', 'escuro'));
      await pagina.goto(`${base}/painel`);
      await pagina.waitForLoadState('networkidle');
      await pagina.getByRole('main').getByRole('heading').first().waitFor();
      await pagina.screenshot({ path: `${saida}/painel-escuro.png` });
    }
    if (erros.length) throw new Error(erros.join('\n'));
    await contexto.close();
  }
} finally { await navegador.close(); }
