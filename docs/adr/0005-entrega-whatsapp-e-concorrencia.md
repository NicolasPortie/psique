# 0005. Evolution API v2 e confirmação de envio

Em 1º de novembro de 2026, foi escolhida a Evolution API v2 para o contrato de
WhatsApp, após autorização do responsável pelo projeto. A integração usa uma
instância e uma chave exclusivas por conta. A alternativa de manter um Bridge
genérico não definia autenticação, envio nem confirmação; a API oficial da Meta
exigiria outro contrato e provisionamento. A Evolution atende à escolha de um
Bridge conhecido e permite configurar o transporte sem mudar as regras do bot.

Uma mensagem na fila continua Pendente e o lembrete continua Agendado. O aceite
de `POST /message/sendText/{instance}`, com `key.id`, registra Enviada. Os eventos
de entrega e leitura atualizam esses estados separadamente. O teste de conexão
exige uma resposta autenticada de `GET /instance/connectionState/{instance}` com
`state: open`. HTTP 401 e 404 nunca representam conexão válida.

O SQL Server coordena operações concorrentes com `sp_getapplock`: mensagem,
lembrete e competência de mensalidade têm recursos separados. A fila é durável
no banco, sem transformar o Redis em fonte de dados do consultório. O marcador
de início é persistido antes da chamada externa. Como o contrato de envio não
oferece idempotência, uma resposta incerta exige conferência no provedor e não
gera repetição automática. Isso evita duplicações cegas, mas uma queda após o
aceite pode deixar a mensagem sem confirmação local. Não há garantia de entrega
exatamente uma vez ao aparelho.

Confirmações são preservadas com identificador externo e estado, inclusive
quando chegam antes da resposta HTTP. A correlação e a gravação usam a mesma
trava por conta e identificador; leitura e entrega não regridem por eventos
antigos. O conteúdo pessoal continua cifrado.

Fontes do contrato:
[OpenAPI v2](https://github.com/evolution-foundation/docs-evolution/blob/main/openapi/openapi-v2.json)
e [implementação do webhook](https://github.com/EvolutionAPI/evolution-api/blob/main/src/api/integrations/event/webhook/webhook.controller.ts).
