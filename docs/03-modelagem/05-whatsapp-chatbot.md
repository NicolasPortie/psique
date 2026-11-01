# WhatsApp, Bridge e Chatbot Administrativo

## Objetivo

Este documento define a modelagem e as regras do WhatsApp e do chatbot administrativo.

O chatbot nao realiza atendimento psicologico.

## Papel do Chatbot

Pode fazer:

- agendar consulta;
- remarcar consulta;
- cancelar consulta;
- confirmar presenca;
- consultar horarios;
- enviar lembretes;
- responder duvidas administrativas;
- encaminhar para humano.

Nao pode fazer:

- terapia;
- diagnostico;
- interpretacao de sintomas;
- orientacao psicologica;
- gerar laudo ou relatorio;
- coletar relato intimo sem necessidade;
- conduzir crise clinica sem humano.

## Fluxo

```txt
Paciente no WhatsApp
  -> Bridge WhatsApp
  -> API da Psique
  -> Identificacao da ContaPsicologo
  -> Motor administrativo do bot
  -> Regras de agenda/consulta
  -> Bridge WhatsApp
  -> Paciente
```

## IntegracoesWhatsApp

O transporte implementado usa Evolution API v2, com instância e chave exclusivas
por conta. Veja a [decisão de entrega](../adr/0005-entrega-whatsapp-e-concorrencia.md)
e o [procedimento de conexão](../05-operacao/WHATSAPP_EVOLUTION.md).

Campos principais:

```txt
Id
ContaPsicologoId
Nome
Provedor
Instancia
UrlBaseBridge
TokenCriptografado
NumeroWhatsApp
Status
Ativo
CriadoEm
AtualizadoEm
```

Regras:

- token criptografado;
- pertence a uma conta;
- webhook deve identificar a conta pela integracao/token;
- nao expor token pela API.

## ConversasWhatsApp

Campos principais:

```txt
Id
ContaPsicologoId
IntegracaoWhatsAppId
PacienteId
Telefone
NomeContato
Status
UltimaMensagemEm
AtendimentoHumanoNecessario
MotivoAtendimentoHumano
CriadoEm
AtualizadoEm
```

Regras:

- pode existir sem paciente vinculado;
- se detectar mensagem sensivel, marcar atendimento humano;
- listagens nao devem expor conteudo clinico.

Passagem para a equipe (`Status = AtendimentoHumano`, `AtendimentoHumanoNecessario = true`):

- o paciente pede uma pessoa ("atendente", "falar com a equipe", "recepcao" e termos parecidos);
- o assistente nao entende duas mensagens seguidas;
- o pedido depende do cadastro e o paciente nao foi identificado pelo telefone;
- a agenda ainda nao foi configurada;
- a mensagem tem conteudo sensivel (resposta inclui CVV 188 e SAMU 192).

Em todos os casos o assistente responde ao paciente avisando a transicao, encerra as sessoes do bot,
avisa quem tem a permissao Conversas e registra auditoria. Enquanto a conversa esta com a equipe, o
assistente fica em silencio. A equipe pode devolver a conversa ao assistente
(`POST /whatsapp/conversas/{id}/devolver-ao-assistente`), o que limpa a marca e o motivo.

## MensagensWhatsApp

Campos principais:

```txt
Id
ContaPsicologoId
ConversaWhatsAppId
MensagemExternaId
LembreteId
EnvioIniciadoEm
Direcao
TipoMensagem
Conteudo
Status
EnviadaEm
RecebidaEm
LidaEm
ErroEnvio
CriadoEm
```

Regras:

- conteudo criptografado;
- evitar armazenar relato clinico desnecessario;
- usar `MensagemExternaId` para idempotencia;
- considerar mascaramento/resumo/descarte para conteudo sensivel.
- Mensagem Pendente não comprova envio: o lembrete vinculado permanece Agendado
  até o provedor aceitar a mensagem e devolver seu identificador.
- Enviada significa aceite do provedor; Entregue e Lida dependem de confirmação.
- Tentativas iniciadas sem confirmação exigem conferência antes de um novo envio;
  não são repetidas automaticamente, inclusive após reinício da API.
- Confirmações anteriores ao retorno do envio são preservadas e correlacionadas;
  eventos fora de ordem não fazem uma mensagem lida voltar a entregue.

## EventosBridgeWhatsApp

Campos principais:

```txt
Id
ContaPsicologoId
IntegracaoWhatsAppId
EventoExternoId
TipoEvento
PayloadHash
PayloadJson
Processado
ProcessadoEm
ErroProcessamento
CriadoEm
```

Regras:

- payload criptografado ou minimizado;
- processamento idempotente;
- falhas rastreadas;
- eventos pertencem a conta.

## SessoesBot

Campos principais:

```txt
Id
ContaPsicologoId
ConversaWhatsAppId
PacienteId
IntencaoAtual
EtapaAtual
DadosTemporariosJson
Status
ExpiraEm
CriadoEm
AtualizadoEm
```

Regras:

- guardar apenas dados administrativos temporarios;
- expirar sessoes antigas;
- nao armazenar dados clinicos detalhados.
