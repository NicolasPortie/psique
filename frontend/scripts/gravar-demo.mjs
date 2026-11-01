#!/usr/bin/env node
/**
 * Grava os GIFs do README em docs/screenshots/ usando a conta criada por `npm run seed:demo`.
 * Exige API e front ativos, além do ffmpeg no PATH.
 *
 *   npm run gravar:demo                      (todos, na ordem abaixo)
 *   npm run gravar:demo -- percurso celular  (só os escolhidos)
 *
 * Os roteiros fazem ações de verdade na conta de demonstração: agendam uma consulta, marcam uma sessão
 * como realizada, recebem a cobrança e registram a sessão no prontuário. Cada execução consome uma
 * consulta já passada e em aberto; `npm run seed:demo -- --renovar` repõe a agenda.
 */
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { chromium, devices } from '@playwright/test';

const BASE = process.env.BASE_URL ?? 'http://localhost:4200';
const API = process.env.API_URL ?? 'http://localhost:5178/api/v1';
const PSICOLOGA = { email: process.env.DEMO_EMAIL ?? 'helena.duarte@demo.psique.local', senha: process.env.DEMO_SENHA ?? 'DemoPsique#2026' };
const RECEPCAO = {
  email: process.env.DEMO_RECEPCAO_EMAIL ?? 'marina.prado@demo.psique.local',
  senha: process.env.DEMO_RECEPCAO_SENHA ?? 'DemoRecepcao#2026',
};
const PASTA_SAIDA = resolve(import.meta.dirname, '../../docs/screenshots');

for (const endereco of [BASE, API]) {
  if (!/localhost|127\.0\.0\.1/.test(endereco)) {
    console.error(`Recusado: ${endereco} não é um endereço local.`);
    process.exit(1);
  }
}

const pausa = (ms) => new Promise((resolver) => setTimeout(resolver, ms));
const doisDigitos = (numero) => String(numero).padStart(2, '0');
const dataIso = (data) => `${data.getFullYear()}-${doisDigitos(data.getMonth() + 1)}-${doisDigitos(data.getDate())}`;
const dataBr = (data) => `${doisDigitos(data.getDate())}/${doisDigitos(data.getMonth() + 1)}/${data.getFullYear()}`;

// ---------------------------------------------------------------------------------------------
// API: escolhe os alvos de cada roteiro com os dados do momento, para a gravação não depender do relógio.

