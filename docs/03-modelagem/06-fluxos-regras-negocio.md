# Fluxos e Regras de Negocio

## Criacao de Conta

```txt
Psicologo cria cadastro
  -> cria ContaPsicologo
  -> cria Usuario principal com perfil Psicologo
  -> cria DadosProfissionaisPsicologo inicial ou pendente
  -> cria ConfiguracaoAtendimento padrao
  -> associa PlanoSaaS inicial, se aplicavel
  -> registra LogAuditoria
```

## Login e Resolucao da Conta

```txt
Usuario informa credenciais
  -> API valida login
  -> API verifica Status do Usuario
  -> API verifica Status da ContaPsicologo
  -> API inclui ContaPsicologoId e Perfil no token/sessao
  -> todas as consultas usam a conta autenticada como escopo
```

Regras:

- front-end nao decide a conta;
- API nunca deve aceitar `ContaPsicologoId` livre como permissao;
- services validam se entidades relacionadas pertencem a mesma conta.

## Cadastro de Paciente

```txt
Usuario informa dados administrativos
  -> API identifica conta autenticada
  -> valida dados
  -> cria Paciente com ContaPsicologoId
  -> cria Prontuario, se regra estiver ativa
  -> registra LogAuditoria
```

## Consulta pelo Painel

```txt
Usuario seleciona paciente, data e horario
  -> API valida paciente na conta
  -> API valida disponibilidade
  -> API valida bloqueios
  -> API cria Consulta
  -> API cria HistoricoConsulta
  -> API pode criar Pagamento pendente
     (valor: informado > ValorSessao do paciente > ValorPadrao do tipo)
  -> API registra LogAuditoria
```

A tela de nova consulta mostra os inícios livres do dia (`GET /agenda/horarios-livres?data&duracaoMinutos`): dentro dos horários de atendimento, fora de consultas e bloqueios e depois da antecedência mínima, em passos de 30 minutos. É uma ajuda de escolha; a validação continua no momento de salvar.

## Série de Consultas (recorrência)

Psicoterapia costuma ser semanal no mesmo horário, então a consulta pode ser criada como série.

```txt
Usuario escolhe "Toda semana" ou "A cada 15 dias" e quantas consultas (2 a 52)
  -> POST /consultas/serie
  -> API valida a primeira ocorrencia como uma consulta comum (falha = nada é criado)
  -> API repete o horario de parede no fuso da conta, de 7 em 7 ou de 14 em 14 dias
  -> ocorrencias em bloqueio, fora do atendimento ou em conflito ficam de fora
     e voltam em "ignoradas" com o motivo (a tela avisa as datas)
  -> todas as criadas compartilham Consulta.SerieId
  -> pagamento pendente e lembrete seguem as mesmas regras da consulta avulsa, por ocorrencia
  -> API registra LogAuditoria "SerieConsultasCriada"
```

Cancelar a série (`PATCH /consultas/{id}/cancelar-serie`) cancela a consulta escolhida e as próximas ainda em aberto (aguardando, agendada, confirmada) da mesma série. As anteriores e as já encerradas ficam como estão. Valem a justificativa obrigatória e a antecedência de cancelamento da configuração da agenda.

## Fechamento do Dia

Consulta que terminou e continua aguardando, agendada ou confirmada está "sem desfecho". O painel (`consultasSemDesfecho`, últimos 60 dias) lista essas sessões para marcar Realizada ou Não compareceu em um toque; sem isso, contagens de sessões, faltas e cobranças ficam erradas.

## Cobranca ao Realizar, Falta e Mensalidade

- `ConfiguracaoAtendimento.CobrarAoRealizar` (padrão ligado): ao marcar a consulta como Realizada (pelo status ou ao registrar a sessão), a API cria a cobrança pendente com `Paciente.ValorSessao` ou, sem ele, o `ValorPadrao` do tipo. Sem valor, nada é criado.
- `CobrarFalta`: o não comparecimento também gera cobrança, com a observação "Falta cobrada conforme a política do consultório."
- Nunca duplica: se a consulta já tem cobrança ativa (por exemplo, criada no agendamento), nada acontece.
- `Paciente.ModoCobranca` = `Mensal`: as sessões não são cobradas uma a uma. `POST /pagamentos/mensalidades {ano, mes, diaVencimento}` cria uma cobrança por mensalista ativo com o valor combinado e `Pagamento.Competencia` = dia 1 do mês; rodar de novo não duplica. Mensalistas sem valor voltam em `semValor`.

## Cobranca e Recibo

- "Vencido" não é status no banco: é o pagamento Pendente/Aguardando com `VencimentoEm` anterior a hoje. A listagem aceita `vencidos=true` e os totais trazem `vencido`.
- Paciente, período (`de`, `ate`, pelo vencimento ou pagamento) e busca por nome recortam a lista e os totais; o estado recorta só a lista.
- Ao receber, a equipe informa a forma (Pix, cartão, dinheiro, transferência, outra).
- Uma cobrança Pendente ou Aguardando confirmação pode ser recebida ou cancelada. Uma cobrança Paga pode ser reembolsada; Cancelada e Reembolsada são estados finais. O reembolso preserva a data original do recebimento. Alterações concorrentes de estado retornam conflito (409), sem sobrescrever a alteração anterior.
- Períodos, vencimentos e datas de referência usam o fuso configurado do consultório. A exportação sem mês selecionado cobre os últimos 12 meses, incluindo o atual; o CSV trata nomes e observações como texto, sem fórmulas.
- `GET /pagamentos/recibo-mensal?pacienteId&ano&mes` junta num recibo todos os pagamentos recebidos do paciente no mês (auditado como "ReciboMensalEmitido").
- `GET /pagamentos/exportacao?de&ate` devolve os recebimentos do período com CPF do pagador, para o contador ou o Carnê-Leão; a tela gera um CSV com ponto e vírgula. Auditado como "RecebimentosExportados".
- `GET /pagamentos/{id}/recibo` devolve os dados do recibo apenas para pagamento recebido: paciente (com CPF, se houver), valor, data, forma, sessão de referência e nome e CRP do psicólogo. Sem nome e CRP cadastrados, a emissão é recusada. Cada emissão fica na auditoria ("ReciboEmitido").

