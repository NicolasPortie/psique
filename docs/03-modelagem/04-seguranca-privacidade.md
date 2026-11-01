# Seguranca, Privacidade e Criptografia

## Objetivo

Este documento define regras de seguranca para dados sensiveis da Psique.

O sistema lida com dados pessoais, dados de saude, prontuario, documentos psicologicos e conversas administrativas.

## Principios

- dados clinicos sao sensiveis;
- dados devem ser escopados por `ContaPsicologoId`;
- senhas devem ser armazenadas apenas como hash;
- tokens e secrets devem ser criptografados;
- campos clinicos devem ser criptografados em repouso;
- trafego deve usar HTTPS/TLS;
- logs nao devem conter segredos ou conteudo clinico aberto;
- backups devem ser criptografados;
- exportacoes devem exigir permissao elevada.

## Confirmação de e-mail no cadastro

- Antes de criar uma conta, enviar um código aleatório de seis números por e-mail transacional (SMTP).
- `POST /api/v1/autenticacao/cadastro/codigo` devolve apenas identificador opaco, validade e intervalo de reenvio. Nunca devolver o código na API nem registrá-lo em logs.
- `POST /api/v1/autenticacao/cadastro/confirmar-email` valida o código e emite comprovante opaco vinculado ao endereço normalizado. Nenhuma dessas operações cria conta ou sessão.
- Código válido por 10 minutos, no máximo cinco tentativas; confirmação correta consome o código atomicamente. Reenviar invalida o identificador anterior informado para o mesmo endereço.
- Intervalo mínimo de 60 segundos; no máximo cinco envios por endereço/hora e vinte por IP/hora, além do limitador de rotas públicas.
- Guardar somente HMAC do código com identificador aleatório, em Redis com expiração; fallback em memória apenas em Development sem Redis. Comparação, desconto de tentativa e consumo são atômicos.
- O comprovante dura 30 minutos, vincula-se ao e-mail e é consumido uma única vez no serviço de criação de conta. Solicitar diretamente o cadastro sem comprovante válido não cria dados duráveis.
- O comprovante é conferido antes de verificar se o e-mail já tem conta: sem ele, o cadastro responde o mesmo erro para e-mail novo ou existente e não serve para descobrir quem usa o sistema.
- Senhas e confirmação de senha não entram no estado temporário da verificação. A confirmação de senha é uma prevenção de erro de preenchimento no cliente; a política de senha permanece no servidor.
- SMTP desativado ou falha de entrega retorna erro, sem anunciar envio. A única exceção é o ambiente Development: sem SMTP, o código passa a ser `123456` e a API avisa no log, para o cadastro poder ser exercitado localmente. Fora de Development nunca há código fixo.

Configuração do transporte: `Email:Ativo`, `ServidorSmtp`, `Porta`, `UsarSsl`, `Usuario`, `Senha`, `EmailRemetente` e `NomeRemetente`. Credenciais devem vir de secrets ou variáveis de ambiente, nunca de arquivos versionados. Não habilitar `Ativo` antes de configurar o transporte.

## Senhas

Campo:

```txt
Usuarios.SenhaHash
```

Regras:

- usar Argon2id, bcrypt ou PBKDF2 com parametros fortes;
- nunca salvar senha em texto puro;
- nunca registrar senha em log;
- nunca retornar hash pela API.

## Cifra em camadas

Nenhum dado pessoal, clínico ou administrativo escrito por gente fica legível no banco. São três camadas:

1. **Cifra feita pelos serviços** (`IProtecaoDadosService`, propósito `Psique.DadosSensiveis.v1`): os campos
   clínicos e de contato listados em "Campos criptografados" abaixo. O valor gravado começa com `CfDJ8`.
2. **Cifra de coluna transparente** (`Data/Cifra/CifraDeColuna.cs`): toda propriedade marcada com `[Cifrado]` passa
   por um conversor do EF Core e vai ao banco como `cf1:` + texto protegido pela Data Protection (AES-256-CBC +
   HMAC-SHA256, propósito `Psique.Colunas.v1`). Datas cifradas (ex.: `Pacientes.DataNascimento`) viram texto
   `yyyy-MM-dd` antes da cifra. Colunas cifradas são `nvarchar(max)`; o limite de tamanho fica na validação do serviço.
