import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarPlus, Download, FileHeart, Pencil, ShieldAlert, ShieldCheck, UserCheck, UserMinus } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';

import { CarregandoPagina } from '../../components/feedback/esqueleto';
import { EstadoErro } from '../../components/feedback/estados';
import { notificar } from '../../components/feedback/notificar';
import { CabecalhoPagina, Pagina, Secao } from '../../components/layout/pagina';
import { Abas, PainelAba } from '../../components/ui/abas';
import { Avatar } from '../../components/ui/avatar';
import { Botao, BotaoLink } from '../../components/ui/botao';
import { Confirmacao } from '../../components/ui/dialogo';
import { Dado, Ficha } from '../../components/ui/lista';
import { EstadoRotulado } from '../../components/ui/marca-estado';
import { MenuAcoes, type ItemMenu } from '../../components/ui/menu';
import { usePermissoes } from '../../core/autenticacao/permissoes';
import { rotuloDe } from '../../core/dominio/estado';
import { mensagemDeErro } from '../../core/erros/mensagem-de-erro';
import { formatarData, formatarDataHora, formatarTempoRelativo } from '../../core/formatacao/datas';
import { formatarMoeda } from '../../core/formatacao/moeda';
import { formatarTelefone, textoOuPadrao } from '../../core/formatacao/pessoas';
import { pluralizar } from '../../core/formatacao/texto';
import { painelKeys } from '../painel/painel.api';
import { ConsultasDoPaciente } from './components/consultas-do-paciente';
import { DocumentosDoPaciente } from './components/documentos-do-paciente';
import { FinanceiroDoPaciente } from './components/financeiro-do-paciente';
import { resumirConsultas, useConsultasDoPaciente, useFinanceiroDoPaciente } from './ficha-paciente.dados';
import { ativarPaciente, exportarDadosPaciente, inativarPaciente, obterPacientePorId, registrarConsentimento } from './pacientes.api';
import { pacientesKeys } from './pacientes.keys';
import { estadosPaciente, rotulosTipoContato } from './pacientes.rotulos';
import type { ContatoPaciente } from './pacientes.types';

/** Idade completa em anos, só para exibição ao lado da data de nascimento. */
function idadeEmAnos(nascimento: string, hoje = new Date()) {
  const data = new Date(`${nascimento.slice(0, 10)}T12:00:00`);
  if (Number.isNaN(data.getTime())) return null;
  let idade = hoje.getFullYear() - data.getFullYear();
  const jaFezAniversario = hoje.getMonth() > data.getMonth() || (hoje.getMonth() === data.getMonth() && hoje.getDate() >= data.getDate());
  if (!jaFezAniversario) idade -= 1;
  return idade >= 0 ? idade : null;
}

const abasValidas = ['resumo', 'consultas', 'financeiro', 'documentos'] as const;
type Aba = (typeof abasValidas)[number];

function valorContato(contato: ContatoPaciente) {
  return contato.tipoContato === 'Email' || /[^\d]/.test(contato.valor) ? contato.valor : formatarTelefone(contato.valor);
}