## LGPD do Paciente

- `POST /pacientes/{id}/consentimento` registra `ConsentimentoRegistradoEm` (auditado). A ficha mostra a data ou o botão para registrar.
- `GET /pacientes/{id}/exportacao` entrega os dados administrativos (cadastro, contatos, consultas e pagamentos), nunca conteúdo clínico, para atender a um pedido de acesso do titular. Auditado como "PacienteDadosExportados".
- O prontuário mostra o prazo de guarda (o CFP exige no mínimo 5 anos); inativar o paciente preserva tudo.

## Pacientes sem Consulta Recente

`GET /pacientes?semConsultaHaDias=30` lista ativos que já tiveram sessão realizada e não têm nenhuma consulta (passada ou marcada) desde o limite: bons candidatos a um contato.

## Painel: Aniversarios e Primeiros Passos

O painel traz `aniversariantes` (ativos com aniversário nos próximos 7 dias) e, para quem gere a conta, `primeirosPassos` (nome e CRP, horários, tipos de atendimento, primeiro paciente e WhatsApp).

## Lembretes Vencidos

O processador não repega por 15 minutos um lembrete já tentado (e-mail desligado, canal interno), para os travados não ocuparem o lote. Lembrete cuja consulta já começou é encerrado como Ignorado ("LembreteExpirado").

A criação exige um horário anterior ao início da consulta. O reenvio manual
também confere a situação e o início do atendimento antes de chamar o transporte;
consultas encerradas ou já iniciadas tornam o lembrete Ignorado. O processador
automático só seleciona contas ativas. O destinatário é decifrado no serviço,
imediatamente antes do transporte de e-mail. Datas e horários dos modelos usam o
fuso configurado da conta.

## Responsavel e Contato de Emergencia

`ContatoPaciente` aceita os tipos `Responsavel` (responsável legal, obrigatório na prática para menores) e `Emergencia`, com `Nome` protegido como os demais dados pessoais. A ficha avisa quando o paciente é menor de idade e não tem responsável cadastrado.

## Registro de Sessao

```txt
Psicologo acessa consulta realizada
  -> API identifica ou cria Prontuario
  -> psicologo registra dados clinicos
  -> API cria RegistroSessao
  -> API pode criar EvolucaoProntuario vinculada
  -> API atualiza Consulta como Realizada
  -> API registra LogAuditoria
```

## Acompanhamento do Paciente

```txt
Psicologo acessa prontuario
  -> visualiza linha do tempo
  -> registra EvolucaoProntuario ou atualiza resumo
  -> API criptografa conteudo clinico
  -> API salva evolucao
  -> API registra auditoria
```

## Geracao de Documento

```txt
Psicologo seleciona tipo de documento
  -> escolhe fontes: sessoes, evolucoes, periodo ou resumo
  -> sistema monta rascunho
  -> psicologo revisa e edita
  -> psicologo finaliza/assina
  -> API cria versao final
  -> API registra auditoria
```

Regras:

- documento final nunca e emitido automaticamente;
- todo documento nasce como rascunho;
- documento entregue exige protocolo;
- fontes usadas devem ser rastreaveis.

## WhatsApp

### Confirmação de lembretes por e-mail

Antes de chamar o SMTP, o back-end persiste `EnvioEmailIniciadoEm` no lembrete.
O marcador fica preservado mesmo após o aceite do transporte. Se o processo
interromper antes de registrar o resultado, o próximo processamento marca a
tentativa como Falhou e informa que é preciso conferir o envio no serviço de
e-mail. Não envia novamente nem permite reprocessar a mesma tentativa incerta.
Depois da conferência, a equipe pode criar um novo lembrete quando necessário.

E-mail desabilitado ou paciente sem endereço são verificados antes do marcador;
esses casos não representam uma tentativa entregue ao SMTP. O controle reduz
duplicações após uma queda, sem prometer entrega exatamente uma vez.

### Estado de conexão da Evolution

Mensagens recebidas e confirmações de entrega/leitura não atestam a conexão
atual da Evolution. Somente o teste autenticado e os eventos de conexão atualizam
esse estado. Uma confirmação atrasada não reativa uma conexão desconectada.

```txt
Paciente envia mensagem
  -> Bridge chama webhook
  -> API identifica conta pela integracao
  -> API registra evento
  -> API registra mensagem
  -> bot identifica intencao administrativa
  -> API executa regra de agenda/consulta
  -> Bridge envia resposta
```

## Mensagem Sensivel

```txt
Paciente envia mensagem sensivel ou urgente
  -> bot detecta risco ou conteudo clinico
  -> bot nao realiza atendimento psicologico
  -> conversa marcada como AtendimentoHumanoNecessario
  -> psicologo recebe notificacao
```
