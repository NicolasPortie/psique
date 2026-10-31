---
name: Psique
description: Ferramenta de trabalho calma para o psicólogo e a equipe do consultório, em verde-musgo sobre linho, com painéis claros e cantos macios.
colors:
  paper: "#faf8f3"
  rail: "#f4f1ea"
  surface: "#fefdfb"
  field: "#ffffff"
  hover: "#eef2ed"
  selected: "#dbeede"
  line: "#dee3de"
  line-strong: "#7c837a"
  ink: "#1b211c"
  ink-muted: "#515b53"
  ink-faint: "#5d665e"
  accent: "#23593d"
  accent-hover: "#184b31"
  accent-ink: "#164e33"
  accent-wash: "#e5f5e7"
  on-accent: "#f9fdfa"
  focus: "#2b7a52"
  ocre: "#dcaf61"
  danger: "#a83825"
  danger-ink: "#932d1c"
  danger-wash: "#ffeee7"
  warning: "#b97a13"
  warning-ink: "#694414"
  warning-wash: "#fff6d9"
  success: "#006f55"
  success-ink: "#00503c"
  success-wash: "#e4f9f1"
typography:
  title:
    fontFamily: "Jost Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "1.625rem"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "-0.015em"
  section:
    fontFamily: "Jost Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 600
    lineHeight: 1.5
  body:
    fontFamily: "Atkinson Hyperlegible Next Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Atkinson Hyperlegible Next Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 500
    lineHeight: 1.43
rounded:
  xs: "4px"
  sm: "6px"
  md: "10px"
  lg: "14px"
  xl: "20px"
  pill: "9999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "16px"
  lg: "24px"
  xl: "40px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.on-accent}"
    rounded: "{rounded.md}"
    height: "36px"
    padding: "0 14px"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
    textColor: "{colors.on-accent}"
  button-secondary:
    backgroundColor: "{colors.field}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    height: "36px"
  input:
    backgroundColor: "{colors.field}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    height: "36px"
---

# Design System: Psique

## Overview

**Estrela do norte criativa: "o azulejo como módulo"**

Uma ferramenta de trabalho para o psicólogo e para quem trabalha com ele, aberta o dia inteiro. Por isso é calma: fundo de porcelana branca, texto em grafite quente, um único verde-musgo para o que é ação, e nenhuma decoração que dispute atenção com a rotina. A marca tem origem nos painéis de azulejo modulares (Athos Bulcão): um módulo simples, repetido e combinado. Aqui o módulo vive só onde tem função, na marca e nas marcas de estado, em que cada coisa é o mesmo azulejo em um ponto do preenchimento.

O nome, **Psique**, vem da palavra grega para mente e alma, o objeto de trabalho do psicólogo. O símbolo é um azulejo com duas metades de um semicírculo, porcelana e ocre, que se encontram no meio: o encontro entre duas pessoas, psicólogo e paciente.

A interface é tipográfica, macia e moderadamente densa. O fundo é linho (branco quente) e os blocos que importam moram em painéis claros de cantos macios, separados por régua fina e sem sombra. Estados e filtros são pílulas. Não há grade de cartões iguais, métricas gigantes, faixas laterais coloridas, ilustrações nem padrões decorativos (ver `docs/adr/0003-cantos-macios-e-paineis.md`). A única imagem é uma fotografia de ambiente de consultório (duas poltronas frente a frente, luz natural) na tela de acesso, sem pessoas e sem texto por cima; ver `docs/04-ux/imagens.md`.

Rejeita: azul saturado e padrões vibrantes (parecem banco ou jogo, não consultório), estética hospitalar, ícones terapêuticos literais (folha, cérebro, psi), template SaaS genérico e qualquer sinal de painel multi-clínica.

**Características-chave:**

- trilho lateral recolhível no desktop e navegação inferior no celular, com a mesma informação em ambos;
- fundo de linho com painéis claros; no tema escuro, grafite quente com um sopro de verde, sem azul;
- cantos macios (10 px nos controles, 14 px nos painéis, 20 px nas folhas) e estados em pílula;
- um único acento verde-musgo, mais ambar para atenção, tijolo para erro e pinho para concluído;
- dois tipos: Jost nos títulos e na marca, Atkinson Hyperlegible Next no texto;
- movimento curto (120–300 ms), `ease-out` forte, removível com `prefers-reduced-motion`.

