# Conectar o WhatsApp com Evolution API v2

## Preparação

Use uma instalação da Evolution API v2 acessível por HTTPS. Crie uma instância
exclusiva para o consultório e faça o pareamento pelo mecanismo de QR code do
provedor. A chave cadastrada no Psique deve ser exclusiva dessa instância/conta;
não reutilize a chave administrativa global entre consultórios.

Em **Integração WhatsApp**, preencha o número com DDI e DDD, a chave de acesso,
o nome exato da instância e a URL base da Evolution. Em opções avançadas,
o provedor deve ser **Evolution API**. Salve e use **Testar conexão**.
Só uma instância autenticada com estado `open` ficará Ativa.

## Recebimento e confirmações

Configure o webhook da instância para:

```text
https://SEU_DOMINIO/api/v1/whatsapp/bridge/evolution
```

Ative `MESSAGES_UPSERT`, `MESSAGES_UPDATE` e `CONNECTION_UPDATE`, com envio por
evento desativado (`webhookByEvents`/`byEvents: false`, conforme a versão).
O envelope deve incluir `instance`, `event`, `data` e `apikey`; alternativamente,
configure o header `X-Psique-Bridge-Token` com a mesma chave da integração.
Nunca coloque a chave na URL. Desative logs de payload no provedor: o envelope
pode conter a chave e mensagens pessoais.

O adaptador aceita texto individual em `conversation` ou
`extendedTextMessage.text`. Ignora ecos próprios, grupos, status e anexos.
O contrato genérico anterior em `/eventos` continua disponível para integrações
existentes, mas seu nome de provedor não habilita o transporte Evolution.

## Como acompanhar o envio

- **Pendente:** mensagem gravada e aguardando processamento.
- **Enviada:** o provedor aceitou a mensagem e devolveu um identificador.
- **Entregue/Lida:** confirmação recebida do WhatsApp.
- **Falhou/sem confirmação:** confira a conversa no provedor antes de enviar uma
  nova mensagem. O sistema não repete automaticamente tentativas incertas.

O processador verifica a fila a cada 15 segundos. Contas e integrações inativas,
conversas bloqueadas/finalizadas e lembretes de consultas encerradas não são
enviados. Um lembrete só é marcado Enviado após o aceite do provedor.

Uma queda depois do início persistido pode deixar a mensagem Pendente com aviso
de conferência. A ausência de confirmação local não prova que o telefone não
recebeu a mensagem. Verifique antes de criar outra saída pela conversa.

## Homologação antes de usar pacientes reais

Os testes automatizados verificam o contrato HTTP e a concorrência com SQL Server
real. Eles não fazem pareamento de telefone nem enviam mensagens reais.
Depois de configurar a instância, use um número de teste autorizado para conferir
envio, recebimento, confirmação e desconexão/reconexão, antes de ativar a rotina.
O domínio, a instalação Evolution e o número não foram fornecidos nesta revisão.
