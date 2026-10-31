# UX da Psique

A referência visual é o código e o [`DESIGN.md`](../../DESIGN.md). Não há mais mockups externos como fonte de verdade. A decisão está em [`docs/adr/0001-reestruturacao-do-frontend.md`](../adr/0001-reestruturacao-do-frontend.md).

## Mapa de telas

| Grupo no menu | Tela | Rota | Perfis |
| --- | --- | --- | --- |
| Rotina | Hoje (painel) | `/painel` | todos |
| Rotina | Agenda (semana, dia no celular) | `/agenda` | todos |
| Rotina | Nova consulta | `/consultas/nova` | todos |
| Rotina | Detalhe da consulta | `/consultas/:id` | todos |
| Rotina | Pacientes, ficha (abas resumo, consultas, financeiro, documentos), cadastro | `/pacientes`, `/pacientes/:id?aba=`, `/pacientes/novo`, `/pacientes/:id/editar` | todos (financeiro com permissão; documentos só Psicólogo) |
| Rotina | Financeiro | `/pagamentos` | todos |
| Rotina | Recibo de pagamento recebido | `/pagamentos/:id/recibo` | Financeiro |
| Rotina | Recibo do mês do paciente | `/pacientes/:id/recibo-mensal?mes=AAAA-MM` | Financeiro |
| Rotina | Lista de espera | `/lista-espera` | todos |
| Comunicação | Conversas do WhatsApp | `/conversas` | todos |
| Comunicação | Lembretes | `/lembretes` | todos |
| Clínico | Prontuário e sessões | `/pacientes/:id/prontuario` | Psicólogo |
| Clínico | Documentos e detalhe | `/documentos`, `/documentos/:id` | Psicólogo |
| Consultório | Relatórios administrativos | `/relatorios` | todos |
| Consultório | Notificações | `/notificacoes` | todos |
| Consultório | Configurações (hub) | `/configuracoes/{conta,agenda,profissional,equipe,auditoria,mensagens,whatsapp}` | gestão (Psicólogo e Administrador); conta, agenda e mensagens para todos |
| Público | Termos de uso e política de privacidade | `/termos`, `/privacidade` | sem login |

A recepção nunca vê conteúdo clínico: prontuário e documentos mostram "acesso restrito", e o servidor recusa a requisição.

## Padrões de tela

- **Cabeçalho**: título único, descrição curta e, no máximo, uma ação primária preenchida.
- **Lista**: filtros na URL, tabela em régua no desktop e linhas empilhadas no celular, paginação só quando há mais de uma página.
- **Criar/editar registro pequeno**: gaveta lateral. Formulário longo: página própria com seções.
- **Ação destrutiva**: diálogo de confirmação que descreve a consequência.
- **Estados**: toda tela de dados define carregamento (esqueleto), vazio (próximo passo), erro (nova tentativa) e sucesso (toast).
- **Estado de entidade**: marca desenhada + texto, vindos de `*.rotulos.ts`.

## Rotina do consultório

- **Hoje**: saudação como título; em destaque, a próxima sessão (mesmo de outro dia, com "Entrar na sessão" quando é online e começa em até 30 minutos); a agenda do dia; e "Sessões para fechar", as consultas que já terminaram sem desfecho, com Realizada/Faltou em um toque.
- **Agenda**: no sábado ou domingo sem atendimento, abre a semana seguinte e avisa.
- **Nova consulta**: horários livres do dia em pílulas; "Repetir" (não repetir, toda semana, a cada 15 dias) com a quantidade de consultas; datas ocupadas ficam de fora e o toast lista quais.
- **Consulta**: selo "Série · mais N consultas"; "Cancelar esta e as próximas"; sessão que já passou troca "Confirmar" por "Marcar como realizada" e "Faltou".
- **Ficha do paciente**: é o centro do paciente. Resumo (próxima consulta, sessões no último ano, faltas, valor em aberto), aviso de menor sem responsável e abas Resumo, Consultas, Financeiro e Documentos. O prontuário fica a um botão, só para o psicólogo.
- **Financeiro**: navegação por mês (ou todo o período), busca por paciente, totais recebido/a receber/vencido, filtro "Vencidos", "Receber" com a forma de pagamento e "Emitir recibo" para o que foi recebido.
- **Agenda (desktop)**: visões Dia, Semana e Mês (`?visao=`); arrastar uma consulta em aberto para outro horário abre a confirmação de remarcação com data e hora preenchidas; consultas de série mostram o ícone de repetição.
- **Busca global**: o campo "Buscar paciente" do trilho (a lupa no celular), sem atalho de teclado; pacientes ativos pelo nome, telefone ou e-mail, áreas do menu e ações comuns.
- **Hoje**: "Primeiros passos" para a conta nova (dispensável) e "Aniversários da semana".
- **Pacientes**: filtro "Só quem não vem há mais de 30 dias"; no celular, a próxima consulta aparece sob o nome.
- **Ficha**: forma de cobrança (por sessão ou mensalidade), consentimento LGPD, exportação dos dados do paciente e recibo do mês.
- **Prontuário**: resumo (sessões, última sessão, plano terapêutico atual) e linha do tempo de sessões e evoluções com busca local sem acentos; o registro de sessão oferece roteiros (Livre, Estruturado, DAP) que só preenchem campos vazios.
- **Consulta**: "Declaração de comparecimento" em um clique (rascunho para revisar) e, ao cancelar um horário futuro, aviso de quem espera na lista de espera, que já leva data e hora para o agendamento.
- **Financeiro**: "Gerar mensalidades" e "Exportar recebimentos" no menu do cabeçalho.
- **Relatórios**: o mês em números (sessões, faltas, cancelamentos, recebido, a receber) antes dos relatórios oficiais para imprimir.