3. **TDE no SQL Server** (`backend/sql/habilitar-tde.sql`): AES-256 sobre os arquivos de dados, log, tempdb e backups.
   Cobre também o que precisa ficar consultável pelo banco (datas e horários de consulta, valores, estados, ids).

Na subida, `MigracaoCifraColunas` cifra o que ainda estiver em texto aberto nas duas primeiras camadas e preenche
hashes de busca que faltarem. É idempotente.

Colunas com `[Cifrado]`:

```txt
Pacientes.NomeCompleto, Pacientes.DataNascimento
Usuarios.NomeCompleto, Usuarios.Email, Usuarios.Telefone
ContasPsicologo.NomeConta
DadosProfissionaisPsicologo.NomeProfissional, Bio, EmailProfissional, TelefoneProfissional
Consultas.LinkOnline, EnderecoAtendimento, ObservacaoAdministrativa, JustificativaCancelamento
HistoricosConsulta.Descricao
Pagamentos.Observacao
ListaEspera.ObservacaoAdministrativa
Lembretes.Assunto, Mensagem, ErroEnvio
Notificacoes.Titulo, Mensagem
ModelosMensagem.Assunto, Conteudo
TiposAtendimento.Descricao
BloqueiosAgenda.Titulo, Descricao
ConversasWhatsApp.Telefone, NomeContato, MotivoAtendimentoHumano
MensagensWhatsApp.ErroEnvio
EventosBridgeWhatsApp.ErroProcessamento
IntegracoesWhatsApp.NumeroWhatsApp, UrlBaseBridge, UltimoErro
DocumentosProntuario.Titulo, Finalidade, Solicitante
VersoesDocumentoProntuario.MotivoAlteracao
ProtocolosEntregaDocumento.Destinatario, Observacao
RetificacoesRegistroSessao.ValorAnterior, ValorNovo, Motivo
LogsAuditoria.Descricao, Ip, UserAgent
AssinaturasConta.MotivoCancelamento
```

Ficam em texto (protegidos só pelo TDE), porque o banco precisa filtrar, ordenar ou somar por eles: ids e chaves
estrangeiras, datas e horários, valores, estados e enums, nomes de tipos de atendimento, de perfis de acesso e de
modelos de mensagem, ids externos do WhatsApp e hashes.

Consequências no código:

- busca e ordem por nome de paciente ou usuário acontecem na memória da API, depois de decifrar, sempre já restritas
  a uma `ContaPsicologoId`;
- e-mail de login e telefone de conversa são localizados pelo hash (`Usuarios.EmailHash`, `ConversasWhatsApp.TelefoneHash`),
  preenchido pelo `AppDbContext` ao gravar;
- nunca filtrar com `Contains`, `StartsWith` ou `OrderBy` no SQL sobre coluna cifrada: o resultado sai errado sem erro.
  Para campo novo com dado de pessoa, marcar com `[Cifrado]` e criar a migração.

## Campos Criptografados

Clinicos:

```txt
Prontuarios.ObservacaoGeral
Prontuarios.ResumoAcompanhamento
Prontuarios.ObjetivosAcompanhamento
Prontuarios.PlanoAcompanhamento
Prontuarios.HipotesesClinicas
RegistrosSessao.ObservacoesClinicas
RegistrosSessao.Evolucao
RegistrosSessao.Encaminhamentos
EvolucoesProntuario.Conteudo
EvolucoesProntuario.Resumo
DocumentosProntuario.Conteudo
VersoesDocumentoProntuario.Conteudo
```

Pessoais:

```txt
Pacientes.Cpf
Pacientes.ObservacaoAdministrativa
ContatosPaciente.Valor
DadosProfissionaisPsicologo.Crp
```

Integracoes:

```txt
IntegracoesWhatsApp.TokenCriptografado
MensagensWhatsApp.Conteudo
EventosBridgeWhatsApp.PayloadJson
SessoesBot.DadosTemporariosJson
```

