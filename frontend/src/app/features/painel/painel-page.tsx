import { useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { Link } from 'react-router';

import { EstadoErro } from '../../components/feedback/estados';
import { CarregandoPagina } from '../../components/feedback/esqueleto';
import { BotaoLink } from '../../components/ui/botao';
import { PainelVazio } from '../../components/ui/lista';
import { CabecalhoPagina, Pagina, Secao } from '../../components/layout/pagina';
import { mensagemDeErro } from '../../core/erros/mensagem-de-erro';
import { formatarDiaCompleto } from '../../core/formatacao/datas';
import { capitalizar } from '../../core/formatacao/texto';
import { AgendaDeHoje } from './components/agenda-de-hoje';
import { Aniversarios } from './components/aniversarios';
import { FilaDeAtencao } from './components/fila-de-atencao';
import { PrimeirosPassos } from './components/primeiros-passos';
import { ProximaSessao } from './components/proxima-sessao';
import { SessoesSemDesfecho } from './components/sessoes-sem-desfecho';
import { ReguaDoDia } from './components/regua-do-dia';
import { SemanaEmBarras } from './components/semana-em-barras';
import { obterResumoPainel, painelKeys } from './painel.api';
import { resumoDoDia, saudacao } from './painel.apresentacao';

export default function PainelPage() {
  const { data, error, isPending, isFetching, refetch } = useQuery({
    queryKey: painelKeys.resumo(),
    queryFn: ({ signal }) => obterResumoPainel({ signal }),
  });

  if (isPending) return <CarregandoPagina rotulo="Carregando o seu dia…" />;
  if (error || !data) {
    return (
      <EstadoErro
        titulo="Não foi possível abrir o painel"
        mensagem={mensagemDeErro(error, 'Tente novamente em instantes.')}
        aoTentarNovamente={() => void refetch()}
        recarregando={isFetching}
      />
    );
  }

  const agora = new Date();
  const proxima = data.proximasConsultas?.[0] ?? null;
  const semDesfecho = data.consultasSemDesfecho ?? [];
  const semConsultasHoje = data.consultasHoje.length === 0;

  return (
    <Pagina largura="ampla">
      <CabecalhoPagina
        titulo={`${saudacao(agora.getHours())}${data.usuario.primeiroNome ? `, ${data.usuario.primeiroNome}` : ''}`}
        meta={capitalizar(formatarDiaCompleto(agora))}
        descricao={resumoDoDia(data.indicadores)}
        acoes={
          <>
            <BotaoLink to="/agenda" variante="secundario">
              Abrir agenda
            </BotaoLink>
            <BotaoLink to="/consultas/nova" variante="primario">
              <Plus size={16} aria-hidden="true" />
              Nova consulta
            </BotaoLink>
          </>
        }
      />

      <div className="grid gap-x-14 gap-y-10 xl:grid-cols-[minmax(0,1fr)_21rem] 3xl:grid-cols-[minmax(0,1fr)_26rem] 3xl:gap-x-20">
        <div className="grid content-start gap-10">
          {data.primeirosPassos ? <PrimeirosPassos estado={data.primeirosPassos} /> : null}
          {proxima ? <ProximaSessao consulta={proxima} /> : null}

          <Secao id="agenda-hoje" titulo="Agenda de hoje">
            {semConsultasHoje && proxima ? (
              <PainelVazio>Nenhuma consulta hoje. O dia está livre para organizar o consultório.</PainelVazio>
            ) : (
              <>
                <ReguaDoDia consultas={data.consultasHoje} />
                <AgendaDeHoje consultas={data.consultasHoje} />
              </>
            )}
          </Secao>

          <SessoesSemDesfecho consultas={semDesfecho} />
        </div>

        <div className="grid content-start gap-10">
          <Secao id="atencao" titulo="Atenção">
            <FilaDeAtencao prioridades={data.prioridades} />
          </Secao>

          <Aniversarios aniversariantes={data.aniversariantes ?? []} />

          <Secao id="semana" painel titulo="Próximos dias" acoes={<Link to="/agenda" className="text-sm font-medium">Ver agenda</Link>}>
            <SemanaEmBarras dias={data.visaoSemana} />
          </Secao>
        </div>
      </div>
    </Pagina>
  );
}
