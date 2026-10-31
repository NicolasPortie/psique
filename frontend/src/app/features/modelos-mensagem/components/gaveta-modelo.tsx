import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Eye } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { z } from 'zod';

import { Aviso } from '../../../components/feedback/aviso';
import { notificar } from '../../../components/feedback/notificar';
import { Botao } from '../../../components/ui/botao';
import { AreaTexto, Campo, Entrada, OpcaoMarcavel } from '../../../components/ui/campo';
import { Gaveta } from '../../../components/ui/dialogo';
import { mensagemDeErro } from '../../../core/erros/mensagem-de-erro';
import {
  atualizarModeloMensagem,
  criarModeloMensagem,
  listarVariaveisModelo,
  modelosKeys,
  previaModeloMensagem,
  type ModeloMensagem,
  type PreviaModelo,
} from '../modelos-mensagem.api';

const tipos = ['LembreteConsulta', 'ConfirmacaoConsulta', 'CancelamentoConsulta', 'RemarcacaoConsulta', 'PagamentoPendente'] as const;
const canais = ['Email', 'WhatsApp', 'Sms', 'Sistema', 'Telefone'] as const;

const schema = z
  .object({
    nome: z.string().trim().min(2, 'Informe o nome do modelo.'),
    tipo: z.enum(tipos),
    canal: z.enum(canais),
    assunto: z.string(),
    conteudo: z.string().trim().min(1, 'Escreva o conteúdo da mensagem.'),
    ativo: z.boolean(),
  })
  .refine((dados) => dados.canal !== 'Email' || dados.assunto.trim().length > 0, { path: ['assunto'], message: 'Informe o assunto do e-mail.' });
type Formulario = z.infer<typeof schema>;

const iniciais: Formulario = { nome: '', tipo: 'LembreteConsulta', canal: 'Email', assunto: '', conteudo: '', ativo: true };

function paraFormulario(modelo: ModeloMensagem): Formulario {
  return {
    nome: modelo.nome,
    tipo: modelo.tipo,
    canal: modelo.canal,
    assunto: modelo.assunto ?? '',
    conteudo: modelo.conteudo,
    ativo: modelo.ativo,
  };
}

interface GavetaModeloProps {
  modelo: ModeloMensagem | null;
  aberto: boolean;
  aoMudar: (aberto: boolean) => void;
}

