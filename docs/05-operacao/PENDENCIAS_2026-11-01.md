# Pendências verificadas após a primeira revisão

> Atualização da rodada seguinte: as pendências de implementação descritas abaixo
> foram corrigidas. Redis real, SMTP local, concorrência, restauração e HTTPS local
> foram exercitados. Consulte [CONCLUSAO_2026-11-01.md](CONCLUSAO_2026-11-01.md)
> para resultados atuais e dependências do ambiente real. O restante deste arquivo
> preserva o diagnóstico anterior; não representa o estado final do código.

## Corrigidas nesta rodada

- Endereço cifrado de paciente era passado diretamente ao transporte de e-mail.
  Agora o serviço decifra o endereço antes do envio.
- Reenvio manual não verificava consulta cancelada, remarcada, realizada, com
  falta ou já iniciada. Agora encerra esses lembretes como Ignorado.
- Era possível programar um lembrete para depois do início da consulta.
  A criação agora recusa esse horário sem persistir o lembrete.
- O processador enviava lembretes de contas inativas, bloqueadas ou canceladas.
  A seleção agora exige conta ativa.
- Webhook do WhatsApp aceitava token válido de conta desativada.
  Agora responde 403 antes de gravar ou processar o evento.
- Modelos e lembretes fixavam o fuso em UTC−3. Agora usam o fuso configurado do
  consultório, inclusive na prévia de modelos.

Reproduções anteriores às correções: 13 testes falharam para os problemas de
envio/autorização e um teste adicional falhou por data e hora incorretas. Foram
acrescentados 14 testes. A suíte completa do back-end passou com 207 testes.

## Pendências de implementação confirmadas

### Alta: entrega real das mensagens do WhatsApp

`ConversasWhatsAppService.EnviarMensagemAdministrativaAsync` apenas grava uma
mensagem de saída Pendente. Não foi encontrado consumidor de fila nem chamada
de transporte que envie essa mensagem ao Bridge. O retorno do webhook também
não devolve mensagens pendentes ao Bridge.

Além disso, `LembretesService.TentarEntregarWhatsAppAsync` marca o lembrete como
Enviado imediatamente após criar uma mensagem Pendente. Isso não comprova
entrega. Esta parte permanece aberta: exige definir o contrato com o Bridge,
vincular lembrete à mensagem, implementar envio e confirmação, e impedir
duplicação em tentativas e em múltiplas instâncias. Não considerar WhatsApp
pronto para operação real enquanto esse fluxo não estiver concluído.

### Alta: o teste de conexão não confirma autenticação nem entrega

`IntegracaoWhatsAppService.TestarConexaoAsync` considera HTTP 401 ou 404 em
`/health` como sucesso e marca a integração como Ativa. Isso comprova somente
alcance HTTP. Falta distinguir alcance do servidor, autenticação e disponibilidade
do transporte, de acordo com o contrato real do Bridge.

## Validações ainda necessárias

| Área | O que falta | Evidência ou impedimento |
| --- | --- | --- |
| Redis | Exercitar consumo e rotação, revogação, TTLs e concorrência com Redis real e mais de uma instância. | O Docker está instalado, mas o motor Linux não estava acessível nesta rodada. Os testes atuais não substituem essa integração. |
| E-mail | Testar cadastro, recuperação e lembretes com SMTP de homologação e destinatários de teste autorizados. | O transporte real não foi exercitado; as novas verificações usam um serviço de e-mail de teste que captura o destinatário. |
| Sessão | Validar troca de identidade, renovação e saída entre abas que compartilham o cookie. | A fila HTTP existente é local à instância JavaScript; não coordena abas. |
| Concorrência | Validar envio de lembretes e geração de mensalidades em instâncias simultâneas; incluir cancelamento da consulta ou desativação da conta enquanto o lote já está carregado. | Há seleção seguida de processamento/gravação; não foi demonstrada proteção distribuída de entrega nem atomicidade entre autorização e transporte nesta rodada. |
| Operação | Executar implantação HTTPS e restaurar banco/chaveiro/certificados num ambiente descartável. | Scripts existentes não equivalem a uma restauração exercitada nesta revisão. |
| Navegador | Acompanhar a instabilidade anterior de espera pela auditoria. | O cenário passou na repetição, sem comprovação de causa definitiva para a falha inicial. |

Essas validações são pendências, não resultados positivos nem falhas presumidas.
Não foram disparados e-mails ou mensagens para destinatários reais.

## Verificação desta rodada

- Build do back-end: passou sem avisos ou erros.
- Testes completos do back-end: 207 passaram.
- `npm run test:e2e -- jornada-consultorio.spec.ts rotina-clinica.spec.ts`: 12 cenários passaram em desktop e mobile, incluindo verificação de console nas jornadas.
- Front-end visual, tokens e contratos HTTP não foram alterados nesta rodada.
- Revisão independente das correções: sem novos defeitos acionáveis; o risco de mudanças concorrentes entre carregar o lote e enviar foi mantido explicitamente como pendência de validação.