Auditoria:

```txt
LogsAuditoria.DadosAntesJson
LogsAuditoria.DadosDepoisJson
```

## Hashes de Busca

Campos:

```txt
Pacientes.CpfHash
ContatosPaciente.ValorHash
DadosProfissionaisPsicologo.CrpHash
EventosBridgeWhatsApp.PayloadHash
RefreshTokens.TokenHash
Usuarios.EmailHash
ConversasWhatsApp.TelefoneHash
```

Regras:

- normalizar antes do hash;
- CPF sem pontuacao;
- telefone com DDI/DDD normalizados;
- hash serve para busca/comparacao, nao para recuperar valor.

## Mascaramento

Campos:

```txt
CpfMascarado
CrpMascarado
ValorMascarado
TelefoneMascarado
TokenMascarado
```

Regras:

- listagens devem evitar documento completo;
- logs devem preferir valores mascarados;
- token nunca deve ser exibido completo.

## Chaves e Secrets

Regras:

- chave de criptografia nao fica hardcoded;
- usar secret manager ou variavel protegida;
- planejar rotacao futura de chaves;
- acesso a secrets restrito ao back-end.

Chaves da Data Protection (decifram as camadas 1 e 2):

- ficam no Redis (`Psique:protecao-dados:chaves`) ou, em Development sem Redis, em `.psique-dev/`;
- fora de Development a API não sobe sem `Seguranca:CertificadoChavesArquivo` (PFX) e `Seguranca:CertificadoChavesSenha`:
  o certificado cifra as próprias chaves, então quem lê o Redis não lê os dados;
- **perder as chaves é perder os dados**: fazer backup do chaveiro e do PFX junto com cada backup do banco, guardados
  em cofre separado;
- chaves antigas expiram para novas cifras, mas continuam no chaveiro para decifrar o que já foi gravado; nunca apagar;
- `Seguranca:ChaveHashDados` alimenta os hashes de busca; trocá-la exige recalcular todos os hashes.

TDE (camada 3):

- requer SQL Server 2019+ Standard/Enterprise/Developer ou Azure SQL (ligado por padrão); o LocalDB do desenvolvimento
  é Express e não tem TDE;
- no Docker Compose, o job `cifra-banco` roda `backend/sql/habilitar-tde.sql` depois das migrações
  (`TDE_SENHA_CHAVE_MESTRA` e `TDE_SENHA_BACKUP` no `.env`);
- o script grava `PsiqueTde.cer` e `PsiqueTde.pvk` na primeira execução: copie-os para o cofre; sem eles nenhum
  backup é restaurável em outro servidor.

## Perfis e Permissoes

A equipe da conta tem um perfil: Psicologo (dono da conta), Administrador, Recepcionista ou
Personalizado. O perfil Personalizado usa um `PerfilAcesso` criado pela propria conta, com nome e
um conjunto de permissoes administrativas.

- Catalogo: Agenda, Pacientes, Financeiro, Mensagens (lembretes e modelos), Conversas,
  IntegracaoWhatsApp, Relatorios, Auditoria e Equipe. Cada permissao tem uma politica de
  autorizacao no back-end (`Politicas`).
- Conteudo clinico (prontuario, sessoes, documentos psicologicos) nunca entra no catalogo: segue
  exclusivo do perfil Psicologo (`AcessoClinico`).
- Administrador tem todas as permissoes; Recepcionista tem todas, menos Auditoria, Equipe e
  IntegracaoWhatsApp.
- As permissoes sao lidas do banco a cada requisicao (`ValidacaoSessaoJwtService`) e viajam como
  claims `permissao` montadas pelo servidor, nunca emitidas no token. Mudar um perfil vale na
  proxima acao da pessoa.
- Ninguem concede nem gerencia mais acesso do que tem: criar ou editar perfil, dar perfil a alguem,
  redefinir senha e alterar status exigem que as permissoes do alvo sejam um subconjunto das de
  quem pede. So o Psicologo gerencia o Psicologo.