export function GavetaModelo({ modelo, aberto, aoMudar }: GavetaModeloProps) {
  const queryClient = useQueryClient();
  const areaRef = useRef<HTMLTextAreaElement | null>(null);

  const {
    register,
    control,
    handleSubmit,
    reset,
    getValues,
    setValue,
    formState: { errors },
  } = useForm<Formulario>({ resolver: zodResolver(schema), defaultValues: iniciais });
  const { ref: refConteudo, ...camposConteudo } = register('conteudo');
  const variaveisQuery = useQuery({ queryKey: modelosKeys.variaveis(), queryFn: ({ signal }) => listarVariaveisModelo({ signal }), enabled: aberto });
  const canal = useWatch({ control, name: 'canal' });
  const conteudo = useWatch({ control, name: 'conteudo' });

  const salvar = useMutation({
    mutationFn: (valores: Formulario) => {
      const requisicao = {
        nome: valores.nome.trim(),
        tipo: valores.tipo,
        canal: valores.canal,
        assunto: valores.assunto.trim() || null,
        conteudo: valores.conteudo,
        ativo: valores.ativo,
      };
      return modelo ? atualizarModeloMensagem(modelo.id, requisicao) : criarModeloMensagem(requisicao);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: modelosKeys.todos });
      notificar.sucesso(modelo ? 'Modelo atualizado.' : 'Modelo criado.');
      aoMudar(false);
    },
  });

  const gerarPrevia = useMutation({
    mutationFn: () => previaModeloMensagem(getValues('assunto').trim() || null, getValues('conteudo')),
  });
  const previa: PreviaModelo | null = gerarPrevia.data ?? null;
  const erroApi = salvar.isError
    ? mensagemDeErro(salvar.error, 'Não foi possível salvar o modelo.')
    : gerarPrevia.isError
      ? mensagemDeErro(gerarPrevia.error, 'Não foi possível gerar a prévia.')
      : '';
  const { reset: limparSalvar } = salvar;
  const { reset: limparPrevia } = gerarPrevia;

  useEffect(() => {
    if (aberto) {
      reset(modelo ? paraFormulario(modelo) : iniciais);
      limparSalvar();
      limparPrevia();
    }
  }, [aberto, modelo, reset, limparSalvar, limparPrevia]);

  /** Insere a variável onde o cursor está, sem perder o foco no texto. */
  function inserirVariavel(marcador: string) {
    const area = areaRef.current;
    const atual = getValues('conteudo');
    const inicio = area?.selectionStart ?? atual.length;
    const fim = area?.selectionEnd ?? inicio;
    setValue('conteudo', `${atual.slice(0, inicio)}${marcador}${atual.slice(fim)}`, { shouldDirty: true, shouldValidate: true });
    requestAnimationFrame(() => {
      area?.focus();
      area?.setSelectionRange(inicio + marcador.length, inicio + marcador.length);
    });
  }

  return (
    <Gaveta
      aberto={aberto}
      aoMudar={aoMudar}
      largura="34rem"
      titulo={modelo ? 'Editar modelo' : 'Novo modelo de mensagem'}
      descricao="O texto do e-mail de lembrete que o paciente recebe antes da consulta."
      rodape={
        <>
          <Botao variante="secundario" onClick={() => aoMudar(false)}>
            Cancelar
          </Botao>
          <Botao type="submit" form="form-modelo" variante="primario" carregando={salvar.isPending}>
            Salvar modelo
          </Botao>
        </>
      }
    >
      <form
        id="form-modelo"
        className="grid gap-4"
        noValidate
        onSubmit={(evento) => void handleSubmit((valores) => salvar.mutate(valores))(evento)}
      >
        <Campo rotulo="Nome" erro={errors.nome?.message}>
          <Entrada placeholder="Ex.: Lembrete de consulta" {...register('nome')} />
        </Campo>
        {canal === 'Email' ? (
          <Campo rotulo="Assunto do e-mail" erro={errors.assunto?.message}>
            <Entrada placeholder="Ex.: Lembrete da sua consulta" {...register('assunto')} />
          </Campo>
        ) : null}
        <Campo rotulo="Mensagem" erro={errors.conteudo?.message}>
          <AreaTexto
            rows={7}
            {...camposConteudo}
            ref={(elemento) => {
              refConteudo(elemento);
              areaRef.current = elemento;
            }}
          />
        </Campo>

        {variaveisQuery.data?.length ? (
          <div className="grid gap-2">
            <p className="text-sm font-medium text-ink">Inserir variável</p>
            <ul className="flex flex-wrap gap-1.5">
              {variaveisQuery.data.map((variavel) => (
                <li key={variavel.chave}>
                  <button
                    type="button"
                    title={variavel.descricao}
                    onClick={() => inserirVariavel(variavel.marcador)}
                    className="num inline-flex h-7 items-center rounded-sm border border-line-strong bg-field px-2 text-xs text-ink transition-[background-color,scale] duration-150 ease-out hover:bg-hover active:scale-[0.96] pointer-coarse:h-10"
                  >
                    {variavel.marcador}
                  </button>
                </li>
              ))}
            </ul>
            <p className="text-xs text-ink-muted">Cada variável é substituída pelos dados reais no momento do envio.</p>
          </div>
        ) : null}

        <OpcaoMarcavel tipo="interruptor" rotulo="Modelo ativo" descricao="Modelos inativos não são usados em novos envios." {...register('ativo')} />

        <div className="grid gap-3 border-t border-line pt-4">
          <Botao variante="secundario" className="w-fit" carregando={gerarPrevia.isPending} onClick={() => gerarPrevia.mutate()} disabled={!conteudo.trim()}>
            <Eye size={16} aria-hidden="true" />
            Ver prévia com dados de exemplo
          </Botao>
          {previa ? (
            <div className="grid gap-1.5 rounded-md bg-rail p-3 text-base" aria-live="polite">
              {previa.assunto ? <p className="font-medium text-ink">{previa.assunto}</p> : null}
              <p className="whitespace-pre-wrap text-ink">{previa.conteudo}</p>
            </div>
          ) : null}
        </div>

        {erroApi ? <Aviso tom="perigo">{erroApi}</Aviso> : null}
      </form>
    </Gaveta>
  );
}
