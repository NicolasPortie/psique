import { PanelLeftClose, PanelLeftOpen } from 'lucide-react';
import { Link } from 'react-router';

import { usePermissoes } from '../../core/autenticacao/permissoes';
import { MARCA } from '../../core/marca';
import { rotuloPerfil } from '../../core/dominio/perfil';
import { Avatar } from '../ui/avatar';
import { BotaoBuscaGlobal } from './busca-global';
import { cn } from '../ui/cn';
import { Dica } from '../ui/dica';
import { MenuAcoes } from '../ui/menu';
import { useAcoesUsuario } from './acoes-usuario';
import { LinkNavegacao } from './link-navegacao';
import { Marca } from './marca';
import { gruposPermitidos } from './navegacao';
import { SinoNotificacoes } from './sino-notificacoes';

interface TrilhoProps {
  recolhido: boolean;
  aoAlternar: () => void;
}

/** Trilho lateral do desktop: marca, áreas agrupadas e conta. Um segundo neutro, levemente mais denso que o papel. */
export function Trilho({ recolhido, aoAlternar }: TrilhoProps) {
  const permissoes = usePermissoes();
  const grupos = gruposPermitidos(permissoes);
  const acoes = useAcoesUsuario();
  const usuario = permissoes.usuario;

  return (
    <aside
      className="nao-imprimir sticky top-0 hidden h-dvh min-h-0 flex-col border-r border-line bg-rail lg:flex"
      aria-label="Navegação principal"
    >
      <div
        className={cn(
          'flex h-14 shrink-0 items-center',
          recolhido ? 'justify-center' : 'justify-between pl-4 pr-2.5',
        )}
      >
        <Link
          to="/painel"
          aria-label={`${MARCA.nome}, ir para Hoje`}
          className="rounded-md no-underline"
        >
          <Marca somenteSimbolo={recolhido} />
        </Link>
        {recolhido ? null : (
          <SinoNotificacoes />
        )}
      </div>

      <nav
        aria-label="Áreas do consultório"
        className="scroll-contido min-h-0 flex-1 overflow-y-auto px-2 pb-4"
      >
        {recolhido ? (
          <div className="mb-3 grid justify-center gap-1">
            <BotaoBuscaGlobal compacto />
            <SinoNotificacoes />
          </div>
        ) : (
          <div className="mb-2 px-0.5 pt-1">
            <BotaoBuscaGlobal />
          </div>
        )}
        {grupos.map((grupo) => (
          <div key={grupo.id} className="mt-4 first:mt-1">
            {recolhido ? (
              <div className="mx-3 mb-2 h-px bg-line" role="presentation" />
            ) : (
              <p className="px-2.5 pb-1 text-xs font-medium text-ink-faint">{grupo.rotulo}</p>
            )}
            <ul className="grid gap-0.5">
              {grupo.itens.map((item) => (
                <li key={item.rota}>
                  <LinkNavegacao item={item} recolhido={recolhido} />
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <div className="shrink-0 border-t border-line p-2">
        <Dica texto={recolhido ? 'Expandir menu' : 'Recolher menu'} lado="right">
          <button
            type="button"
            onClick={aoAlternar}
            aria-label={recolhido ? 'Expandir menu lateral' : 'Recolher menu lateral'}
            className={cn(
              'mb-1 flex h-8 w-full items-center gap-2.5 rounded-md text-sm font-medium text-ink-muted transition-[background-color,color] duration-150 ease-out hover:bg-hover hover:text-ink',
              recolhido ? 'justify-center' : 'px-2.5',
            )}
          >
            {recolhido ? (
              <PanelLeftOpen size={16} aria-hidden="true" />
            ) : (
              <PanelLeftClose size={16} aria-hidden="true" />
            )}
            {recolhido ? null : 'Recolher'}
          </button>
        </Dica>
        <MenuAcoes
          itens={acoes}
          alinhamento="start"
          lado="top"
          rotulo="Menu da conta"
          gatilho={
            <button
              type="button"
              aria-label={`Menu da conta de ${usuario?.nomeCompleto || 'usuário'}`}
              className={cn(
                'flex w-full items-center gap-2.5 rounded-md p-1.5 text-left transition-colors duration-150 ease-out hover:bg-hover',
                recolhido && 'justify-center',
              )}
            >
              <Avatar nome={usuario?.nomeCompleto || MARCA.nome} />
              {recolhido ? null : (
                <span className="grid min-w-0 flex-1 leading-tight">
                  <span className="truncate text-sm font-medium text-ink">
                    {usuario?.nomeCompleto || 'Minha conta'}
                  </span>
                  <span className="truncate text-xs text-ink-muted">
                    {rotuloPerfil(usuario?.perfil, usuario?.nomeDoPerfil)}
                  </span>
                </span>
              )}
            </button>
          }
        />
      </div>
    </aside>
  );
}
