import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarRange, ChevronLeft, ChevronRight, Download, Plus, Wallet } from 'lucide-react';
import { useEffect, useState } from 'react';

import { EsqueletoLinhas } from '../../components/feedback/esqueleto';
import { EstadoErro, EstadoVazio } from '../../components/feedback/estados';
import { notificar } from '../../components/feedback/notificar';
import { CabecalhoPagina, Pagina } from '../../components/layout/pagina';
import { Botao } from '../../components/ui/botao';
import { CampoBusca, GrupoSegmentado } from '../../components/ui/campo';
import { Confirmacao } from '../../components/ui/dialogo';
import { MenuAcoes } from '../../components/ui/menu';
import { MarcaEstado } from '../../components/ui/marca-estado';
import { Paginacao } from '../../components/ui/paginacao';
import { mensagemDeErro } from '../../core/erros/mensagem-de-erro';
import { useDebounce } from '../../core/hooks/use-debounce';
import { useParametrosUrl } from '../../core/hooks/use-parametros-url';
import { formatarMesAno, paraCampoData } from '../../core/formatacao/datas';
import { formatarMoeda } from '../../core/formatacao/moeda';
import { capitalizar } from '../../core/formatacao/texto';
import { GavetaPagamento } from './components/gaveta-pagamento';
import { TabelaPagamentos } from './components/tabela-pagamentos';
import { exportarRecebimentos, gerarMensalidades, listarPagamentos, pagamentosKeys, type ListarPagamentosFiltro } from './pagamentos.api';
import { baixarRecebimentosCsv } from './pagamentos.exportacao';
import { estadosPagamento, estadoVencido } from './pagamentos.rotulos';

const filtrosEstado = ['Pendente', 'Vencidos', 'Pago', 'Reembolsado', 'Cancelado'] as const;
const TAMANHO_PAGINA = 20;

/** "2026-10" para o mês de referência; vazio = mês atual; "todos" = sem recorte de período. */
function lerMes(valor: string | null): Date | null {
  if (valor === 'todos') return null;
  const [ano, mes] = (valor ?? '').split('-').map(Number);
  if (ano && mes && mes >= 1 && mes <= 12) return new Date(ano, mes - 1, 1);
  const hoje = new Date();
  return new Date(hoje.getFullYear(), hoje.getMonth(), 1);
}

const chaveMes = (data: Date) => `${data.getFullYear()}-${String(data.getMonth() + 1).padStart(2, '0')}`;

