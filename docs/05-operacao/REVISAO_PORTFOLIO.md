# Revisão da apresentação do projeto

Revisão de 1º de novembro de 2026. O objetivo desta entrega é um repositório de
portfólio reproduzível localmente, com demonstrações baseadas no sistema real.

## Cruzamento entre apresentação e implementação

| Descrição no README | Implementação e verificação |
| --- | --- |
| Agenda, consultas, realização e financeiro | Features `agenda`, `pacientes`, `sessoes`, `pagamentos`; jornadas E2E de consultório e rotina clínica |
| Separação da recepção e prontuário | Autorização da API, permissões de interface e cenário E2E que recusa acesso clínico da recepção |
| Sessão entre abas | BroadcastChannel, Web Locks, proteção contra respostas antigas e cenário E2E de troca de identidade/saída |
| Mensalidades, recibos e exportação | Controllers/Services de pagamentos, testes de fuso, transição de estado, exportação e concorrência SQL |
| Evolution API v2 | Cliente HTTP, fila durável, webhook, correlação de confirmações e testes de contrato; sem alegação de pareamento real |
| E-mail | SMTP com STARTTLS e marcador persistido para evitar repetição automática de tentativas incertas |
| Verificação | Resultados locais vinculados aos relatórios; CI apresentado separadamente pelo selo do Actions |
| Produção | Configuração disponível, sem afirmar que domínio público, SMTP real ou restauração integral com TDE foram homologados |

## Imagens e GIFs

As oito imagens PNG e os quatro GIFs de `docs/screenshots/` foram regenerados
com a versão atual, pela conta fictícia do seed de demonstração. Foram conferidos
os arquivos, a decodificação dos GIFs e quadros distribuídos ao longo de cada
animação. A tela de acesso aguarda o formulário e a imagem carregarem; o início
do GIF mobile aguarda o painel, evitando publicar somente o esqueleto inicial.

Os GIFs executam ações reais na conta local: agendamento, realização, recebimento,
registro de sessão, navegação móvel e recusa de acesso clínico da recepção.
Datas e totais variam conforme o momento da gravação e o seed utilizado.
Credenciais vistas na demonstração são exclusivamente da conta fictícia local.

Reprodução: `npm run seed:demo -- --renovar`, `npm run gravar:demo` e
`node scripts/capturar-telas.mjs`, em `frontend/`, com a API e o Vite ativos.
O roteiro de GIFs exige ffmpeg. O script de imagens verifica falhas de página e
aguarda conteúdo, fontes e carregamento antes de capturar.

Os documentos de planejamento descrevem também fases futuras; não constituem
uma lista de funcionalidades já entregues. Planos comerciais, cobrança SaaS,
portal do paciente e integrações financeiras continuam identificados como futuro.

## Publicação

Na execução E2E em Docker no GitHub, o runner UTC expôs uma dependência do fuso
da máquina nos horários dos cenários. O Playwright passou a definir explicitamente
`America/Sao_Paulo` tanto no processo Node dos helpers quanto no navegador,
correspondendo ao fuso das contas semeadas. Isso mantém os horários enviados pela
API e preenchidos na interface consistentes, sem relaxar as regras de expediente
ou remover os cenários que falharam.

A auditoria executada no Actions identificou alertas de alta severidade nas
versões anteriores de Zod e undici. O lockfile foi atualizado sem `--force`;
lint, build e os 153 testes do front-end passaram novamente. A auditoria permanece
ativa no CI. Ainda existem alertas moderados transitivos no minimatch utilizado
pelas ferramentas ESLint/TypeScript ESLint. A correção publicada do minimatch
está em outra versão principal e não foi imposta por override a consumidores de
APIs antigas. Não afirmar que o repositório possui zero alertas de dependências.
Referência: [aviso do minimatch](https://github.com/advisories/GHSA-g3hv-75wg-m249).

A publicação não inclui `.env`, certificados, bancos, logs, artefatos de teste,
dependências instaladas ou o projeto independente `video-divulgacao/`.
O vídeo independente foi preservado no workspace; os GIFs e imagens do README
estão incluídos na entrega do sistema.
