import { useState } from 'react';
import { Outlet, ScrollRestoration } from 'react-router';

import { useTituloRota } from '../../core/use-titulo-rota';
import { PerfilProfissionalInicial } from '../../features/dados-profissionais/perfil-profissional-inicial';
import { BuscaGlobal } from './busca-global';
import { NavegacaoMovel } from './navegacao-movel';
import { Trilho } from './trilho';

const CHAVE_TRILHO = 'psique:trilho-recolhido';

function lerTrilhoRecolhido() {
  try {
    return window.localStorage.getItem(CHAVE_TRILHO) === '1';
  } catch {
    return false;
  }
}

/** Estrutura autenticada: trilho lateral no desktop, barras e folha no celular, conteúdo no centro. */
export function Shell() {
  const titulo = useTituloRota();
  const [recolhido, setRecolhido] = useState(lerTrilhoRecolhido);

  function alternarTrilho() {
    setRecolhido((atual) => {
      const proximo = !atual;
      try {
        window.localStorage.setItem(CHAVE_TRILHO, proximo ? '1' : '0');
      } catch {
        // A preferência de layout é opcional; sem armazenamento, vale só nesta sessão.
      }
      return proximo;
    });
  }

  return (
    <div
      className="min-h-dvh bg-paper lg:grid lg:grid-cols-[var(--largura-trilho)_minmax(0,1fr)] print:block"
      style={{ '--largura-trilho': recolhido ? '4rem' : '14.5rem' } as React.CSSProperties}
    >
      <a
        href="#conteudo-principal"
        className="sr-only z-[100] rounded-md bg-accent px-4 py-2 text-base font-medium text-on-accent focus:not-sr-only focus:fixed focus:left-4 focus:top-4"
      >
        Pular para o conteúdo
      </a>
      <Trilho recolhido={recolhido} aoAlternar={alternarTrilho} />
      <NavegacaoMovel />
      <main
        id="conteudo-principal"
        tabIndex={-1}
        className="min-w-0 px-4 pb-24 pt-6 outline-none sm:px-6 lg:px-10 lg:pb-14 lg:pt-9"
      >
        <p role="status" aria-live="polite" className="sr-only">
          {titulo}
        </p>
        <PerfilProfissionalInicial />
        <Outlet />
      </main>
      <BuscaGlobal />
      <ScrollRestoration />
    </div>
  );
}
