import { expect, test } from '@playwright/test';
import { entrar, garantirConta, senhaPadrao } from './apoio';

test('troca de pessoa e saída invalidam a outra aba que compartilha o cookie', async ({ page, context, request }) => {
  const sufixo = test.info().project.name;
  const ana = { nomeCompleto: 'Ana Teste Abas', email: `ana.abas.${sufixo}@psique.local`, senha: senhaPadrao, crp: '06/123456' };
  const bia = { nomeCompleto: 'Bia Teste Abas', email: `bia.abas.${sufixo}@psique.local`, senha: senhaPadrao, crp: '06/123457' };
  await garantirConta(request, ana);
  await garantirConta(request, bia);
  const outra = await context.newPage();
  await outra.goto('/login');
  await expect(outra.getByLabel('E-mail')).toBeVisible();
  await entrar(page, ana.email, ana.senha);
  await outra.getByLabel('E-mail').fill(bia.email);
  await outra.getByLabel('Senha', { exact: true }).fill(bia.senha);
  await outra.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(outra).toHaveURL(/\/painel$/);
  await expect(outra.getByRole('heading', { name: /Bia/ })).toBeVisible();
  await page.goto('/painel');
  await expect(page).toHaveURL(/\/painel$/);
  await expect(page.getByRole('heading', { name: /Bia/ })).toBeVisible();
  if (test.info().project.name === 'mobile') {
    await outra.getByRole('button', { name: 'Mais', exact: true }).click();
    await outra.getByRole('button', { name: 'Sair com segurança' }).click();
  } else {
    await outra.getByRole('button', { name: /Menu da conta/ }).click();
    await outra.getByRole('menuitem', { name: 'Sair com segurança' }).click();
  }
  await expect(outra).toHaveURL(/\/login/);
  await expect(page).toHaveURL(/\/login/);
});