- O front-end esconde o que a pessoa nao pode abrir, mas isso nunca substitui a autorizacao do servidor.
- A permissão Relatorios não concede os dados de outras áreas: relatórios financeiros também exigem Financeiro; cadastro exige Pacientes; agenda exige Agenda; histórico por paciente exige Agenda e Pacientes. O catálogo mostra somente os relatórios permitidos, e a geração repete a autorização.

## Auditoria

Logs devem registrar:

- conta;
- usuario;
- acao;
- entidade;
- entidade afetada;
- data/hora;
- IP;
- user agent.

Logs nao devem registrar:

- senha;
- token;
- chave;
- conteudo clinico completo em texto aberto.

## Backups e Exportacoes

Regras:

- backups criptografados (com TDE ligado, todo backup já sai cifrado pelo certificado `PsiqueTde`);
- exportacoes de prontuario/documentos exigem permissao clinica;
- exportacao registra auditoria;
- arquivos temporarios expiram;
- exclusao de conta nao deve apagar prontuario sem regra clara de guarda.

## Redis e Autenticacao

Redis deve armazenar apenas estado temporario com expiracao definida:

- contadores de tentativas de login;
- bloqueios temporarios;
- tokens de recuperacao armazenados somente como hash;
- refresh tokens armazenados somente como hash;
- indices de revogacao de sessoes;
- chaveiro compartilhado de Data Protection.

Regras:

- nunca armazenar senha no Redis;
- nunca armazenar token bruto no Redis;
- usar TTL em todas as chaves de autenticacao;
- tokens de recuperacao e refresh tokens devem ser de uso unico ou rotacionados;
- redefinir senha deve revogar sessoes anteriores;
- access tokens incluem `VersaoAutenticacao` do usuario; redefinicao de senha
  e mudanca de status incrementam essa versao e revogam refresh tokens;
- a validacao de cada JWT confirma usuario e conta ativos, perfil atual e versao;
- tokens anteriores a essa regra exigem renovacao de sessao;
- Redis deve exigir autenticacao e conexao protegida em producao;
- chaves Redis nao devem conter e-mail, CRP, CPF ou outro dado pessoal em texto aberto.

Para desenvolvimento local, o estado temporario de autenticacao pode rodar em memoria quando Redis nao estiver configurado. Esse modo nao e distribuido, nao sobrevive a reinicio da API e existe apenas para facilitar o desenvolvimento sem infraestrutura local adicional.

## MFA

MFA/2FA e altamente recomendado para psicologos.

Pode ser fase futura, mas deve ser previsto na arquitetura de autenticacao.

## Camada HTTP

Regras aplicadas na API e no servidor do front-end:

- respostas da API com `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` e CSP `default-src 'none'`;
- CSP do front sem scripts embutidos, com `connect-src 'self'` e `frame-ancestors 'none'`;
- HSTS na API fora de Development e `Strict-Transport-Security` no terminador TLS em producao;
- limite de 60 requisicoes por minuto por IP nos endpoints sem sessao (cadastro, login, recuperacao e webhook do Bridge), alem das travas por conta e por e-mail;
- IP do cliente por `X-Forwarded-For` somente quando `Rede:ConfiarEmProxyReverso` esta ligado e a origem e uma rede privada;
- endpoint de saude anonimo sem detalhes da infraestrutura fora de Development;
- listagem de consultas com periodo maximo de 366 dias e filtro por paciente no servidor;
- troca de senha com a senha atual, limite de tentativas, revogacao das demais sessoes e auditoria (`AlteracaoSenha`).

### Perfil profissional após a criação do acesso

O cadastro confirmado cria conta e usuário sem nome ou CRP fictícios. Nome completo e CRP são complementados após o acesso, em diálogo opcional ou em Dados profissionais. `ContaPsicologo.PerfilProfissionalAdiadoEm` guarda o adiamento no servidor, com autorização e isolamento por conta; ele não indica perfil concluído. A situação é derivada do registro profissional válido do titular. A finalização de documentos exige essa identificação no back-end; criar rascunhos permanece permitido.