export default function PagamentosPage() {
  const { params, atualizar, lerOpcao, pagina } = useParametrosUrl();
  const queryClient = useQueryClient();
  const [criando, setCriando] = useState(false);
  const [confirmandoMensalidades, setConfirmandoMensalidades] = useState(false);
  const [busca, setBusca] = useState(params.get('busca') ?? '');
  const buscaAtrasada = useDebounce(busca);

  useEffect(() => {
    if ((params.get('busca') ?? '') !== buscaAtrasada) atualizar({ busca: buscaAtrasada || null, pagina: null });
  }, [buscaAtrasada, params, atualizar]);

  const estado = lerOpcao('estado', filtrosEstado);
  const mes = lerMes(params.get('mes'));
  const fimMes = mes ? new Date(mes.getFullYear(), mes.getMonth() + 1, 0) : null;

  const filtro: ListarPagamentosFiltro = {
    status: estado && estado !== 'Vencidos' ? estado : '',
    vencidos: estado === 'Vencidos',
    de: mes ? paraCampoData(mes) : undefined,
    ate: fimMes ? paraCampoData(fimMes) : undefined,
    busca: buscaAtrasada || undefined,
    pagina,
    tamanhoPagina: TAMANHO_PAGINA,
  };

  const listaQuery = useQuery({
    queryKey: pagamentosKeys.lista(filtro),
    queryFn: ({ signal }) => listarPagamentos(filtro, { signal }),
    placeholderData: keepPreviousData,
  });

  const { data, isPending, error, isFetching, isPlaceholderData, refetch } = listaQuery;
  const resumo = data?.resumo;
  const filtrado = estado !== '' || Boolean(buscaAtrasada);

  function irParaMes(deslocamento: number) {
    const base = mes ?? lerMes('')!;
    atualizar({ mes: chaveMes(new Date(base.getFullYear(), base.getMonth() + deslocamento, 1)), pagina: null });
  }

  const mesReferencia = mes ?? lerMes('')!;
  const rotuloMes = formatarMesAno(mesReferencia);

  const mensalidades = useMutation({
    mutationFn: () => gerarMensalidades(mesReferencia.getFullYear(), mesReferencia.getMonth() + 1),
    onSuccess: async (resultado) => {
      await queryClient.invalidateQueries({ queryKey: pagamentosKeys.todos });
      setConfirmandoMensalidades(false);
      if (resultado.criadas === 0 && resultado.semValor.length === 0) {
        notificar.info(resultado.jaExistiam > 0 ? 'As mensalidades deste mês já tinham sido geradas.' : 'Nenhum paciente com cobrança mensal.');
      } else {
        notificar.sucesso(`${resultado.criadas} ${resultado.criadas === 1 ? 'mensalidade gerada' : 'mensalidades geradas'}.`);
      }
      if (resultado.semValor.length > 0) notificar.info(`Sem valor combinado: ${resultado.semValor.join(', ')}. Complete na ficha.`);
    },
    onError: (erro) => {
      setConfirmandoMensalidades(false);
      notificar.erro(mensagemDeErro(erro, 'Não foi possível gerar as mensalidades.'));
    },
  });

  const exportar = useMutation({
    mutationFn: () => {
      const hoje = new Date();
      const inicio = mes ?? new Date(hoje.getFullYear(), hoje.getMonth() - 11, 1);
      return exportarRecebimentos(paraCampoData(inicio), paraCampoData(fimMes ?? hoje));
    },
    onSuccess: (itens) => {
      if (itens.length === 0) {
        notificar.info('Nenhum recebimento no período.');
        return;
      }
      baixarRecebimentosCsv(itens, mes ? `recebimentos-${chaveMes(mes)}.csv` : 'recebimentos.csv');
      notificar.sucesso('Planilha baixada. A exportação ficou registrada na auditoria.');
    },
    onError: (erro) => notificar.erro(mensagemDeErro(erro, 'Não foi possível exportar os recebimentos.')),
  });

  const totais = resumo
    ? [
        { rotulo: 'Recebido', valor: resumo.recebido, estado: estadosPagamento.Pago },
        { rotulo: 'A receber', valor: resumo.pendente, estado: estadosPagamento.Pendente },
        { rotulo: 'Vencido', valor: resumo.vencido, estado: estadoVencido },
      ]
    : [];

  return (
    <Pagina largura="ampla" className="gap-6">
      <CabecalhoPagina
        titulo="Financeiro"
        descricao="Cobranças das sessões e avulsas. Receba com um toque e emita o recibo quando o paciente pedir."
        acoes={
          <>
            <MenuAcoes
              rotulo="Mais ações do financeiro"
              itens={[
                { rotulo: `Gerar mensalidades de ${rotuloMes}`, icone: <CalendarRange size={16} />, aoSelecionar: () => setConfirmandoMensalidades(true) },
                {
                  rotulo: mes ? `Exportar recebimentos de ${rotuloMes}` : 'Exportar recebimentos dos últimos 12 meses',
                  icone: <Download size={16} />,
                  aoSelecionar: () => exportar.mutate(),
                },
              ]}
            />
            <Botao variante="primario" onClick={() => setCriando(true)}>
              <Plus size={16} aria-hidden="true" />
              Nova cobrança
            </Botao>
          </>
        }
      />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-1" role="group" aria-label="Período">
          {mes ? (
            <>
              <Botao variante="secundario" tamanho="icone" aria-label="Mês anterior" onClick={() => irParaMes(-1)}>
                <ChevronLeft size={16} aria-hidden="true" />
              </Botao>
              <span className="num min-w-36 px-1 text-center text-base font-semibold text-ink sm:min-w-40 sm:px-2" aria-live="polite">
                {capitalizar(formatarMesAno(mes))}
              </span>
              <Botao variante="secundario" tamanho="icone" aria-label="Próximo mês" onClick={() => irParaMes(1)}>
                <ChevronRight size={16} aria-hidden="true" />
              </Botao>
              <Botao variante="discreto" tamanho="pequeno" className="ml-1" onClick={() => atualizar({ mes: 'todos', pagina: null })}>
                Todo o período
              </Botao>
            </>
          ) : (
            <>
              <span className="px-1 text-base font-semibold text-ink">Todo o período</span>
              <Botao variante="discreto" tamanho="pequeno" onClick={() => atualizar({ mes: null, pagina: null })}>
                Ver por mês
              </Botao>
            </>
          )}
        </div>
        <div className="w-full sm:w-64">
          <CampoBusca valor={busca} aoMudar={setBusca} placeholder="Buscar paciente" aria-label="Buscar paciente" />
        </div>
      </div>

      {totais.length > 0 ? (
        <dl className="grid gap-px overflow-hidden rounded-lg border border-line bg-line sm:grid-cols-3" aria-label="Totais do período">
          {totais.map(({ rotulo, valor, estado: marca }) => (
            <div key={rotulo} className="flex items-center justify-between gap-3 bg-surface px-4 py-2.5 sm:grid sm:content-start sm:justify-normal sm:gap-1 sm:px-5 sm:py-3.5">
              <dt className="flex items-center gap-1.5 text-sm text-ink-muted">
                <MarcaEstado forma={marca.marca} tom={marca.tom} tamanho={14} />
                {rotulo}
              </dt>
              <dd className="num text-base font-semibold text-ink sm:text-xl">{formatarMoeda(valor)}</dd>
            </div>
          ))}
        </dl>
      ) : null}

      <GrupoSegmentado
        rotulo="Filtrar por estado"
        valor={estado}
        aoMudar={(valor) => atualizar({ estado: valor || null, pagina: null })}
        opcoes={[
          { valor: '', rotulo: 'Todos' },
          { valor: 'Pendente', rotulo: 'A receber' },
          { valor: 'Vencidos', rotulo: 'Vencidos' },
          { valor: 'Pago', rotulo: 'Recebidos' },
          { valor: 'Reembolsado', rotulo: 'Reembolsados' },
          { valor: 'Cancelado', rotulo: 'Cancelados' },
        ]}
      />

      {isPending ? (
        <EsqueletoLinhas quantidade={8} />
      ) : error && !data ? (
        <EstadoErro titulo="Financeiro indisponível" mensagem={mensagemDeErro(error, 'Tente novamente em instantes.')} aoTentarNovamente={() => void refetch()} recarregando={isFetching} />
      ) : data && data.itens.length === 0 ? (
        <EstadoVazio
          icone={Wallet}
          titulo={filtrado ? 'Nada neste filtro' : 'Nenhuma cobrança neste período'}
          descricao={filtrado ? 'Mude o estado, a busca ou o mês para ver as demais cobranças.' : 'Registre cobranças avulsas ou crie a cobrança ao agendar uma consulta.'}
          acao={
            filtrado ? (
              <Botao
                variante="secundario"
                onClick={() => {
                  setBusca('');
                  atualizar({ estado: null, busca: null, pagina: null });
                }}
              >
                Limpar filtros
              </Botao>
            ) : (
              <Botao variante="secundario" onClick={() => setCriando(true)}>
                Nova cobrança
              </Botao>
            )
          }
        />
      ) : data ? (
        <div aria-busy={isFetching} className={isPlaceholderData ? 'opacity-60 transition-opacity duration-150' : 'transition-opacity duration-150'}>
          <TabelaPagamentos pagamentos={data.itens} />
          <Paginacao
            className="mt-2"
            pagina={data.pagina}
            total={data.total}
            tamanhoPagina={data.tamanhoPagina}
            aoMudarPagina={(proxima) => atualizar({ pagina: proxima === 1 ? null : String(proxima) })}
            desabilitado={isFetching}
          />
        </div>
      ) : null}

      <GavetaPagamento aberto={criando} aoMudar={setCriando} />

      <Confirmacao
        aberto={confirmandoMensalidades}
        aoMudar={setConfirmandoMensalidades}
        titulo={`Gerar mensalidades de ${rotuloMes}?`}
        descricao="Cria uma cobrança para cada paciente ativo com cobrança mensal, com o valor combinado e vencimento no dia 10. Quem já tem a mensalidade do mês fica como está."
        rotuloConfirmar="Gerar mensalidades"
        carregando={mensalidades.isPending}
        aoConfirmar={() => mensalidades.mutate()}
      />
    </Pagina>
  );
}
