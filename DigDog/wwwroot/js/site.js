// ── Overlay de Carregamento Global ──────────────────────────────────
(function () {
    const overlay = document.getElementById('overlayCarregando');
    if (!overlay) return;

    // Exibe overlay ao clicar em links de navegação (exceto âncoras, externos, logout e botões)
    document.addEventListener('click', function (e) {
        const alvo = e.target.closest('a');
        if (!alvo) return;

        const href = alvo.getAttribute('href');
        if (!href) return;

        // Ignora: âncoras (#), javascript:, links externos, target _blank
        if (
            href.startsWith('#') ||
            href.startsWith('javascript') ||
            alvo.hostname !== window.location.hostname ||
            alvo.target === '_blank'
        ) return;

        exibirOverlay();
    });

    // Exibe overlay ao submeter formulários (exceto o de logout que já redireciona)
    document.addEventListener('submit', function (e) {
        const form = e.target;
        // Apenas formulários que vão navegar (não ajax)
        if (form.dataset.semOverlay) return;
        exibirOverlay();
    });

    // Esconde ao voltar/avançar no histórico do browser
    window.addEventListener('pageshow', function (e) {
        // pageshow com persisted = true significa que veio do cache (botão voltar)
        esconderOverlay();
    });

    function exibirOverlay() {
        overlay.classList.add('ativo');
    }

    function esconderOverlay() {
        overlay.classList.remove('ativo');
    }
})();

// ── Persistência de Scroll da Sidebar ───────────────────────────────
(function () {
    const CHAVE_SCROLL_SIDEBAR = 'digdog-sidebar-scroll';

    function obterElementoScroll() {
        const sidebar = document.getElementById('sidebar');
        if (!sidebar) return null;
        return sidebar.querySelector('.simplebar-content-wrapper');
    }

    // Salva antes de sair da página
    window.addEventListener('beforeunload', function () {
        const el = obterElementoScroll();
        if (!el) return;
        localStorage.setItem(CHAVE_SCROLL_SIDEBAR, el.scrollTop);
    });

    document.addEventListener('DOMContentLoaded', function () {
        const el = obterElementoScroll();
        if (!el) return;

        // Salva ao rolar
        el.addEventListener('scroll', function () {
            localStorage.setItem(CHAVE_SCROLL_SIDEBAR, el.scrollTop);
        }, { passive: true });

        // Restaura a posição — aguarda Simplebar terminar de inicializar
        const posicaoSalva = localStorage.getItem(CHAVE_SCROLL_SIDEBAR);
        if (!posicaoSalva) return;

        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                el.scrollTop = parseInt(posicaoSalva, 10);
            });
        });
    });
})();