import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MessageSquareText, Plus } from 'lucide-react';
import { useState } from 'react';

import { EsqueletoLinhas } from '../../components/feedback/esqueleto';
import { EstadoErro, EstadoVazio } from '../../components/feedback/estados';
import { notificar } from '../../components/feedback/notificar';
import { CabecalhoPagina, Pagina } from '../../components/layout/pagina';
import { Botao } from '../../components/ui/botao';
import { EstadoRotulado } from '../../components/ui/marca-estado';
import { MenuAcoes } from '../../components/ui/menu';
import { CabecalhoTabela, CelulaTabela, ColunaTabela, LinhaTabela, Tabela } from '../../components/ui/tabela';
import { mensagemDeErro } from '../../core/erros/mensagem-de-erro';
import { estadoAtivo } from '../agenda/agenda.rotulos';
import { GavetaModelo } from './components/gaveta-modelo';
import { alterarAtivoModelo, listarModelosMensagem, modelosKeys, type ModeloMensagem } from './modelos-mensagem.api';

export default function ModelosMensagemPage() {
  const queryClient = useQueryClient();
  const [edicao, setEdicao] = useState<{ modelo: ModeloMensagem | null } | null>(null);
  const listaQuery = useQuery({ queryKey: modelosKeys.lista(), queryFn: ({ signal }) => listarModelosMensagem({ signal }) });

  const alternar = useMutation({
    mutationFn: (modelo: ModeloMensagem) => alterarAtivoModelo(modelo.id, !modelo.ativo),
    onSuccess: async (modelo) => {
      await queryClient.invalidateQueries({ queryKey: modelosKeys.todos });
      notificar.sucesso(modelo.ativo ? 'Modelo ativado.' : 'Modelo inativado.');
    },
    onError: (erro) => notificar.erro(mensagemDeErro(erro, 'Não foi possível alterar o modelo.')),
  });

  const modelos = listaQuery.data ?? [];

  return (
    <Pagina largura="ampla" className="gap-6">
      <CabecalhoPagina
        titulo="Modelos de mensagem"
        descricao="O texto do e-mail de lembrete enviado antes da consulta. Vale o modelo ativo editado por último."
        acoes={
          <Botao variante="primario" onClick={() => setEdicao({ modelo: null })}>
            <Plus size={16} aria-hidden="true" />
            Novo modelo
          </Botao>
        }
      />

      {listaQuery.isPending ? (
        <EsqueletoLinhas quantidade={5} />
      ) : listaQuery.error && !listaQuery.data ? (
        <EstadoErro titulo="Modelos indisponíveis" mensagem={mensagemDeErro(listaQuery.error, 'Tente novamente em instantes.')} aoTentarNovamente={() => void listaQuery.refetch()} recarregando={listaQuery.isFetching} />
      ) : modelos.length === 0 ? (
        <EstadoVazio
          icone={MessageSquareText}
          titulo="Nenhum modelo cadastrado"
          descricao="Sem modelo, o lembrete sai com um texto padrão. Crie o seu com variáveis como o nome do paciente e o horário."
          acao={
            <Botao variante="secundario" onClick={() => setEdicao({ modelo: null })}>
              Criar o primeiro modelo
            </Botao>
          }
        />
      ) : (
        <Tabela rotulo="Modelos de mensagem">
          <CabecalhoTabela>
            <tr>
              <ColunaTabela>Modelo</ColunaTabela>
              <ColunaTabela>Estado</ColunaTabela>
              <ColunaTabela className="w-px">
                <span className="sr-only">Ações</span>
              </ColunaTabela>
            </tr>
          </CabecalhoTabela>
          <tbody>
            {modelos.map((modelo) => (
              <LinhaTabela key={modelo.id}>
                <CelulaTabela>
                  <span className="block font-medium text-ink">{modelo.nome}</span>
                  <span className="block max-w-md truncate text-sm text-ink-muted">{modelo.conteudo}</span>
                </CelulaTabela>
                <CelulaTabela>
                  <EstadoRotulado estado={estadoAtivo(modelo.ativo)} compacto />
                </CelulaTabela>
                <CelulaTabela className="whitespace-nowrap text-right">
                  <div className="flex items-center justify-end gap-1">
                    <Botao variante="secundario" tamanho="pequeno" onClick={() => setEdicao({ modelo })} aria-label={`Editar ${modelo.nome}`}>
                      Editar
                    </Botao>
                    <MenuAcoes
                      rotulo={`Mais ações de ${modelo.nome}`}
                      itens={[{ rotulo: modelo.ativo ? 'Inativar' : 'Ativar', aoSelecionar: () => alternar.mutate(modelo) }]}
                    />
                  </div>
                </CelulaTabela>
              </LinhaTabela>
            ))}
          </tbody>
        </Tabela>
      )}

      <GavetaModelo modelo={edicao?.modelo ?? null} aberto={edicao !== null} aoMudar={(aberto) => !aberto && setEdicao(null)} />
    </Pagina>
  );
}