## Colors

Cores em OKLCH, declaradas como papéis semânticos em `frontend/src/app/styles/tokens.css`. Componentes só leem papéis (`bg-paper`, `text-ink`, `border-line`), nunca valores literais. Todos os pares de texto são medidos por `npm run contraste` (AA no mínimo, AAA no texto principal), nos dois temas.

### Primary

- **Verde-musgo** (`accent`): ação primária, seleção atual, foco e o estado "confirmada". O verde é a cor internacional da saúde mental; aqui aparece fechado e sóbrio, nunca em tom de menta ou de "saúde". `accent-ink` para texto e links, `accent-wash` para fundos de seleção suave.

### Semânticas

- **Tijolo** (`danger`): erro, ação destrutiva e atenção alta.
- **Âmbar** (`warning`): pendências que pedem atenção sem urgência.
- **Pinho** (`success`): concluído, mais fundo que o acento para a marca de check se distinguir. Sempre com marca e texto.
- **Ocre** (`ocre`): só na marca, nunca como cor de interface.

### Neutral

- **Linho** (`paper`), **trilho** (`rail`), **painel** (`surface`), **campo** (`field`): níveis de superfície com diferença de tom, não de sombra. O painel é branco sobre o linho.
- **Grafite** (`ink`, `ink-muted`, `ink-faint`): três níveis de texto, todos acima de 4,5:1.
- **Régua** (`line`, `line-strong`): `line` separa linhas de lista; `line-strong` contorna controles e passa 3:1.

**A regra da voz única.** O acento ocupa no máximo 10% da tela; a raridade comunica prioridade.

## Typography

**Títulos e marca:** Jost Variable, geométrica de herança modernista, par dos módulos do azulejo.
**Texto:** Atkinson Hyperlegible Next Variable, desenhada para a leitura sem esforço, em listas, formulários e navegação. Numerais tabulares (`.num`) onde há valores, horas e datas.

### Escala curta e fixa

- **Título** (Jost 600, 1,625 rem): título da página. Um por tela.
- **Seção** (Jost 600, 1 rem): agrupamentos dentro da página.
- **Corpo** (Atkinson 400, 0,9375 rem, entrelinha 1,6): conteúdo e instruções; prosa limitada a 60 caracteres por linha. Quinze pixels porque a ferramenta fica aberta o dia inteiro.
- **Rótulo** (Atkinson 500, 0,875 rem): campos e navegação. Legendas e cabeçalhos de coluna em 0,8125 rem.

**A regra da hierarquia silenciosa.** Peso e espaço criam estrutura. Nada de rótulos em caixa alta com espaçamento, nem números gigantes decorativos.

## Elevation

Plano por padrão. A separação vem de tom, régua e painel: painéis claros (`surface`) com régua de 1 px e cantos de 14 px agrupam o que importa (tabelas, listas, formulários, detalhes). Sombra só em superfícies que flutuam (menus, diálogos, gavetas, toasts), sempre como anel de 1 px mais sustentação e ambiente. A opção marcada de um filtro em trilho usa só o anel.

## Components

Os primitivos vivem em `frontend/src/app/components/ui/` e seguem o vocabulário em português do projeto.

### Botão (`Botao`, `BotaoLink`)

- 36 px de altura (44 px em ponteiro de toque), cantos de 10 px, `scale(0.96)` ao pressionar.
- Cinco variantes: primário, secundário, discreto, perigo e perigo discreto. Uma única primária por visão.
- Carregando: spinner no lugar do conteúdo, largura preservada, `aria-busy`.

### Campo (`Campo`, `Entrada`, `Selecao`, `AreaTexto`)

- Rótulo sempre visível e associado; dica e erro por `aria-describedby`; "opcional" indicado em texto.
- 36 px de altura, cantos de 10 px, borda `line-strong`, foco com anel verde sem deslocar layout.
- Filtros de escolha única (`GrupoSegmentado`) são um trilho em pílula; a opção marcada sobe como pastilha clara.
- Busca de paciente por servidor (`SeletorPaciente`, combobox), sem limite fixo de itens.

### Marca de estado (`MarcaEstado`, `EstadoRotulado`)

