import { CalendarPlus, Repeat } from 'lucide-react';

import { EsqueletoLinhas } from '../../../components/feedback/esqueleto';
import { EstadoErro, EstadoVazio } from '../../../components/feedback/estados';
import { Secao } from '../../../components/layout/pagina';
import { BotaoLink } from '../../../components/ui/botao';
import { EstadoRotulado } from '../../../components/ui/marca-estado';
import { CabecalhoTabela, CelulaTabela, ColunaTabela, LinhaTabela, LinkDaLinha, Tabela } from '../../../components/ui/tabela';
import { descreverEstado, rotuloDe } from '../../../core/dominio/estado';
import { mensagemDeErro } from '../../../core/erros/mensagem-de-erro';
import { formatarData, formatarDiaSemanaCurto, formatarIntervaloHora } from '../../../core/formatacao/datas';
import { capitalizar } from '../../../core/formatacao/texto';
import { estadosConsulta, rotulosModalidade } from '../../agenda/agenda.rotulos';
import type { Consulta } from '../../agenda/agenda.types';
import { useConsultasDoPaciente } from '../ficha-paciente.dados';

function TabelaConsultas({ consultas, rotulo }: { consultas: Consulta[]; rotulo: string }) {
  return (
    <Tabela rotulo={rotulo}>
      <CabecalhoTabela>
        <tr>
          <ColunaTabela>Quando</ColunaTabela>
          <ColunaTabela ocultarAte="md">Atendimento</ColunaTabela>
          <ColunaTabela ocultarAte="sm">Estado</ColunaTabela>
        </tr>
      </CabecalhoTabela>
      <tbody>
        {consultas.map((consulta) => {
          const estado = descreverEstado(estadosConsulta, consulta.status);
          return (
            <LinhaTabela key={consulta.id}>
              <CelulaTabela>
                <LinkDaLinha to={`/consultas/${consulta.id}`}>
                  <span className="num">
                    {capitalizar(formatarDiaSemanaCurto(consulta.inicioEm))}, {formatarData(consulta.inicioEm)}
                  </span>
                </LinkDaLinha>
                <span className="num block text-sm text-ink-muted">
                  {formatarIntervaloHora(consulta.inicioEm, consulta.fimEm)}
                  {consulta.serieId ? (
                    <span className="ml-2 inline-flex items-center gap-1 align-middle">
                      <Repeat size={13} aria-hidden="true" />
                      <span>série</span>
                    </span>
                  ) : null}
                </span>
                <span className="mt-1 block sm:hidden">
                  <EstadoRotulado estado={estado} compacto />
                </span>
              </CelulaTabela>
              <CelulaTabela ocultarAte="md" className="text-ink-muted">
                {consulta.tipoAtendimento} · {rotuloDe(rotulosModalidade, consulta.modalidade)}
              </CelulaTabela>
              <CelulaTabela ocultarAte="sm">
                <EstadoRotulado estado={estado} compacto />
              </CelulaTabela>
            </LinhaTabela>
          );
        })}
      </tbody>
    </Tabela>
  );
}

/** Próximas e anteriores, num só lugar: a linha do tempo administrativa do acompanhamento. */
export function ConsultasDoPaciente({ pacienteId, podeAgendar = true }: { pacienteId: string; podeAgendar?: boolean }) {
  const { consultas, isPending, error, refetch } = useConsultasDoPaciente(pacienteId);
  if (isPending) return <EsqueletoLinhas quantidade={5} />;
  if (error) return <EstadoErro mensagem={mensagemDeErro(error, 'Tente novamente em instantes.')} aoTentarNovamente={() => void refetch()} />;

  const agora = new Date();
  const proximas = consultas.filter((consulta) => new Date(consulta.fimEm) > agora).sort((a, b) => Date.parse(a.inicioEm) - Date.parse(b.inicioEm));
  const anteriores = consultas.filter((consulta) => new Date(consulta.fimEm) <= agora).sort((a, b) => Date.parse(b.inicioEm) - Date.parse(a.inicioEm));

  if (consultas.length === 0) {
    return (
      <EstadoVazio
        icone={CalendarPlus}
        titulo="Nenhuma consulta no último ano"
        descricao={podeAgendar ? 'Agende a primeira consulta. Para acompanhamento contínuo, repita toda semana.' : 'Este paciente está inativo. Reative o cadastro para agendar novas consultas.'}
        acao={
          podeAgendar ? <BotaoLink to={`/consultas/nova?pacienteId=${pacienteId}`} variante="secundario">
            Agendar consulta
          </BotaoLink> : undefined
        }
      />
    );
  }

  return (
    <div className="grid gap-8">
      <Secao titulo="Próximas" descricao={proximas.length === 0 ? 'Nada agendado daqui para frente.' : undefined}>
        {proximas.length > 0 ? <TabelaConsultas consultas={proximas} rotulo="Próximas consultas do paciente" /> : null}
      </Secao>
      <Secao titulo="Anteriores" descricao="Últimos 12 meses.">
        {anteriores.length > 0 ? (
          <TabelaConsultas consultas={anteriores} rotulo="Consultas anteriores do paciente" />
        ) : (
          <p className="text-base text-ink-muted">Nenhuma consulta anterior.</p>
        )}
      </Secao>
    </div>
  );
}
