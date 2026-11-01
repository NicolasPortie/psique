# Nova inspeção após a conclusão da rodada

## Atualização: quatro pendências corrigidas

Validação complementar concluída: a suíte completa foi repetida com SQL Server,
Redis e SMTP reais em contêineres locais descartáveis, incluindo restauração.
Resultado: **247 testes aprovados, zero falhas e zero ignorados**. Evidência em
`output/revisao-extra-infra-completa.log`. Isso fecha os seis testes de
infraestrutura que ficaram ignorados na execução anterior descrita abaixo.
Os contêineres exclusivos da validação foram removidos após os testes.

Após autorização para corrigir, os quatro casos receberam implementação e testes
de regressão pelos serviços públicos. O diagnóstico original abaixo fica
preservado como histórico.

- E-mail: marcador durável `EnvioEmailIniciadoEm` antes do transporte. Uma
  tentativa interrompida é sinalizada para conferência, sem repetição automática
  nem reprocessamento manual da mesma tentativa. O caso passou também com duas
  conexões SQL independentes.
- Evolution: eventos de mensagem não alteram mais o estado de conexão. A
  compatibilidade do contrato genérico antigo foi mantida.
- Telefone: o recorte de onze posições agora exige pelo menos onze dígitos;
  regressões cobrem entradas de 10, 11, 12 e 13 dígitos.
- JSON: os tipos da raiz, de `instance`, `state` e `key` são conferidos antes da
  leitura; respostas de formato inesperado não viram sucesso nem exceção de tipo
  sem tratamento.

Verificação: 241 testes passaram e 6 foram ignorados por infraestrutura opcional
não configurada nesta rodada (Redis, SMTP e restauração). SQL Server real via
LocalDB foi exercitado, incluindo persistência da tentativa entre conexões.
O build do back-end passou durante os testes. Os casos de JSON, telefone,
conexão e interrupção SMTP falharam antes de suas respectivas correções.
As jornadas de consultório e rotina clínica passaram em desktop e mobile:
12 cenários, incluindo conferência de console nas jornadas. Evidência em
`output/revisao-extra-e2e.log`. Não houve alteração visual ou de tokens.

A migração `ProtegerTentativasEmailInterrompidas` foi gerada e aplicada ao banco
local. Aplicá-la antes de iniciar a API atualizada em outros ambientes.
Logs locais: `output/revisao-extra-backend.log`, `revisao-extra-*-red.log`,
`revisao-extra-*-green.log` e `revisao-extra-migrar-dev.log`.

## Diagnóstico anterior às correções

Solicitação: conferir o que ainda falta. Esta rodada fez inspeção estática focada
em entrega, recuperação de falhas e operação. Não alterou implementação nem
reexecutou as suítes. Os testes aprovados anteriormente continuam sendo evidência
dos cenários cobertos, sem comprovar ausência destes outros casos.

## Pendências de código

### Alta: confirmação de e-mail não é durável antes do transporte

Em `Services/Lembretes/LembretesService.cs`, `TentarProcessarEnvioAsync` chama
`EnviarMensagemAdministrativaAsync` antes de persistir qualquer marcador de
tentativa. Só depois do retorno SMTP o lembrete muda para Enviado e é salvo.
Se o processo cair após o servidor SMTP aceitar a mensagem e antes da gravação,
o banco mantém Agendado; `ProcessarVencidosAsync` poderá enviar novamente.
A trava SQL evita dois processadores simultâneos, mas não sobrevive como
confirmação durável de uma tentativa já entregue ao SMTP.

Falta persistir o início e distinguir confirmação incerta de envio não iniciado,
com regressão que simule a interrupção nesse intervalo. Isso reduz repetição
automática; não torna SMTP um transporte com entrega exatamente uma vez.

### Média: eventos de mensagem reativam uma conexão desconectada

Em `Services/WhatsApp/BridgeWhatsAppService.cs`, todo evento processado executa
`integracao.Status = StatusIntegracao.Ativa`. Assim, depois de um evento de
desconexão colocar a integração como Pendente, uma confirmação atrasada de leitura
ou entrega pode voltar a marcá-la como Ativa sem conferir a conexão. A fila de
saída usa esse estado para selecionar mensagens.

Falta separar processamento de mensagem de confirmação de conexão, preservando
o estado dado pelo teste autenticado e pelos eventos de conexão. Acrescentar a
sequência desconectar → receber confirmação antiga como regressão.

### Média: variação de telefone acessa 11 posições em uma entrada de 10

Em `Services/WhatsApp/BridgeWhatsAppService.cs`, `VariacoesHashTelefone` testa
`telefone.Length >= 10`, mas acessa `telefone[^11..]`. Uma entrada de 10 dígitos,
aceita por `NormalizarTelefone`, lança exceção ao identificar um novo contato.
O processamento do webhook pode terminar em erro 500 e marcar a integração com
erro. O adaptador Evolution normalmente recebe DDI; o contrato genérico ainda
aceita a entrada que dispara o defeito.

Falta alinhar o limite do recorte ao tamanho efetivamente acessado e cobrir
as variações de 10, 11, 12 e 13 dígitos pelo serviço público.

### Baixa: formatos JSON inesperados escapam do tratamento do provedor

Em `Services/WhatsApp/EvolutionApiClient.cs`, `TryGetProperty` é chamado sem
conferir `ValueKind` da raiz e de `instance`/`key`; o estado também usa
`GetString` sem conferir o tipo. Respostas JSON válidas com formato inesperado
(por exemplo `null`, `[]` ou `{"instance":null}`) podem lançar
`InvalidOperationException`, que não está nos filtros de erro de conexão/envio.
No envio, o marcador já persistido impede repetição cega, mas a tentativa pode
ficar Pendente com aviso genérico em vez de receber uma falha tratada.

Falta validar a estrutura antes de ler propriedades e acrescentar respostas
malformadas aos testes de contrato. Não houve nova consulta ao provedor nesta
rodada: esse achado vem do comportamento das operações JSON no código local.

## Dependências operacionais preservadas

- Provisionar a Evolution, configurar credenciais e parear um número.
- Homologar mensagens com destinatário autorizado.
- Configurar domínio, certificado público e SMTP reais.
- Exercitar o procedimento de restauração no ambiente de homologação, incluindo
  TDE quando habilitado.

As quatro pendências de código acima foram corrigidas conforme a atualização
no início deste documento. As dependências operacionais continuam abertas.
Não foram enviadas mensagens reais nem executadas restaurações nesta rodada.