Oito formas SVG, todas o mesmo círculo de 16 px em um ponto do preenchimento: contorno com ponto (agendada), metade preenchida (aguardando), cheio (confirmada), cheio com check (realizada), contorno riscado (cancelada), contorno com seta (remarcada), cheio com x (não compareceu) e ponto pequeno. `EstadoRotulado` mostra a marca e o rótulo numa pílula tingida pelo tom (`*-wash` com texto `*-ink`). Cada estado de domínio mapeia forma + tom + rótulo em `*.rotulos.ts` da própria feature. A cor nunca é o único portador de significado.

### Marca (`Marca`, `SimboloMarca`)

Azulejo musgo com semicírculo em duas metades (porcelana e ocre) e a palavra "psique" em Jost. As cores vêm dos tokens `--marca-*`, então a marca acompanha o tema. A fonte da verdade do símbolo é `frontend/public/favicon.svg`; os ícones raster saem dele.

### Acesso ao consultório

O login estende a identidade existente: cabeçalho comum com marca e menu de aparência (claro, escuro e sistema), fotografia à esquerda e formulário à direita em proporção 7:5 a partir de 1024 px, com largura total limitada a 1600 px. Abaixo de 1024 px, a foto é ocultada: cabeçalho compacto, formulário em coluna única e rodapé no fluxo da página. Os campos ficam próximos do topo, com rolagem natural em telas baixas e com o teclado aberto. A foto fica sem texto sobreposto; origem e licença em `docs/04-ux/imagens.md`.

O formulário tem largura máxima de 384 px, título "Acessar meu consultório" em Jost (32 px, 40 px a partir de 640 px), campos e ação principal de 48 px. Recuperação de senha, cadastro e links legais têm alvos de pelo menos 44 px. Informações legais ficam no rodapé. Sem slogans, métricas ou marcas de estado usadas como decoração.

Na chegada, as duas metades da marca se encontram em 280 ms; a foto abre por `clip-path` e opacidade em 300 ms; cabeçalho, formulário e convite de cadastro entram em 220 ms com intervalos de 40 ms. A seta de "Entrar" se desloca em 150 ms no foco ou no hover com ponteiro fino. Usa-se o `--ease-out` existente, sem repetição e sem impedir a digitação; `prefers-reduced-motion` remove esses movimentos. As telas internas seguem a mesma linha, em escala menor (ver "Movimento das telas internas").

### Tabela e lista (`Tabela`, `ListaRegulada`)

- Painel claro de cantos de 14 px, cabeçalho em tom de linho, linhas separadas por régua, linha inteira clicável por um único link.
- Colunas secundárias somem por breakpoint (`ocultarAte`); no celular nada força rolagem horizontal (verificado nos testes de ponta a ponta).

### Painel (`Painel`, `Secao painel`, `PainelVazio`)

- Superfície `surface` com régua `line` e cantos de 14 px, sem sombra; padding de 16 px (20 px a partir de 640 px).
- `Secao painel` envolve só o conteúdo; o título da seção fica fora, sobre o linho.
- `Painel destaque` (fundo `accent-wash`, régua `accent-line`) é reservado a uma coisa por tela: a próxima sessão no Hoje.
- Totais (financeiro, mês em números) são uma régua de células no mesmo painel, nunca cartões soltos; no celular viram linhas rótulo/valor.

### Busca global

- `BuscaGlobal`: diálogo no topo (12% da altura), 36 rem de largura, cantos de 20 px; combobox com lista de opções, setas e Enter. Entra com opacidade e escala 0,98 em 160 ms.

### Sobreposições (`Dialogo`, `Gaveta`, `Confirmacao`, `MenuAcoes`, `Dica`)

Base UI como camada sem estilo, com foco preso, `Esc`, retorno de foco e portal. Gaveta lateral para criar e editar; diálogo de confirmação com texto da consequência para ações destrutivas.

### Navegação

Trilho lateral recolhível (desktop, `lg+`) com grupos Rotina, Comunicação, Clínico e Consultório. No celular, barra inferior com quatro destinos mais "Mais". Configurações é um hub com subnavegação lateral (faixa rolável no celular). A marca e o sino de notificações ficam no topo; o tema é escolhido no menu do usuário.

### Feedback

- Toasts (Sonner) para confirmação transitória; avisos em linha para erro que bloqueia.
- Todo carregamento usa esqueleto na forma do conteúdo; todo vazio explica o próximo passo num azulejo ainda por preencher; todo erro oferece "Tentar novamente".

