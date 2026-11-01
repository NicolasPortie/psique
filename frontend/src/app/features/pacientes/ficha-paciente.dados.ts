import { useQuery } from '@tanstack/react-query';

import { adicionarDias } from '../../core/formatacao/datas';
import { agendaKeys, listarConsultas } from '../agenda/agenda.api';
import type { Consulta } from '../agenda/agenda.types';
import { listarPagamentos, pagamentosKeys } from '../pagamentos/pagamentos.api';

/** Janela da ficha: um ano para trás e seis meses para frente (a API aceita até 366 dias por pedido). */
export function periodoDaFicha(hoje = new Date()) {
  return { inicio: adicionarDias(hoje, -365), fim: adicionarDias(hoje, 180) };
}

export function useConsultasDoPaciente(pacienteId: string, habilitado = true) {
  const { inicio, fim } = periodoDaFicha();
  const passado = useQuery({
    queryKey: agendaKeys.consultas(inicio.toISOString().slice(0, 10), new Date().toISOString().slice(0, 10), pacienteId),
    queryFn: ({ signal }) => listarConsultas(inicio.toISOString(), new Date().toISOString(), { signal, pacienteId }),
    enabled: Boolean(pacienteId) && habilitado,
  });
  const futuro = useQuery({
    queryKey: agendaKeys.consultas(new Date().toISOString().slice(0, 10), fim.toISOString().slice(0, 10), pacienteId),
    queryFn: ({ signal }) => listarConsultas(new Date().toISOString(), fim.toISOString(), { signal, pacienteId }),
    enabled: Boolean(pacienteId) && habilitado,
  });
  const todas = [...(passado.data ?? []), ...(futuro.data ?? [])].filter(
    (consulta, indice, lista) => lista.findIndex((outra) => outra.id === consulta.id) === indice,
  );
  return {
    consultas: todas,
    isPending: passado.isPending || futuro.isPending,
    error: passado.error ?? futuro.error,
    refetch: () => Promise.all([passado.refetch(), futuro.refetch()]),
  };
}

/** Contagens de apresentação para o resumo da ficha. */
export function resumirConsultas(consultas: Consulta[], agora = new Date()) {
  const realizadas = consultas.filter((consulta) => consulta.status === 'Realizada');
  const faltas = consultas.filter((consulta) => consulta.status === 'NaoCompareceu').length;
  const proxima = consultas
    .filter((consulta) => new Date(consulta.fimEm) > agora && ['Agendada', 'AguardandoConfirmacao', 'Confirmada'].includes(consulta.status))
    .sort((a, b) => Date.parse(a.inicioEm) - Date.parse(b.inicioEm))[0];
  const primeira = [...realizadas].sort((a, b) => Date.parse(a.inicioEm) - Date.parse(b.inicioEm))[0];
  return { realizadas: realizadas.length, faltas, proxima: proxima ?? null, primeira: primeira ?? null };
}


export function useFinanceiroDoPaciente(pacienteId: string, habilitado = true, pagina = 1) {
  const filtro = { pacienteId, pagina, tamanhoPagina: 50 };
  return useQuery({
    queryKey: pagamentosKeys.lista(filtro),
    queryFn: ({ signal }) => listarPagamentos(filtro, { signal }),
    enabled: Boolean(pacienteId) && habilitado,
  });
}
