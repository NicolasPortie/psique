import { describe, expect, it } from 'vitest';

import { montarRecebimentosCsv } from './pagamentos.exportacao';

describe('montarRecebimentosCsv', () => {
  it.each(['=1+1', '+1+1', '-1+1', '@SUM(A1)', '  =1+1', '\t=1+1', '\r=1+1'])('exporta %j como texto, sem executar fórmula', (valor) => {
    const csv = montarRecebimentosCsv([
      { pagoEm: '2026-10-12T15:00:00Z', paciente: valor, pacienteCpf: null, valor: 180, formaPagamento: 'Pix', referencia: valor },
    ]);
    expect(csv).toContain(`'${valor}`);
  });

  it('protege retorno de carro dentro de uma célula', () => {
    const csv = montarRecebimentosCsv([
      { pagoEm: '2026-10-12T15:00:00Z', paciente: 'Ana\rSilva', pacienteCpf: null, valor: 180, formaPagamento: 'Pix', referencia: 'Sessão' },
    ]);
    expect(csv).toContain('"Ana\rSilva"');
  });

  it('usa ponto e vírgula, vírgula decimal e protege campos com separador', () => {
    const csv = montarRecebimentosCsv([
      { pagoEm: '2026-10-12T15:00:00Z', paciente: 'Ana; Silva', pacienteCpf: '123.456.789-00', valor: 180, formaPagamento: 'Pix', referencia: 'Sessão de 12/10/2026' },
    ]);
    const [cabecalho, linha] = csv.replace('﻿', '').split('\r\n');
    expect(cabecalho).toBe('Data do recebimento;Paciente;CPF;Valor;Forma;Referência');
    expect(linha).toBe('12/10/2026;"Ana; Silva";123.456.789-00;180,00;Pix;Sessão de 12/10/2026');
  });
});