### Código de confirmação (`EntradaCodigo`)

Seis casas sobre um único `<input>` real (`one-time-code`), então colar, preencher automaticamente e ler com leitor de tela funcionam como num campo comum. Cada dígito assenta em 160 ms; a casa ativa mostra um cursor que pisca. O sexto dígito envia sozinho. Código recusado: as casas ficam em tijolo, balançam uma vez (320 ms, `--ease-in-out`) e se esvaziam com o foco de volta. Código aceito pela API: as casas passam a pinho da esquerda para a direita, com 40 ms entre elas, e a marca de check anuncia "E-mail confirmado". Nunca se mostra sucesso antes da resposta da API.

### Movimento das telas internas

Vocabulário em `frontend/src/app/styles/movimento.css`, aplicado pelos primitivos, então toda tela nova herda:

| Onde | Movimento | Por quê |
| --- | --- | --- |
| `Pagina` (cabeçalho e seções) | sobe 4 px e aparece em 200 ms, cascata de 30 ms até o 4º bloco | evita a troca seca de tela |
| `ListaRegulada` | linhas sobem 4 px em 180 ms, cascata de 25 ms até a 5ª | mostra o que chegou |
| `Tabela` | aparece em 140 ms | substitui o esqueleto sem salto |
| `Abas` | sublinhado desliza por `clip-path` em 240 ms; painel aparece em 180 ms | indica para onde se foi |
| Erro de `Campo`, `Aviso`, detalhe expandido, `<details>` | desce 3 px e aparece em 180 ms | a mensagem nasce onde se lê |
| `EstadoVazio`, `EstadoErro` | o azulejo assenta (escala 0,94 → 1) e o texto vem 50 ms depois | estado novo, não erro de layout |
| Selo do sino | assenta a cada mudança de contagem | sinaliza a novidade |
| Barras da semana (Hoje) | crescem por `clip-path` até o valor na primeira pintura, 30 ms entre dias | leitura da carga da semana |
| Barra inferior do celular | pílula de `accent-wash` abre atrás do ícone ativo; o ícone afunda a 0,94 no toque | estado ativo além da cor, retorno ao toque |
| Troca de tema | um esmaecimento de 200 ms da tela inteira (View Transitions) | evita cada cor transicionar sozinha |

Ficam de fora, de propósito: a grade semanal da agenda (dado denso, consultado dezenas de vezes ao dia), recolher o trilho (moveria o layout da página inteira) e qualquer movimento ligado a atalho de teclado. Entradas usam preenchimento `backwards`, para não deixar `transform` que prenderia filhos `fixed`. Com movimento reduzido, nada se desloca: restam esmaecimentos de 120 ms; o balanço, o cursor que pisca e as cascatas somem.

## Do's and Don'ts

### Do:

- **Do** usar padrões familiares, rótulos diretos e os quatro estados: carregando, vazio, erro e sucesso.
- **Do** mostrar o estado como marca + texto, em pílula.
- **Do** agrupar o que importa num painel claro (`Secao painel`, `Painel`), nunca numa grade de cartões iguais.
- **Do** manter dados administrativos e clínicos visualmente separados e nunca expor conteúdo clínico em listas ou painéis.
- **Do** pensar na recepção tanto quanto no psicólogo: a mesma tela é lida por quem agenda e por quem atende.
- **Do** manter alvos de toque de 44 px, foco visível, navegação completa por teclado e `prefers-reduced-motion`.
- **Do** escrever rótulos em português do Brasil com acentuação correta.

### Don't:

- **Don't** usar ilustrações, padrões vibrantes ou azul saturado; a ferramenta é calma. A única imagem é a foto de ambiente do login: sem pessoas, tons neutros e quentes, sem texto sobre ela.
- **Don't** criar aparência de landing page, métricas heroicas ou grade de cartões idênticos.
- **Don't** usar faixas laterais coloridas, texto em gradiente, glassmorphism ou ciano neon.
- **Don't** animar propriedades de layout (largura, altura, margem); use transform, opacidade, `clip-path` ou grid.
- **Don't** usar mais de uma ação primária preenchida na mesma visão.
- **Don't** sugerir multi-clínica, troca de profissional ou painel compartilhado.
- **Don't** simular sucesso no cliente: o resultado de toda operação vem da API.