async function entrarNaApi(conta) {
  const resposta = await fetch(`${API}/autenticacao/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(conta),
  });
  if (!resposta.ok) throw new Error(`Login de ${conta.email} falhou (${resposta.status}). Rode npm run seed:demo.`);
  const token = (await resposta.json()).dados.accessToken;
  return async (caminho) => {
    const resultado = await fetch(`${API}${caminho}`, { headers: { Authorization: `Bearer ${token}` } });
    if (!resultado.ok) throw new Error(`GET ${caminho} -> ${resultado.status}`);
    return (await resultado.json()).dados;
  };
}

/** Primeiro dia útil a partir de amanhã com horário livre, e um horário do meio da manhã ou da tarde. */
async function escolherHorarioLivre(api) {
  for (let deslocamento = 1; deslocamento <= 21; deslocamento += 1) {
    const dia = new Date();
    dia.setDate(dia.getDate() + deslocamento);
    const livres = await api(`/agenda/horarios-livres?data=${dataIso(dia)}&duracaoMinutos=50`);
    if (livres.length > 0) return { dia, hora: livres.find((hora) => hora >= '10:00') ?? livres[0] };
  }
  throw new Error('Nenhum horário livre nas próximas três semanas.');
}

/** Sessão já passada e ainda em aberto, de paciente cobrado por sessão: vira realizada e gera cobrança. */
async function escolherSessaoParaRealizar(api) {
  const agora = new Date();
  const inicio = new Date(agora);
  inicio.setDate(inicio.getDate() - 6);
  inicio.setHours(0, 0, 0, 0);
  const consultas = await api(`/consultas?inicio=${encodeURIComponent(inicio.toISOString())}&fim=${encodeURIComponent(agora.toISOString())}`);
  const candidatas = consultas
    .filter((consulta) => ['Agendada', 'AguardandoConfirmacao', 'Confirmada'].includes(consulta.status) && new Date(consulta.fimEm) < agora)
    .sort((a, b) => b.inicioEm.localeCompare(a.inicioEm));
  for (const consulta of candidatas) {
    const paciente = await api(`/pacientes/${consulta.pacienteId}`);
    if ((paciente.valorSessao ?? 0) > 0 && paciente.modoCobranca === 'PorSessao') {
      return { ...consulta, hoje: new Date(consulta.inicioEm).toDateString() === agora.toDateString() };
    }
  }
  throw new Error('Nenhuma sessão passada em aberto. Rode npm run seed:demo -- --renovar.');
}

/** Consulta realizada ainda sem registro de sessão, para o roteiro do prontuário. */
async function escolherSessaoSemRegistro(api) {
  const agora = new Date();
  const inicio = new Date(agora);
  inicio.setDate(inicio.getDate() - 30);
  const consultas = await api(`/consultas?inicio=${encodeURIComponent(inicio.toISOString())}&fim=${encodeURIComponent(agora.toISOString())}`);
  const realizadas = consultas.filter((consulta) => consulta.status === 'Realizada').sort((a, b) => b.inicioEm.localeCompare(a.inicioEm));
  for (const consulta of realizadas) {
    const registros = await api(`/sessoes/registros/${consulta.pacienteId}?pagina=1&tamanhoPagina=100`);
    if (!registros.itens.some((registro) => registro.consultaId === consulta.id)) return consulta;
  }
  throw new Error('Nenhuma consulta realizada sem registro. Grave o percurso antes do prontuário.');
}

// ---------------------------------------------------------------------------------------------
// Navegador e captura.

/** Ponteiro desenhado na página (a captura não inclui o do sistema): seta no computador, toque no celular. */
function instalarPonteiro(toque) {
  const montar = () => {
    if (document.getElementById('ponteiro-demo')) return;
    const ponteiro = document.createElement('div');
    ponteiro.id = 'ponteiro-demo';
    ponteiro.innerHTML = toque
      ? '<div style="width:44px;height:44px;border-radius:50%;background:rgb(30 30 28 / 0.22);border:2px solid rgb(255 255 255 / 0.85)"></div>'
      : '<svg width="22" height="22" viewBox="0 0 24 24"><path d="M4 2.5v17.2l4.6-4.4 2.9 6.6 3-1.3-2.9-6.5h6.3z" fill="#fff" stroke="#1d1d1b" stroke-width="1.6" stroke-linejoin="round"/></svg>';
    const ancora = toque ? [22, 22] : [4, 2];
    Object.assign(ponteiro.style, {
      position: 'fixed',
      left: '0',
      top: '0',
      zIndex: '2147483647',
      pointerEvents: 'none',
      transformOrigin: `${ancora[0]}px ${ancora[1]}px`,
      filter: toque ? 'none' : 'drop-shadow(0 1px 2px rgb(0 0 0 / 0.3))',
      transition: 'scale 120ms ease-out, opacity 160ms ease-out',
      opacity: toque ? '0' : '1',
    });
    const posicionar = (x, y) => {
      ponteiro.style.translate = `${x - ancora[0]}px ${y - ancora[1]}px`;
      sessionStorage.setItem('ponteiro-demo', JSON.stringify([x, y]));
    };
    const [x, y] = JSON.parse(sessionStorage.getItem('ponteiro-demo') ?? `[${innerWidth / 2},${innerHeight / 2}]`);
    posicionar(x, y);
    document.body.append(ponteiro);
    addEventListener('pointermove', (evento) => posicionar(evento.clientX, evento.clientY), true);
    addEventListener('pointerdown', (evento) => {
      posicionar(evento.clientX, evento.clientY);
      ponteiro.style.scale = toque ? '1' : '0.82';
      if (toque) ponteiro.style.opacity = '1';
    }, true);
    addEventListener('pointerup', () => {
      ponteiro.style.scale = '1';
      if (toque) setTimeout(() => (ponteiro.style.opacity = '0'), 140);
    }, true);
  };
  if (document.body) montar();
  else document.addEventListener('DOMContentLoaded', montar);
}

async function abrirNavegador({ celular = false } = {}) {
  const navegador = await chromium.launch();
  const perfil = celular
    ? { ...devices['Pixel 7'], viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 }
    : { viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 };
  const contexto = await navegador.newContext({ ...perfil, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', colorScheme: 'light' });
  await contexto.addInitScript(() => {
    if (!localStorage.getItem('psique:tema')) localStorage.setItem('psique:tema', 'claro');
    // O cartão de primeiros passos já foi visto: a demonstração mostra a rotina, não a configuração inicial.
    localStorage.setItem('psique:primeiros-passos-dispensado', '1');
  });
  await contexto.addInitScript(instalarPonteiro, celular);
  const pagina = await contexto.newPage();
  return { navegador, contexto, pagina, celular, ...perfil.viewport, escala: perfil.deviceScaleFactor };
}

async function entrar(pagina, conta) {
  await pagina.goto(`${BASE}/login`);
  await pagina.getByLabel('E-mail').fill(conta.email);
  await pagina.getByLabel('Senha', { exact: true }).fill(conta.senha);
  await pagina.getByRole('button', { name: 'Entrar' }).click();
  await pagina.waitForURL(/\/painel$/);
  await pagina.waitForLoadState('networkidle');
}

/** Ações com ponteiro visível e ritmo humano. */
function criarAtor(sessao) {
  const { pagina, celular } = sessao;
  let ponteiro = { x: sessao.width * 0.55, y: sessao.height * 0.5 };

  async function moverAte(alvo) {
    await alvo.waitFor();
    await alvo.scrollIntoViewIfNeeded();
    const caixa = await alvo.boundingBox();
    const destino = { x: caixa.x + Math.min(caixa.width / 2, 60), y: caixa.y + caixa.height / 2 };
    if (!celular) {
      const distancia = Math.hypot(destino.x - ponteiro.x, destino.y - ponteiro.y);
      await pagina.mouse.move(destino.x, destino.y, { steps: Math.max(12, Math.round(distancia / 18)) });
    }
    ponteiro = destino;
    return destino;
  }

  return {
    async clicar(alvo, espera = 1200) {
      const destino = await moverAte(alvo);
      await pausa(celular ? 250 : 180);
      if (celular) {
        await pagina.mouse.move(destino.x, destino.y);
        await pagina.mouse.down();
        await pausa(110);
        await pagina.mouse.up();
      } else {
        await pagina.mouse.down();
        await pausa(90);
        await pagina.mouse.up();
      }
      await pagina.waitForLoadState('networkidle');
      await pausa(espera);
    },
    async digitar(alvo, texto, espera = 500) {
      await this.clicar(alvo, 150);
      await pagina.keyboard.type(texto, { delay: 55 });
      await pausa(espera);
    },
    /** Escreve logo depois de um tópico do texto (ex.: "Avanços:"), sem depender de onde o clique caiu. */
    async digitarApos(alvo, marcador, texto, espera = 500) {
      await this.clicar(alvo, 150);
      await alvo.evaluate((campo, procurado) => {
        const posicao = campo.value.indexOf(procurado);
        const fim = posicao < 0 ? campo.value.length : posicao + procurado.length;
        campo.setSelectionRange(fim, fim);
      }, marcador);
      await pagina.keyboard.type(texto, { delay: 45 });
      await pausa(espera);
    },
    async rolarAte(alvo, espera = 500) {
      await alvo.evaluate((elemento) => elemento.scrollIntoView({ behavior: 'smooth', block: 'center' }));
      await pausa(espera + 500);
    },
    pausa,
  };
}

/** Grava a tela pelo screencast do Chrome (quadros nítidos) e converte em GIF com paleta própria. */
async function gravar(sessao, nome, { largura, quadrosPorSegundo = 15, cores = 192 }, roteiro) {
  const { contexto, pagina } = sessao;
  const pasta = mkdtempSync(join(tmpdir(), 'psique-demo-'));
  const quadros = [];
  const cdp = await contexto.newCDPSession(pagina);
  cdp.on('Page.screencastFrame', ({ data, metadata, sessionId }) => {
    const arquivo = join(pasta, `q${String(quadros.length).padStart(5, '0')}.jpg`);
    writeFileSync(arquivo, Buffer.from(data, 'base64'));
    quadros.push({ arquivo, instante: metadata.timestamp });
    void cdp.send('Page.screencastFrameAck', { sessionId }).catch(() => undefined);
  });
  await cdp.send('Page.startScreencast', {
    format: 'jpeg',
    quality: 95,
    maxWidth: sessao.width * sessao.escala,
    maxHeight: sessao.height * sessao.escala,
  });
  await pausa(300);
  await roteiro(criarAtor(sessao));
  const fim = Date.now() / 1000;
  await cdp.send('Page.stopScreencast');

  const lista = quadros.map(({ arquivo, instante }, indice) => {
    const proximo = quadros[indice + 1]?.instante ?? fim;
    return `file '${arquivo.replaceAll('\\', '/')}'\nduration ${Math.max(proximo - instante, 0.001).toFixed(3)}`;
  });
  lista.push(`file '${quadros.at(-1).arquivo.replaceAll('\\', '/')}'`);
  const roteiroQuadros = join(pasta, 'quadros.txt');
  writeFileSync(roteiroQuadros, lista.join('\n'));
  const saida = join(PASTA_SAIDA, `${nome}.gif`);
  execFileSync('ffmpeg', [
    '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', roteiroQuadros,
    '-vf',
    `fps=${quadrosPorSegundo},scale=${largura}:-1:flags=lanczos,split[a][b];` +
      `[a]palettegen=stats_mode=diff:max_colors=${cores}[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle`,
    '-loop', '0', saida,
  ], { stdio: 'inherit' });
  rmSync(pasta, { recursive: true, force: true });
  console.log(`${nome}.gif gravado (${quadros.length} quadros).`);
}

// ---------------------------------------------------------------------------------------------
// Roteiros.

/** Agendar uma consulta, marcar uma sessão como realizada e receber a cobrança que ela gerou. */
async function percurso() {
  const api = await entrarNaApi(PSICOLOGA);
  const { dia, hora } = await escolherHorarioLivre(api);
  const sessaoRealizar = await escolherSessaoParaRealizar(api);
  const sessao = await abrirNavegador();
  const { pagina } = sessao;
  await entrar(pagina, PSICOLOGA);
  const menu = pagina.getByRole('navigation', { name: 'Áreas do consultório' });
  // Visita prévia: carrega o código de cada tela para a gravação não mostrar esqueletos.
  for (const area of ['Agenda', 'Financeiro', 'Hoje']) {
    await menu.getByRole('link', { name: area }).click();
    await pagina.waitForLoadState('networkidle');
  }
  await pagina.goto(`${BASE}/consultas/nova`);
  await pagina.waitForLoadState('networkidle');
  await pagina.goto(`${BASE}/painel`);
  await pagina.waitForLoadState('networkidle');
  await pagina.mouse.move(sessao.width * 0.55, sessao.height * 0.5);

  await gravar(sessao, 'demo', { largura: 1000 }, async (ator) => {
    await ator.pausa(1600);
    const principal = pagina.getByRole('main');

    // 1. Agendar.
    await ator.clicar(principal.getByRole('link', { name: 'Nova consulta' }).first(), 900);
    await ator.digitar(pagina.getByRole('combobox', { name: 'Paciente' }), 'Gabriel', 900);
    await ator.clicar(pagina.getByRole('option', { name: /Gabriel Moreira/ }), 500);
    const tipo = pagina.getByLabel('Tipo de atendimento');
    const psicoterapia = await tipo.locator('option', { hasText: 'Psicoterapia individual' }).first().getAttribute('value').catch(() => null);
    if (psicoterapia) await tipo.selectOption(psicoterapia);
    else if (!(await tipo.inputValue())) await tipo.selectOption({ index: 1 });
    await ator.clicar(pagina.getByLabel('Data'), 200);
    await pagina.getByLabel('Data').fill(dataIso(dia));
    await pagina.waitForLoadState('networkidle');
    await ator.pausa(700);
    await ator.clicar(pagina.getByRole('list', { name: 'Horários livres' }).getByRole('button', { name: hora }), 700);
    const agendar = pagina.getByRole('button', { name: 'Agendar consulta' });
    await ator.rolarAte(agendar, 300);
    await ator.clicar(agendar, 2200);

    // 2. A sessão que já aconteceu: marcar como realizada (a API gera a cobrança).
    const menuLateral = pagina.getByRole('navigation', { name: 'Áreas do consultório' });
    if (sessaoRealizar.hoje) {
      await ator.clicar(menuLateral.getByRole('link', { name: 'Hoje' }), 1000);
      await ator.clicar(pagina.getByRole('main').getByRole('link', { name: sessaoRealizar.paciente }).first(), 1300);
    } else {
      await pagina.goto(`${BASE}/consultas/${sessaoRealizar.id}`);
      await pagina.waitForLoadState('networkidle');
      await ator.pausa(1000);
    }
    await ator.clicar(pagina.getByRole('button', { name: 'Marcar como realizada' }).first(), 2000);

    // 3. Receber a cobrança gerada.
    await ator.clicar(menuLateral.getByRole('link', { name: 'Financeiro' }), 1000);
    await ator.digitar(pagina.getByRole('main').getByPlaceholder('Buscar paciente'), sessaoRealizar.paciente.split(' ')[0], 1300);
    const linha = pagina
      .getByRole('row')
      .filter({ hasText: sessaoRealizar.paciente })
      .filter({ hasText: `Sessão de ${dataBr(new Date(sessaoRealizar.inicioEm))}` })
      .first();
    await ator.clicar(linha.getByRole('button', { name: /^Receber/ }), 900);
    await ator.clicar(pagina.getByRole('button', { name: 'Confirmar recebimento' }), 2600);
  });
  await sessao.navegador.close();
}

/** Registrar a sessão no prontuário, com o roteiro de tópicos e o texto da evolução. */
async function prontuario() {
  const api = await entrarNaApi(PSICOLOGA);
  const consulta = await escolherSessaoSemRegistro(api);
  const sessao = await abrirNavegador();
  const { pagina } = sessao;
  await entrar(pagina, PSICOLOGA);
  await pagina.goto(`${BASE}/pacientes/${consulta.pacienteId}/prontuario`);
  await pagina.waitForLoadState('networkidle');
  await pagina.goto(`${BASE}/consultas/${consulta.id}`);
  await pagina.waitForLoadState('networkidle');

  await gravar(sessao, 'prontuario', { largura: 900 }, async (ator) => {
    await ator.pausa(1300);
    await ator.clicar(pagina.getByRole('main').getByRole('link', { name: 'Registrar sessão' }).first(), 1300);
    const gaveta = pagina.getByRole('dialog');
    if (await pagina.getByRole('button', { name: 'Registrar sessão' }).first().isVisible() && !(await gaveta.isVisible())) {
      await ator.clicar(pagina.getByRole('button', { name: 'Registrar sessão' }).first(), 900);
    }
    const consultaCampo = gaveta.getByLabel('Consulta');
    if (!(await consultaCampo.inputValue().catch(() => 'preenchido'))) await consultaCampo.selectOption({ index: 1 });
    // O rádio é visualmente oculto; o clique vai no rótulo, como faria uma pessoa.
    const roteiros = gaveta.getByRole('radiogroup', { name: 'Roteiro' }).locator('label');
    if ((await roteiros.count()) > 1) await ator.clicar(roteiros.nth(1), 900);
    await ator.digitarApos(gaveta.getByLabel('Observações clínicas'), 'Demanda trazida:', ' ansiedade antes das provas.', 500);
    await ator.digitarApos(gaveta.getByLabel('Evolução'), 'Avanços:', ' retomou os exercícios e dorme melhor.', 800);
    await ator.clicar(gaveta.getByRole('button', { name: /^Salvar|^Registrar/ }).last(), 2600);
  });
  await sessao.navegador.close();
}

/** A recepção entra, cuida da agenda e dos pacientes, mas não abre o prontuário. */
async function recepcao() {
  const api = await entrarNaApi(PSICOLOGA);
  const pacientes = await api('/pacientes?pagina=1&tamanhoPagina=1');
  const paciente = pacientes.itens[0];
  const sessao = await abrirNavegador();
  const { pagina } = sessao;
  await pagina.goto(`${BASE}/login`);
  await pagina.waitForLoadState('networkidle');

  await gravar(sessao, 'recepcao', { largura: 900 }, async (ator) => {
    await ator.pausa(900);
    await ator.digitar(pagina.getByLabel('E-mail'), RECEPCAO.email, 250);
    await ator.digitar(pagina.getByLabel('Senha', { exact: true }), RECEPCAO.senha, 300);
    await ator.clicar(pagina.getByRole('button', { name: 'Entrar' }), 2000);
    await ator.clicar(pagina.getByRole('navigation', { name: 'Áreas do consultório' }).getByRole('link', { name: 'Pacientes' }), 900);
    await ator.clicar(pagina.getByRole('main').getByRole('link', { name: paciente.nomeCompleto }).first(), 2200);
    // Mesmo pelo endereço direto, o prontuário fica fechado: quem decide é a API.
    await pagina.goto(`${BASE}/pacientes/${paciente.id}/prontuario`);
    await pagina.waitForLoadState('networkidle');
    await ator.pausa(3000);
  });
  await sessao.navegador.close();
}

/** O dia no celular: painel, agenda e uma consulta. */
async function celular() {
  const sessao = await abrirNavegador({ celular: true });
  const { pagina } = sessao;
  await entrar(pagina, PSICOLOGA);
  const atalhos = pagina.getByRole('navigation', { name: 'Atalhos' });
  for (const area of ['Agenda', 'Pacientes', 'Hoje']) {
    await atalhos.getByRole('link', { name: area }).click();
    await pagina.waitForLoadState('networkidle');
  }

  await pagina.getByRole('heading', { name: /^(Bom dia|Boa tarde|Boa noite)/ }).waitFor();
  await pagina.getByText('Agenda de hoje', { exact: true }).waitFor();
  await gravar(sessao, 'celular', { largura: 360, cores: 160 }, async (ator) => {
    await ator.pausa(1500);
    await pagina.evaluate(() => window.scrollBy({ top: 420, behavior: 'smooth' }));
    await ator.pausa(1300);
    await pagina.evaluate(() => window.scrollTo({ top: 0, behavior: 'smooth' }));
    await ator.pausa(700);
    await ator.clicar(atalhos.getByRole('link', { name: 'Agenda' }), 1600);
    await ator.clicar(pagina.getByRole('main').getByRole('link').filter({ hasText: /\d{2}:\d{2}/ }).first(), 2000);
    await ator.clicar(atalhos.getByRole('link', { name: 'Pacientes' }), 1800);
  });
  await sessao.navegador.close();
}

const roteiros = { percurso, prontuario, recepcao, celular };
const escolhidos = process.argv.slice(2).filter((nome) => !nome.startsWith('-'));
for (const nome of escolhidos.length > 0 ? escolhidos : Object.keys(roteiros)) {
  if (!roteiros[nome]) throw new Error(`Roteiro desconhecido: ${nome}. Use: ${Object.keys(roteiros).join(', ')}.`);
  console.log(`Gravando ${nome}…`);
  await roteiros[nome]();
}
