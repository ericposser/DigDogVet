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