export default function FichaPacientePage() {
  const { id = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const queryClient = useQueryClient();
  const { acessoClinico, pode } = usePermissoes();
  const podeFinanceiro = pode('Financeiro');
  const podeAgenda = pode('Agenda');
  const [confirmandoInativacao, setConfirmandoInativacao] = useState(false);

  const abaDaUrl = params.get('aba') as Aba | null;
  const abaPermitida = abaDaUrl !== 'consultas' || podeAgenda;
  const abaFinanceiraPermitida = abaDaUrl !== 'financeiro' || podeFinanceiro;
  const abaClinicaPermitida = abaDaUrl !== 'documentos' || acessoClinico;
  const aba: Aba = abaDaUrl && abasValidas.includes(abaDaUrl) && abaPermitida && abaFinanceiraPermitida && abaClinicaPermitida ? abaDaUrl : 'resumo';

  const pacienteQuery = useQuery({
    queryKey: pacientesKeys.detalhe(id),
    queryFn: ({ signal }) => obterPacientePorId(id, { signal }),
    enabled: Boolean(id),
  });
  const consultas = useConsultasDoPaciente(id, podeAgenda);
  const financeiro = useFinanceiroDoPaciente(id, podeFinanceiro);

  const consentir = useMutation({
    mutationFn: () => registrarConsentimento(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: pacientesKeys.todos });
      notificar.sucesso('Consentimento registrado.');
    },
    onError: (erro) => notificar.erro(mensagemDeErro(erro, 'Não foi possível registrar o consentimento.')),
  });

  const exportar = useMutation({
    mutationFn: () => exportarDadosPaciente(id),
    onSuccess: (dados) => {
      const arquivo = new Blob([JSON.stringify(dados, null, 2)], { type: 'application/json' });
      const endereco = URL.createObjectURL(arquivo);
      const ancora = document.createElement('a');
      ancora.href = endereco;
      ancora.download = `dados-${dados.cadastro.nomeCompleto.toLocaleLowerCase('pt-BR').replace(/\s+/g, '-')}.json`;
      ancora.click();
      URL.revokeObjectURL(endereco);
      notificar.sucesso('Dados exportados. A exportação ficou registrada na auditoria.');
    },
    onError: (erro) => notificar.erro(mensagemDeErro(erro, 'Não foi possível exportar os dados.')),
  });

  const alterarStatus = useMutation({
    mutationFn: (ativar: boolean) => (ativar ? ativarPaciente(id) : inativarPaciente(id)),
    onSuccess: async (paciente) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: pacientesKeys.todos }),
        queryClient.invalidateQueries({ queryKey: painelKeys.todos }),
      ]);
      notificar.sucesso(paciente.status === 'Ativo' ? 'Paciente ativado.' : 'Paciente inativado.');
      setConfirmandoInativacao(false);
    },
    onError: (erro) => {
      notificar.erro(mensagemDeErro(erro, 'Não foi possível alterar o paciente.'));
      setConfirmandoInativacao(false);
    },
  });

  if (pacienteQuery.isPending) return <CarregandoPagina rotulo="Carregando o paciente…" />;
  if (pacienteQuery.error || !pacienteQuery.data) {
    return (
      <EstadoErro
        titulo="Paciente indisponível"
        mensagem={mensagemDeErro(pacienteQuery.error, 'Não encontramos este paciente.')}
        aoTentarNovamente={() => void pacienteQuery.refetch()}
        acao={
          <BotaoLink to="/pacientes" variante="secundario">
            Voltar aos pacientes
          </BotaoLink>
        }
      />
    );
  }

  const paciente = pacienteQuery.data;
  const idade = paciente.dataNascimento ? idadeEmAnos(paciente.dataNascimento) : null;
  const menor = idade !== null && idade < 18;
  const resumo = resumirConsultas(consultas.consultas);
  const responsaveis = paciente.contatosAdicionais.filter((contato) => contato.tipoContato === 'Responsavel');
  const emergencia = paciente.contatosAdicionais.filter((contato) => contato.tipoContato === 'Emergencia');
  const outrosContatos = paciente.contatosAdicionais.filter((contato) => contato.tipoContato !== 'Responsavel' && contato.tipoContato !== 'Emergencia');

  const acoesMenu: ItemMenu[] = [
    { rotulo: 'Editar cadastro', icone: <Pencil size={16} />, para: `/pacientes/${paciente.id}/editar` },
    { rotulo: 'Exportar dados do paciente (LGPD)', icone: <Download size={16} />, aoSelecionar: () => exportar.mutate() },
    ...(paciente.status === 'Arquivado'
      ? []
      : paciente.status === 'Ativo'
        ? [{ rotulo: 'Inativar paciente', icone: <UserMinus size={16} />, separador: true, aoSelecionar: () => setConfirmandoInativacao(true) }]
        : [{ rotulo: 'Ativar paciente', icone: <UserCheck size={16} />, separador: true, aoSelecionar: () => alterarStatus.mutate(true) }]),
  ];

  const abas = [
    { valor: 'resumo', rotulo: 'Resumo' },
    ...(podeAgenda ? [{ valor: 'consultas', rotulo: 'Consultas', contagem: consultas.isPending || consultas.error ? undefined : consultas.consultas.length }] : []),
    ...(podeFinanceiro ? [{ valor: 'financeiro', rotulo: 'Financeiro' }] : []),
    ...(acessoClinico ? [{ valor: 'documentos', rotulo: 'Documentos' }] : []),
  ];

  return (
    <Pagina largura="padrao" className="gap-8">
      <CabecalhoPagina
        titulo={paciente.nomeCompleto}
        voltarPara={{ para: '/pacientes', rotulo: 'Pacientes' }}
        meta={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
            <EstadoRotulado estado={estadosPaciente[paciente.status]} />
            {idade !== null ? <span className="text-sm">{pluralizar(idade, 'ano')}</span> : null}
            {podeAgenda && !consultas.error && resumo.primeira ? (
              <span className="text-sm">
                Primeira sessão no último ano: <span className="num">{formatarData(resumo.primeira.inicioEm)}</span>
              </span>
            ) : null}
          </span>
        }
        acoes={
          <>
            <MenuAcoes itens={acoesMenu} rotulo="Mais ações do paciente" />
            {acessoClinico ? (
              <BotaoLink to={`/pacientes/${paciente.id}/prontuario`} variante="secundario">
                <FileHeart size={16} aria-hidden="true" />
                Prontuário
              </BotaoLink>
            ) : null}
            {podeAgenda && paciente.status === 'Ativo' ? <BotaoLink to={`/consultas/nova?pacienteId=${paciente.id}`} variante="primario">
              <CalendarPlus size={16} aria-hidden="true" />
              Agendar consulta
            </BotaoLink> : null}
          </>
        }
      />

      {podeAgenda || podeFinanceiro ? <section aria-label="Resumo do acompanhamento" className="grid gap-4 rounded-lg border border-line bg-surface p-4 sm:grid-cols-[auto_minmax(0,1fr)] sm:items-center sm:gap-6 sm:p-5">
        <Avatar nome={paciente.nomeCompleto} tamanho="enorme" className="max-sm:hidden" />
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 md:grid-cols-4">
          {podeAgenda ? <>
          <div className="grid gap-0.5">
            <dt className="text-sm text-ink-muted">Próxima consulta</dt>
            <dd className="text-base font-medium text-ink">
              {consultas.isPending ? '…' : consultas.error ? 'Indisponível' : resumo.proxima ? (
                <Link to={`/consultas/${resumo.proxima.id}`} className="num">
                  {formatarDataHora(resumo.proxima.inicioEm)}
                </Link>
              ) : (
                <span className="text-ink-muted">Nada agendado</span>
              )}
              {!consultas.error && resumo.proxima ? <span className="block text-sm font-normal text-ink-muted">{formatarTempoRelativo(resumo.proxima.inicioEm)}</span> : null}
            </dd>
          </div>
          <div className="grid gap-0.5">
            <dt className="text-sm text-ink-muted">Sessões no último ano</dt>
            <dd className="num text-base font-medium text-ink">{consultas.isPending ? '…' : consultas.error ? 'Indisponível' : resumo.realizadas}</dd>
          </div>
          <div className="grid gap-0.5">
            <dt className="text-sm text-ink-muted">Faltas</dt>
            <dd className="num text-base font-medium text-ink">{consultas.isPending ? '…' : consultas.error ? 'Indisponível' : resumo.faltas}</dd>
          </div>
          </> : null}
          {podeFinanceiro ? (
            <div className="grid gap-0.5">
              <dt className="text-sm text-ink-muted">Em aberto</dt>
              <dd className="num text-base font-medium text-ink">
                {financeiro.error ? 'Indisponível' : financeiro.data ? formatarMoeda(financeiro.data.resumo.pendente) : '…'}
                {financeiro.data && financeiro.data.resumo.vencido > 0 ? (
                  <span className="block text-sm font-normal text-danger-ink">{formatarMoeda(financeiro.data.resumo.vencido)} vencido</span>
                ) : null}
              </dd>
            </div>
          ) : null}
        </dl>
      </section> : null}

      {podeAgenda && consultas.error ? <EstadoErro
        mensagem="Não foi possível carregar as consultas deste paciente."
        aoTentarNovamente={() => void consultas.refetch()}
      /> : null}

      {podeFinanceiro && financeiro.error ? <EstadoErro
        mensagem="Não foi possível carregar os pagamentos deste paciente."
        aoTentarNovamente={() => void financeiro.refetch()}
      /> : null}

      {menor && responsaveis.length === 0 ? (
        <p className="flex items-start gap-2 rounded-lg border border-warning-line bg-warning-wash px-4 py-3 text-base text-warning-ink">
          <ShieldAlert size={18} className="mt-0.5 shrink-0" aria-hidden="true" />
          <span>
            Paciente menor de idade sem responsável legal cadastrado.{' '}
            <Link to={`/pacientes/${paciente.id}/editar`} className="font-medium text-warning-ink">
              Cadastrar responsável
            </Link>
          </span>
        </p>
      ) : null}

      <Abas valor={aba} aoMudar={(valor) => setParams(valor === 'resumo' ? {} : { aba: valor }, { replace: true })} abas={abas} rotulo="Seções da ficha">
        <PainelAba valor="resumo">
          <div className="grid gap-x-8 gap-y-8 lg:grid-cols-2">
            <Secao titulo="Contato" painel>
              <Ficha colunas={1}>
                <Dado rotulo="WhatsApp">
                  <span className="num">{formatarTelefone(paciente.whatsApp) || <span className="text-ink-muted">Não informado</span>}</span>
                </Dado>
                <Dado rotulo="Telefone">
                  <span className="num">{formatarTelefone(paciente.telefone) || <span className="text-ink-muted">Não informado</span>}</span>
                </Dado>
                <Dado rotulo="E-mail">{textoOuPadrao(paciente.email, 'Não informado')}</Dado>
                {outrosContatos.map((contato, indice) => (
                  <Dado key={contato.id ?? indice} rotulo={`${rotuloDe(rotulosTipoContato, contato.tipoContato)}${contato.principal ? ' · principal' : ''}`}>
                    {contato.nome ? `${contato.nome} · ` : ''}
                    <span className="num">{valorContato(contato)}</span>
                  </Dado>
                ))}
              </Ficha>
            </Secao>

            <Secao
              titulo="Responsável e emergência"
              painel
              acoes={
                <Link to={`/pacientes/${paciente.id}/editar`} className="text-sm font-medium">
                  Editar
                </Link>
              }
            >
              {responsaveis.length + emergencia.length === 0 ? (
                <p className="text-base text-ink-muted">Ninguém cadastrado. Vale ter um contato para emergências{menor ? ' e o responsável legal' : ''}.</p>
              ) : (
                <Ficha colunas={1}>
                  {[...responsaveis, ...emergencia].map((contato, indice) => (
                    <Dado key={contato.id ?? indice} rotulo={rotuloDe(rotulosTipoContato, contato.tipoContato)}>
                      <span className="block font-medium">{contato.nome || 'Sem nome'}</span>
                      <span className="num text-ink-muted">{valorContato(contato)}</span>
                    </Dado>
                  ))}
                </Ficha>
              )}
            </Secao>

            <Secao titulo="Cadastro" painel>
              <Ficha colunas={2}>
                <Dado rotulo="Data de nascimento">
                  {paciente.dataNascimento ? <span className="num">{formatarData(`${paciente.dataNascimento.slice(0, 10)}T12:00:00`)}</span> : <span className="text-ink-muted">Não informada</span>}
                </Dado>
                <Dado rotulo="CPF">
                  <span className="num">{paciente.cpfMascarado ?? <span className="text-ink-muted">Não informado</span>}</span>
                </Dado>
                <Dado rotulo={paciente.modoCobranca === 'Mensal' ? 'Mensalidade' : 'Valor da sessão'}>
                  {paciente.valorSessao != null ? <span className="num">{formatarMoeda(paciente.valorSessao)}</span> : <span className="text-ink-muted">Não combinado</span>}
                  <span className="block text-sm text-ink-muted">{paciente.modoCobranca === 'Mensal' ? 'Cobrança mensal' : 'Cobrança por sessão'}</span>
                </Dado>
                <Dado rotulo="Consentimento (LGPD)" largo>
                  {paciente.consentimentoRegistradoEm ? (
                    <span className="inline-flex items-center gap-1.5">
                      <ShieldCheck size={16} className="text-success" aria-hidden="true" />
                      Registrado em <span className="num">{formatarData(paciente.consentimentoRegistradoEm)}</span>
                    </span>
                  ) : (
                    <span className="flex flex-wrap items-center gap-2">
                      <span className="text-ink-muted">Ainda não registrado.</span>
                      <Botao variante="secundario" tamanho="pequeno" carregando={consentir.isPending} onClick={() => consentir.mutate()}>
                        Registrar consentimento
                      </Botao>
                    </span>
                  )}
                </Dado>
                <Dado rotulo="Cadastrado em">
                  <span className="num">{formatarData(paciente.criadoEm)}</span>
                </Dado>
              </Ficha>
            </Secao>

            <Secao titulo="Observação administrativa" painel>
              <p className="max-w-[70ch] whitespace-pre-wrap text-base text-ink">
                {paciente.observacaoAdministrativa ?? <span className="text-ink-muted">Sem observação.</span>}
              </p>
              <p className="mt-3 text-sm text-ink-muted">Anotações clínicas ficam no prontuário e nunca aparecem aqui.</p>
            </Secao>
          </div>
        </PainelAba>

        {podeAgenda ? <PainelAba valor="consultas">
          <ConsultasDoPaciente pacienteId={paciente.id} podeAgendar={paciente.status === 'Ativo'} />
        </PainelAba> : null}

        {podeFinanceiro ? (
          <PainelAba valor="financeiro">
            <FinanceiroDoPaciente pacienteId={paciente.id} />
          </PainelAba>
        ) : null}

        {acessoClinico ? (
          <PainelAba valor="documentos">
            <DocumentosDoPaciente pacienteId={paciente.id} />
          </PainelAba>
        ) : null}
      </Abas>

      <Confirmacao
        aberto={confirmandoInativacao}
        aoMudar={setConfirmandoInativacao}
        titulo="Inativar este paciente?"
        descricao={`${paciente.nomeCompleto} deixa de aparecer nas buscas de agendamento. O histórico e o prontuário são preservados (o CFP exige guardar o prontuário por no mínimo 5 anos) e você pode reativar quando quiser.`}
        rotuloConfirmar="Inativar"
        carregando={alterarStatus.isPending}
        aoConfirmar={() => alterarStatus.mutate(false)}
      />
    </Pagina>
  );
}
