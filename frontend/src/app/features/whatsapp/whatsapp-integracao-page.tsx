import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MessagesSquare, PlugZap } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { Aviso } from '../../components/feedback/aviso';
import { CarregandoPagina } from '../../components/feedback/esqueleto';
import { EstadoErro } from '../../components/feedback/estados';
import { notificar } from '../../components/feedback/notificar';
import { CabecalhoPagina, Pagina, Secao } from '../../components/layout/pagina';
import { Botao, BotaoLink } from '../../components/ui/botao';
import { Campo, Entrada, OpcaoMarcavel } from '../../components/ui/campo';
import { Dado, Ficha, ItemLista, ListaRegulada } from '../../components/ui/lista';
import { EstadoRotulado } from '../../components/ui/marca-estado';
import { descreverEstado } from '../../core/dominio/estado';
import { mensagemDeErro } from '../../core/erros/mensagem-de-erro';
import { formatarDataHora } from '../../core/formatacao/datas';
import {
  listarEventosBridge,
  obterIntegracaoWhatsApp,
  salvarIntegracaoWhatsApp,
  testarConexaoWhatsApp,
  whatsappKeys,
} from './whatsapp.api';
import { estadosIntegracao, rotuloEvento } from './whatsapp.rotulos';

const schema = z.object({
  nome: z.string().trim().min(2, 'Informe um nome para a integração.'),
  provedor: z.string().trim().min(2, 'Informe o provedor.'),
  urlBaseBridge: z.string().trim().min(8, 'Informe o endereço do Bridge.'),
  token: z.string(),
  instancia: z.string().trim().max(100),
  numeroWhatsApp: z.string().trim().min(8, 'Informe o número conectado.'),
  ativo: z.boolean(),
});
type Formulario = z.infer<typeof schema>;

const iniciais: Formulario = {
  nome: 'WhatsApp do consultório',
  provedor: 'Evolution API',
  instancia: '',
  urlBaseBridge: '',
  token: '',
  numeroWhatsApp: '',
  ativo: true,
};

