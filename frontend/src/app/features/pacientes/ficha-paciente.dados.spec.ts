import { describe, expect, it } from 'vitest';
import type { Consulta } from '../agenda/agenda.types';
import { resumirConsultas } from './ficha-paciente.dados';

const consulta = (id: string, inicioEm: string): Consulta => ({ id, inicioEm, fimEm: new Date(new Date(inicioEm).getTime() + 50 * 60000).toISOString(),
  pacienteId: 'p1', paciente: 'Ana', tipoAtendimentoId: 't1', tipoAtendimento: 'Sessão', status: 'Confirmada', modalidade: 'Presencial',
  linkOnline: null, enderecoAtendimento: null, observacaoAdministrativa: null });

describe('resumirConsultas', () => {
  it('ordena pelo instante mesmo com offsets diferentes', () => {
    const maisTarde = consulta('tarde', '2026-10-31T09:00:00-05:00');
    const maisCedo = consulta('cedo', '2026-10-31T10:00:00-03:00');
    expect(resumirConsultas([maisTarde, maisCedo], new Date('2026-10-31T12:00:00Z')).proxima?.id).toBe('cedo');
    expect(resumirConsultas([{ ...maisTarde, status: 'Realizada' }, { ...maisCedo, status: 'Realizada' }]).primeira?.id).toBe('cedo');
  });
});
