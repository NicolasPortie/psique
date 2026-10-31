import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { Aviso } from '../../components/feedback/aviso';
import { notificar } from '../../components/feedback/notificar';
import { CabecalhoPagina, Pagina, Secao } from '../../components/layout/pagina';
import { Botao } from '../../components/ui/botao';
import { Campo, Entrada } from '../../components/ui/campo';
import { Dado, Ficha } from '../../components/ui/lista';
import { alterarSenha, useSessao } from '../../core/autenticacao/autenticacao.service';
import { rotuloPerfil } from '../../core/dominio/perfil';
import { mensagemDeErro } from '../../core/erros/mensagem-de-erro';

const schema = z
  .object({
    senhaAtual: z.string().min(1, 'Informe a senha atual.'),
    novaSenha: z.string().min(8, 'Use pelo menos 8 caracteres.').max(200, 'Use até 200 caracteres.'),
    confirmacao: z.string(),
  })
  .refine((dados) => dados.novaSenha === dados.confirmacao, { path: ['confirmacao'], message: 'As senhas não coincidem.' })
  .refine((dados) => dados.novaSenha !== dados.senhaAtual, { path: ['novaSenha'], message: 'A nova senha deve ser diferente da atual.' });
type Formulario = z.infer<typeof schema>;

const vazio: Formulario = { senhaAtual: '', novaSenha: '', confirmacao: '' };

/** Dados de acesso do usuário logado e troca de senha. Disponível a todos os perfis. */
export default function ContaPage() {
  const { usuario } = useSessao();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<Formulario>({ resolver: zodResolver(schema), defaultValues: vazio });

  const trocar = useMutation({
    mutationFn: ({ senhaAtual, novaSenha }: Formulario) => alterarSenha(senhaAtual, novaSenha),
    onSuccess: () => notificar.sucesso('Senha alterada. As outras sessões foram encerradas.'),
  });
  const { isSuccess, reset: limparMutacao } = trocar;
  const erroApi = trocar.isError ? mensagemDeErro(trocar.error, 'Não foi possível alterar a senha.') : '';

  useEffect(() => {
    if (isSuccess) {
      reset(vazio);
      limparMutacao();
    }
  }, [isSuccess, reset, limparMutacao]);

  return (
    <Pagina largura="estreita" className="gap-10">
      <CabecalhoPagina titulo="Conta e segurança" descricao="Seus dados de acesso e a senha da sua conta." />

      <Secao titulo="Acesso" painel>
        <Ficha colunas={2}>
          <Dado rotulo="Nome">{usuario?.nomeCompleto ?? '—'}</Dado>
          <Dado rotulo="Perfil">{rotuloPerfil(usuario?.perfil, usuario?.nomeDoPerfil) || '—'}</Dado>
          <Dado rotulo="E-mail" largo>
            {usuario?.email ?? '—'}
          </Dado>
        </Ficha>
      </Secao>

      <Secao
        titulo="Alterar senha"
        painel
        descricao="Ao trocar, você continua conectado aqui e as sessões abertas em outros dispositivos são encerradas."
      >
        <form className="grid gap-4" noValidate onSubmit={(evento) => void handleSubmit((valores) => trocar.mutate(valores))(evento)}>
          <Campo rotulo="Senha atual" erro={errors.senhaAtual?.message}>
            <Entrada type="password" autoComplete="current-password" {...register('senhaAtual')} />
          </Campo>
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Nova senha" dica="Pelo menos 8 caracteres." erro={errors.novaSenha?.message}>
              <Entrada type="password" autoComplete="new-password" {...register('novaSenha')} />
            </Campo>
            <Campo rotulo="Confirmar nova senha" erro={errors.confirmacao?.message}>
              <Entrada type="password" autoComplete="new-password" {...register('confirmacao')} />
            </Campo>
          </div>
          {erroApi ? <Aviso tom="perigo">{erroApi}</Aviso> : null}
          <div className="flex items-center gap-3">
            <Botao type="submit" variante="primario" carregando={trocar.isPending}>
              Alterar senha
            </Botao>
          </div>
        </form>
      </Secao>
    </Pagina>
  );
}