export default function WhatsAppIntegracaoPage() {
  const queryClient = useQueryClient();
  const [erroApi, setErroApi] = useState('');

  const integracaoQuery = useQuery({
    queryKey: whatsappKeys.integracao(),
    queryFn: ({ signal }) => obterIntegracaoWhatsApp({ signal }),
  });
  const eventosQuery = useQuery({
    queryKey: whatsappKeys.eventos(),
    queryFn: ({ signal }) => listarEventosBridge({ signal }),
  });

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = useForm<Formulario>({ resolver: zodResolver(schema), defaultValues: iniciais });

  const integracao = integracaoQuery.data;
  useEffect(() => {
    if (integracao) {
      reset({
        nome: integracao.nome,
        provedor: integracao.provedor,
        instancia: integracao.instancia ?? '',
        urlBaseBridge: integracao.urlBaseBridge,
        token: '',
        numeroWhatsApp: integracao.numeroWhatsApp,
        ativo: integracao.ativo,
      });
    }
  }, [integracao, reset]);

  const salvar = useMutation({
    mutationFn: (valores: Formulario) =>
      salvarIntegracaoWhatsApp({
        nome: valores.nome.trim(),
        provedor: valores.provedor.trim(),
        instancia: valores.instancia.trim() || null,
        urlBaseBridge: valores.urlBaseBridge.trim(),
        token: valores.token.trim() || null,
        numeroWhatsApp: valores.numeroWhatsApp.trim(),
        ativo: valores.ativo,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: whatsappKeys.integracao() });
      notificar.sucesso('Integração salva.');
      setErroApi('');
    },
    onError: (erro) => setErroApi(mensagemDeErro(erro, 'Não foi possível salvar a integração.')),
  });

  const testar = useMutation({
    mutationFn: testarConexaoWhatsApp,
    onSuccess: async (resultado) => {
      await queryClient.invalidateQueries({ queryKey: whatsappKeys.integracao() });
      if (resultado.sucesso) notificar.sucesso(resultado.mensagem);
      else notificar.erro(resultado.mensagem);
    },
    onError: (erro) => notificar.erro(mensagemDeErro(erro, 'Não foi possível testar a conexão.')),
  });

  if (integracaoQuery.isPending) return <CarregandoPagina rotulo="Carregando a integração…" />;
  if (integracaoQuery.error && integracaoQuery.data === undefined) {
    return (
      <EstadoErro
        titulo="Integração indisponível"
        mensagem={mensagemDeErro(integracaoQuery.error, 'Tente novamente em instantes.')}
        aoTentarNovamente={() => void integracaoQuery.refetch()}
      />
    );
  }

  const eventos = (eventosQuery.data ?? []).slice(0, 15);
  const eventosComErro = eventos.filter((evento) => evento.erroProcessamento);

  return (
    <Pagina largura="padrao">
      <CabecalhoPagina
        titulo="Integração WhatsApp"
        descricao="Conecte o número do consultório para o assistente responder dúvidas de agenda e a equipe conversar com os pacientes."
        acoes={
          <BotaoLink to="/conversas" variante="secundario">
            <MessagesSquare size={16} aria-hidden="true" />
            Abrir conversas
          </BotaoLink>
        }
      />

      <div className="grid gap-x-14 gap-y-10 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <form
          className="grid content-start gap-10"
          noValidate
          onSubmit={(evento) => void handleSubmit((valores) => salvar.mutate(valores))(evento)}
        >
          <Secao
            titulo="Conexão"
            descricao={
              integracao
                ? undefined
                : 'Nenhum número conectado ainda. Preencha os dados abaixo para começar.'
            }
          >
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo
                rotulo="Número do WhatsApp"
                dica="Com código do país e DDD, só números."
                erro={errors.numeroWhatsApp?.message}
              >
                <Entrada
                  inputMode="tel"
                  placeholder="5511999999999"
                  {...register('numeroWhatsApp')}
                />
              </Campo>
              <Campo
                rotulo="Chave de acesso"
                dica={integracao ? 'Deixe em branco para manter a chave atual.' : undefined}
              >
                <Entrada type="password" autoComplete="new-password" {...register('token')} />
              </Campo>
              <Campo
                rotulo="Nome da instância"
                dica="Use o mesmo nome cadastrado na Evolution API."
                erro={errors.instancia?.message}
                className="sm:col-span-2"
              >
                <Entrada autoComplete="off" {...register('instancia')} />
              </Campo>
              <Campo
                rotulo="Endereço do serviço de conexão"
                dica="Endereço da Evolution API, informado por quem configurou o WhatsApp."
                erro={errors.urlBaseBridge?.message}
                className="sm:col-span-2"
              >
                <Entrada
                  type="url"
                  inputMode="url"
                  placeholder="https://"
                  {...register('urlBaseBridge')}
                />
              </Campo>
            </div>
            <details open={errors.nome || errors.provedor ? true : undefined}>
              <summary className="cursor-pointer text-sm font-medium text-accent-ink">
                Opções avançadas
              </summary>
              <div className="mt-3 grid gap-4 sm:grid-cols-2">
                <Campo rotulo="Nome da conexão" erro={errors.nome?.message}>
                  <Entrada {...register('nome')} />
                </Campo>
                <Campo rotulo="Provedor" erro={errors.provedor?.message}>
                  <Entrada {...register('provedor')} />
                </Campo>
              </div>
            </details>
            <OpcaoMarcavel
              tipo="interruptor"
              rotulo="Atendimento pelo WhatsApp ativo"
              descricao="Desativado, o consultório não recebe nem envia mensagens por este número."
              {...register('ativo')}
            />
            {erroApi ? <Aviso tom="perigo">{erroApi}</Aviso> : null}
            <div className="flex flex-wrap gap-2">
              <Botao
                type="submit"
                variante="primario"
                carregando={salvar.isPending}
                disabled={Boolean(integracao) && !isDirty}
              >
                Salvar integração
              </Botao>
              {integracao ? (
                <Botao
                  variante="secundario"
                  carregando={testar.isPending}
                  onClick={() => testar.mutate()}
                >
                  <PlugZap size={16} aria-hidden="true" />
                  Testar conexão
                </Botao>
              ) : null}
            </div>
          </Secao>
        </form>

        <aside className="grid content-start gap-10">
          {integracao ? (
            <Secao titulo="Situação" painel>
              <Ficha colunas={1}>
                <Dado rotulo="Situação">
                  <EstadoRotulado estado={descreverEstado(estadosIntegracao, integracao.status)} />
                </Dado>
                <Dado rotulo="Chave de acesso">
                  <span className="num">{integracao.tokenMascarado}</span>
                </Dado>
                <Dado rotulo="Última verificação">
                  <span className="num">
                    {formatarDataHora(integracao.ultimaConexaoTestadaEm, 'Ainda não verificada')}
                  </span>
                </Dado>
                {integracao.ultimoErro ? (
                  <Dado rotulo="Último erro">
                    <span className="text-danger-ink">{integracao.ultimoErro}</span>
                  </Dado>
                ) : null}
              </Ficha>
            </Secao>
          ) : null}

          {integracao ? (
            <Secao titulo="Atividade do WhatsApp">
              {eventosQuery.isPending ? (
                <p className="text-base text-ink-muted">Carregando…</p>
              ) : eventos.length === 0 ? (
                <p className="text-base text-ink-muted">
                  Nenhuma mensagem chegou ainda. Quando chegarem, o resumo aparece aqui.
                </p>
              ) : (
                <div className="grid gap-3">
                  <p className="text-base text-ink">
                    {eventosComErro.length === 0
                      ? 'As últimas mensagens foram processadas sem problemas.'
                      : eventosComErro.length === 1
                        ? '1 das últimas mensagens não foi processada.'
                        : `${eventosComErro.length} das últimas mensagens não foram processadas.`}
                  </p>
                  {eventosComErro.length > 0 ? (
                    <ListaRegulada>
                      {eventosComErro.map((evento) => (
                        <ItemLista key={evento.id} className="py-3">
                          <p className="text-base font-medium text-ink">
                            {rotuloEvento(evento.tipoEvento)}
                          </p>
                          <p className="num text-xs text-ink-muted">
                            {formatarDataHora(evento.criadoEm)}
                          </p>
                          <p className="text-sm text-danger-ink">{evento.erroProcessamento}</p>
                        </ItemLista>
                      ))}
                    </ListaRegulada>
                  ) : null}
                  <details>
                    <summary className="cursor-pointer text-sm font-medium text-accent-ink">
                      Ver as últimas {eventos.length}
                    </summary>
                    <ListaRegulada className="mt-2">
                      {eventos.map((evento) => (
                        <ItemLista key={evento.id} className="py-3">
                          <p className="flex items-center justify-between gap-2 text-base">
                            <span className="truncate text-ink">
                              {rotuloEvento(evento.tipoEvento)}
                            </span>
                            <EstadoRotulado
                              compacto
                              estado={
                                evento.erroProcessamento
                                  ? { rotulo: 'Com erro', tom: 'perigo', marca: 'x' }
                                  : evento.processado
                                    ? { rotulo: 'Processada', tom: 'sucesso', marca: 'check' }
                                    : { rotulo: 'Em espera', tom: 'aviso', marca: 'meio' }
                              }
                            />
                          </p>
                          <p className="num text-xs text-ink-muted">
                            {formatarDataHora(evento.criadoEm)}
                          </p>
                        </ItemLista>
                      ))}
                    </ListaRegulada>
                  </details>
                </div>
              )}
            </Secao>
          ) : null}
        </aside>
      </div>
    </Pagina>
  );
}