## Tela de acesso

O login é a referência inicial da evolução visual, dentro da identidade de porcelana, grafite e verde-musgo, com Jost e Atkinson. O cabeçalho reúne marca e menu de aparência (claro, escuro e sistema). No desktop, foto à esquerda e formulário à direita ocupam uma grade 7:5; no celular, a foto é ocultada e o formulário vem logo após o cabeçalho compacto, com rodapé no fluxo e rolagem natural em telas baixas. A largura total é limitada a 1600 px e o formulário a 384 px.

O título é "Acessar meu consultório"; campos e ação principal têm 48 px, e links de recuperação, cadastro e informações legais têm alvos de pelo menos 44 px. A foto não recebe texto, slogans ou exemplos de estados. Os links legais ficam no rodapé. Validação, carregamento, erros da API e mostrar/ocultar senha continuam disponíveis.

A entrada anima uma vez as duas metades da marca, o recorte da foto e os grupos do formulário, sem bloquear o uso. Com movimento reduzido, esses elementos ficam estáticos. Durações e limites estão no [`DESIGN.md`](../../DESIGN.md); origem e licença da imagem estão em [`imagens.md`](imagens.md). As telas internas seguem a ADR 0003: painéis claros, cantos macios e estados em pílula.

## Cadastro do psicólogo

O cadastro tem duas etapas na mesma rota `/cadastro`:

1. **Acesso:** e-mail, senha, confirmação de senha e aceite dos termos, com opção de mostrar/ocultar cada senha. Validar o preenchimento antes de solicitar o código.
2. **E-mail:** código de seis números, com preenchimento automático/colagem, correção do endereço e reenvio após 60 segundos. **Confirmar e criar conta** valida o código pela API e cria o acesso, sem pedir nome ou CRP.

Não exibir numeração, barra de progresso ou antecipação da confirmação de e-mail no formulário inicial. Após solicitar o código, título e formulário mudam juntos com entrada de 180 ms por opacidade e deslocamento; com movimento reduzido ficam estáticos. No celular, coluna única, campos de 48 px e rolagem natural, sem foto nem ações fixas sobre o teclado.

O preenchimento e o comprovante de e-mail ficam apenas na memória da página. Voltar preserva os campos; trocar o endereço exige nova confirmação. Sair ou recarregar descarta o preenchimento. Erros da API preservam os campos e nunca simulam criação bem-sucedida.

Após criar a conta, a sessão é autenticada e o painel abre. Um diálogo opcional pede somente **nome completo e CRP**, com **Salvar e continuar** e **Preencher depois**. Fechar o diálogo também adia. O adiamento é persistido na conta pelo servidor: não repetir o convite automaticamente nas próximas entradas. Se a gravação falhar, liberar a navegação e informar que a preferência não foi salva.

Enquanto o perfil estiver incompleto, painel e documentos mostram um lembrete discreto com **Completar perfil**. Também é possível retomar em Configurações → Dados profissionais. A exploração e os rascunhos continuam disponíveis; o back-end exige nome e CRP antes de finalizar documentos. Não criar dados profissionais fictícios. A validação do formato do CRP não equivale a consultar o registro no conselho. CPF, telefone, endereço e cobrança não são pedidos nesse convite inicial.

O envio depende de SMTP configurado. Sem envio disponível, mostrar erro e permanecer na etapa de acesso; nunca anunciar envio ou confirmação fictícios. Regras de expiração e uso único estão em `docs/03-modelagem/04-seguranca-privacidade.md`.

## Verificação visual

Para qualquer mudança de interface:

1. `npm run contraste` sem falhas se `tokens.css` mudou.
2. Conferir a tela em 390 px e 1440 px, nos temas claro e escuro.
3. Abrir gavetas, diálogos e menus da tela alterada.
4. Console do navegador sem erros.
5. `npm run test:e2e` com a API e o front rodando. A suíte inclui a ausência de rolagem horizontal em 390 px e 320 px e a varredura de acessibilidade (axe, WCAG A e AA) nos temas claro e escuro, com gavetas abertas.

Imagens: ver [`imagens.md`](imagens.md) (única foto, origem e como trocar).

Dados de demonstração para conferir telas: `npm run seed:demo` (somente com API local). Para a demonstração não envelhecer, `npm run seed:demo -- --renovar` agenda a grade de novo a partir de hoje.

Ícones do app instalável (PWA) saem de `public/favicon.svg` com `node scripts/gerar-icones.mjs`; o manifesto é `public/manifest.webmanifest`.
