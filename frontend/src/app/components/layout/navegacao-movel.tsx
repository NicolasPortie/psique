import { Menu as IconeMenu } from 'lucide-react';
import { useState } from 'react';
import { Link, NavLink, useLocation } from 'react-router';

import { usePermissoes } from '../../core/autenticacao/permissoes';
import { rotuloPerfil } from '../../core/dominio/perfil';
import { MARCA } from '../../core/marca';
import { Avatar } from '../ui/avatar';
import { cn } from '../ui/cn';
import { Gaveta } from '../ui/dialogo';
import { useAcoesUsuario } from './acoes-usuario';
import { LinkNavegacao } from './link-navegacao';
import { BotaoBuscaGlobal } from './busca-global';
import { Marca } from './marca';
import { gruposPermitidos, gruposNavegacao, itemPermitido, rotasBarraInferior } from './navegacao';
import { SinoNotificacoes } from './sino-notificacoes';

const itensBarra = rotasBarraInferior.map((rota) => {
  const item = gruposNavegacao
    .flatMap((grupo) => grupo.itens)
    .find((candidato) => candidato.rota === rota);
  if (!item) throw new Error(`Rota ${rota} ausente da navegação.`);
  return item;
});

/** Navegação do celular e do tablet: barra superior enxuta, abas inferiores e folha "Mais" com tudo. */
export function NavegacaoMovel() {
  const { pathname } = useLocation();
  // A folha guarda em qual rota foi aberta: ao navegar, deixa de valer sem precisar de efeito.
  const [abertaEm, setAbertaEm] = useState<string | null>(null);
  const aberta = abertaEm === pathname;
  const setAberta = (valor: boolean) => setAbertaEm(valor ? pathname : null);
  const permissoes = usePermissoes();
  const acoes = useAcoesUsuario();
  const grupos = gruposPermitidos(permissoes);
  const atalhos = itensBarra.filter((item) => itemPermitido(item, permissoes));
  const usuario = permissoes.usuario;

  return (
    <>
      <header className="nao-imprimir sticky top-0 z-40 flex h-13 items-center justify-between border-b border-line bg-paper px-4 lg:hidden">
        <Link
          to="/painel"
          aria-label={`${MARCA.nome}, ir para Hoje`}
          className="rounded-md no-underline"
        >
          <Marca tamanho={24} />
        </Link>
        <span className="flex items-center gap-1">
          <BotaoBuscaGlobal compacto />
          <SinoNotificacoes />
        </span>
      </header>

      <nav
        aria-label="Atalhos"
        style={{ gridTemplateColumns: `repeat(${atalhos.length + 1}, minmax(0, 1fr))` }}
        className="nao-imprimir fixed inset-x-0 bottom-0 z-40 grid border-t border-line bg-paper pb-[env(safe-area-inset-bottom)] lg:hidden"
      >
        {atalhos.map(({ rota, rotulo, icone: Icone }) => (
          <NavLink
            key={rota}
            to={rota}
            className={({ isActive }) =>
              cn(
                'grid h-14 place-items-center content-center gap-0.5 text-[0.6875rem] font-medium no-underline',
                'transition-colors duration-150 ease-out active:bg-hover',
                isActive ? 'text-accent-ink' : 'text-ink-muted',
              )
            }
          >
            <span className="pilula-navegacao">
              <Icone size={20} strokeWidth={1.75} aria-hidden="true" />
            </span>
            {rotulo}
          </NavLink>
        ))}
        <button
          type="button"
          onClick={() => setAberta(true)}
          aria-haspopup="dialog"
          className="grid h-14 place-items-center content-center gap-0.5 text-[0.6875rem] font-medium text-ink-muted transition-colors duration-150 ease-out active:bg-hover"
        >
          <span className="pilula-navegacao">
            <IconeMenu size={20} strokeWidth={1.75} aria-hidden="true" />
          </span>
          Mais
        </button>
      </nav>

      <Gaveta aberto={aberta} aoMudar={setAberta} titulo="Menu" lado="esquerda" largura="20rem">
        <div className="-mt-1 mb-4 flex items-center gap-3 border-b border-line pb-4">
          <Avatar nome={usuario?.nomeCompleto || MARCA.nome} tamanho="grande" />
          <div className="grid min-w-0 leading-tight">
            <span className="truncate text-base font-medium text-ink">{usuario?.nomeCompleto || 'Minha conta'}</span>
            <span className="truncate text-sm text-ink-muted">
              {rotuloPerfil(usuario?.perfil, usuario?.nomeDoPerfil)}
            </span>
          </div>
        </div>
        <nav aria-label="Todas as áreas">
          {grupos.map((grupo) => (
            <div key={grupo.id} className="mt-4 first:mt-0">
              <p className="px-2.5 pb-1 text-xs font-medium text-ink-faint">{grupo.rotulo}</p>
              <ul className="grid gap-0.5">
                {grupo.itens.map((item) => (
                  <li key={item.rota}>
                    <LinkNavegacao item={item} aoNavegar={() => setAberta(false)} />
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>
        <ul className="mt-6 grid gap-0.5 border-t border-line pt-4">
          {acoes.map((acao) => (
            <li key={acao.rotulo}>
              {acao.para ? (
                <Link
                  to={acao.para}
                  className="flex min-h-11 items-center gap-2.5 rounded-md px-2.5 text-base font-medium text-ink no-underline hover:bg-hover"
                >
                  {acao.icone}
                  {acao.rotulo}
                </Link>
              ) : (
                <button
                  type="button"
                  onClick={acao.aoSelecionar}
                  className={cn(
                    'flex min-h-11 w-full items-center gap-2.5 rounded-md px-2.5 text-left text-base font-medium hover:bg-hover',
                    acao.perigo ? 'text-danger-ink hover:bg-danger-wash' : 'text-ink',
                    acao.selecionado && 'bg-accent-wash text-accent-ink hover:bg-accent-wash',
                  )}
                  aria-pressed={acao.selecionado}
                >
                  {acao.icone}
                  {acao.rotulo}
                </button>
              )}
            </li>
          ))}
        </ul>
      </Gaveta>
    </>
  );
}
