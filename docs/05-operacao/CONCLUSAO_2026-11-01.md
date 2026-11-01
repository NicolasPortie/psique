# Conclusão da rodada de correções — 1º de novembro de 2026

> Rodada posterior: outros quatro casos foram identificados e corrigidos, com
> regressões e uma migração de proteção de tentativas SMTP. Resultados atuais
> estão em [NOVA_REVISAO_2026-11-01.md](NOVA_REVISAO_2026-11-01.md).

## Implementação concluída

- Sessão: avisos entre abas por BroadcastChannel, sem tokens nem dados pessoais;
  operações de cookie coordenadas por Web Locks. Uma operação ainda na fila é
  descartada se o acesso mudar. A renovação não troca silenciosamente de pessoa.
  A tela autenticada reage à invalidação e volta ao acesso; os loaders não
  renovam automaticamente a identidade recém-invalidada com o cookie de outra aba.
- Concorrência: exclusão por recurso no SQL Server para lembretes, mensagens e
  geração de mensalidades da mesma competência. Releitura do estado antes de
  processar um lote evita usar apenas a seleção antiga do worker.
- WhatsApp: transporte HTTP Evolution API v2, teste autenticado de instância,
  fila durável, vínculo com lembrete, marcador de início, confirmações recebidas
  pelo webhook e correlação de confirmações antecipadas. A simples entrada na
  fila não marca um lembrete como enviado.
- Redis: revogação do conjunto de sessões passou a executar em um script atômico,
  eliminando a janela entre ler os tokens e removê-los.
- SMTP: validação com STARTTLS em servidor local; erros de transporte registram
  somente o tipo da falha, sem despejar exceções com dados do destinatário.

As duas migrações de WhatsApp foram aplicadas ao banco local de desenvolvimento.
Aplicá-las também na implantação, antes de iniciar a API atualizada.

## Evidências

| Verificação | Resultado |
| --- | --- |
| Back-end completo, com SQL Server e Redis reais e SMTP local | 231 testes passaram, nenhum ignorado |
| Front-end unitário | 153 testes passaram |
| Build front-end e lint | Passaram |
| Build back-end durante os testes | Passou |
| SQL Server, conexões independentes | Exclusão do mesmo recurso; recursos diferentes independentes; três gerações de mensalidade produzem uma cobrança |
| WhatsApp com contrato HTTP controlado + SQL real | Uma chamada entre dois processadores; sem repetição após falha; conta inativa bloqueada; confirmação antecipada preservada |
| Redis real | Recuperação e refresh consumidos uma vez; limite/expiração; revogação entre instâncias; código de cadastro consumido uma vez e tentativas limitadas |
| SMTP local com STARTTLS e certificado fixado ao teste | Recuperação e mensagem administrativa recebidas no Mailpit |
| Backup de banco | BACKUP com CHECKSUM, VERIFYONLY e RESTORE em outro banco descartável; registro conferido após restauração |
| Chaveiro e certificado | Exportação/restauração do chaveiro Redis e certificado PFX efêmero; conteúdo cifrado reaberto com sucesso |
| HTTPS local em Production | Certificado validado explicitamente pelo curl; `/api/v1/saude` retornou HTTP 200 com SQL Server e Redis |
| Sessão entre abas no navegador | Troca de pessoa, restauração por recarga e saída passaram em desktop e mobile |
| E2E final após as correções de sessão | 27 passaram e 3 foram ignorados por serem específicos de outra plataforma; conta, jornada, rotas, console e larguras 390/320 px |

A execução completa anterior teve 54 aprovações, 4 exclusões por plataforma e
2 falhas no novo cenário entre abas. O cenário foi corrigido, revelou problemas
reais de restauração/reação da tela e passou após as correções. A rodada final
de 30 cenários repetiu os fluxos afetados, sem falhas. A execução completa anterior
também verificou axe nos temas claro e escuro e movimento reduzido; nenhuma regra
de acessibilidade foi desativada para obter aprovação.

Os testes de contrato do WhatsApp usam respostas controladas do provedor. SMTP,
Redis e SQL Server usam processos reais em contêineres descartáveis, sem enviar
comunicações para pacientes. A restauração foi exercitada por testes de integração,
não pela execução do script destrutivo de restauração de produção. Não houve
restauração do banco de desenvolvimento nem alteração de dados reais.

Logs locais: `output/conclusao-backend-final.log`, `conclusao-front-tests.log`,
`conclusao-front-build.log`, `conclusao-front-lint.log`, `conclusao-restauracao.log`,
`conclusao-smtp.log`, `conclusao-https-cabecalhos.txt`, `conclusao-https-saude.json`
e `conclusao-e2e-abas.log`.
O resultado da última rodada de navegador está em `output/conclusao-e2e-final.log`.
Esses artefatos de execução são locais e ignorados pelo Git.
Os contêineres descartáveis de SQL Server, Redis e SMTP foram removidos ao final;
os demais contêineres do computador não foram encerrados.

## O que depende do ambiente real

1. Instalar/provisionar a Evolution, informar URL/chave/instância e parear o número.
   Conferir envio, recebimento e leitura com destinatário autorizado, seguindo
   [WHATSAPP_EVOLUTION.md](WHATSAPP_EVOLUTION.md).
2. Configurar domínio público, DNS, certificado público e SMTP da implantação.
   HTTPS local não valida DNS, ACME, entregabilidade ou credenciais de terceiros.
3. Ensaiar a restauração pelo procedimento de produção no servidor de homologação,
   incluindo certificado TDE quando essa camada estiver habilitada. O teste local
   comprova restauração SQL e Data Protection, sem comprovar a restauração de TDE
   em outro servidor nem a configuração final de volumes do operador.

Tentativa de WhatsApp sem confirmação exige conferência manual no provedor.
A API do provedor não permite prometer entrega exatamente uma vez.
