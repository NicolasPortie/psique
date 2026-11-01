import { FileText, Wallet } from 'lucide-react';

import { EsqueletoLinhas } from '../../../components/feedback/esqueleto';
import { EstadoErro, EstadoVazio } from '../../../components/feedback/estados';
import { BotaoLink } from '../../../components/ui/botao';
import { MarcaEstado } from '../../../components/ui/marca-estado';
import { Paginacao } from '../../../components/ui/paginacao';
import { useParametrosUrl } from '../../../core/hooks/use-parametros-url';
import { mensagemDeErro } from '../../../core/erros/mensagem-de-erro';
import { formatarMoeda } from '../../../core/formatacao/moeda';
import { TabelaPagamentos } from '../../pagamentos/components/tabela-pagamentos';
import { estadosPagamento, estadoVencido } from '../../pagamentos/pagamentos.rotulos';
import { useFinanceiroDoPaciente } from '../ficha-paciente.dados';

/** Cobranças do paciente, com totais e as mesmas ações do Financeiro (receber, recibo, reembolsar). */
export function FinanceiroDoPaciente({ pacienteId }: { pacienteId: string }) {
  const { pagina, atualizar } = useParametrosUrl();
  const { data, isPending, error, refetch, isFetching } = useFinanceiroDoPaciente(pacienteId, true, pagina);
  if (isPending) return <EsqueletoLinhas quantidade={4} />;
  if (error || !data) return <EstadoErro mensagem={mensagemDeErro(error, 'Tente novamente em instantes.')} aoTentarNovamente={() => void refetch()} recarregando={isFetching} />;
  if (data.itens.length === 0) {
    return <EstadoVazio icone={Wallet} titulo="Nenhuma cobrança" descricao="As cobranças aparecem aqui quando você agenda com cobrança ou registra uma avulsa no Financeiro." />;
  }

  const totais = [
    { rotulo: 'Recebido', valor: data.resumo.recebido, estado: estadosPagamento.Pago },
    { rotulo: 'A receber', valor: data.resumo.pendente, estado: estadosPagamento.Pendente },
    { rotulo: 'Vencido', valor: data.resumo.vencido, estado: estadoVencido },
  ];

  const hoje = new Date();
  const mesAtual = `${hoje.getFullYear()}-${String(hoje.getMonth() + 1).padStart(2, '0')}`;
  const anterior = new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1);
  const mesAnterior = `${anterior.getFullYear()}-${String(anterior.getMonth() + 1).padStart(2, '0')}`;

  return (
    <div className="grid gap-5">
      <div className="flex flex-wrap justify-end gap-2">
        <BotaoLink to={`/pacientes/${pacienteId}/recibo-mensal?mes=${mesAnterior}`} variante="discreto">
          Recibo do mês anterior
        </BotaoLink>
        <BotaoLink to={`/pacientes/${pacienteId}/recibo-mensal?mes=${mesAtual}`} variante="secundario">
          <FileText size={16} aria-hidden="true" />
          Recibo deste mês
        </BotaoLink>
      </div>
      <dl className="grid gap-px overflow-hidden rounded-lg border border-line bg-line sm:grid-cols-3" aria-label="Totais do paciente">
        {totais.map(({ rotulo, valor, estado }) => (
          <div key={rotulo} className="flex items-center justify-between gap-3 bg-surface px-4 py-2.5 sm:grid sm:content-start sm:justify-normal sm:gap-1 sm:px-5 sm:py-3">
            <dt className="flex items-center gap-1.5 text-sm text-ink-muted">
              <MarcaEstado forma={estado.marca} tom={estado.tom} tamanho={14} />
              {rotulo}
            </dt>
            <dd className="num text-base font-semibold text-ink sm:text-lg">{formatarMoeda(valor)}</dd>
          </div>
        ))}
      </dl>
      <TabelaPagamentos pagamentos={data.itens} mostrarPaciente={false} rotulo="Cobranças do paciente" />
      <Paginacao pagina={data.pagina} total={data.total} tamanhoPagina={data.tamanhoPagina}
        aoMudarPagina={(proxima) => atualizar({ pagina: String(proxima) })} desabilitado={isFetching}
        rotulo="Paginação das cobranças do paciente" />
    </div>
  );
}
