// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Searchable project switcher (_ProjectSwitcher.cshtml): type to filter, Enter opens the
// first match, and the current project scrolls into view when the menu opens.
(() => {
    const menu = document.getElementById('project-switcher');
    if (!menu) {
        return;
    }

    const search = document.getElementById('project-switcher-search');
    const items = document.querySelectorAll('#project-switcher-list .dropdown-item');
    const empty = document.getElementById('project-switcher-empty');

    search.addEventListener('input', () => {
        const term = search.value.trim().toLowerCase();
        let visible = 0;
        for (const item of items) {
            const match = item.textContent.toLowerCase().includes(term);
            item.classList.toggle('d-none', !match);
            if (match) visible++;
        }
        empty.classList.toggle('d-none', visible > 0);
    });

    search.addEventListener('keydown', e => {
        if (e.key === 'Enter') {
            e.preventDefault();
            menu.querySelector('#project-switcher-list .dropdown-item:not(.d-none)')?.click();
        }
    });

    const toggle = menu.parentElement.querySelector('[data-bs-toggle="dropdown"]');
    toggle.addEventListener('shown.bs.dropdown', () => {
        search.focus();
        menu.querySelector('.dropdown-item.active')?.scrollIntoView({ block: 'center' });
    });
})();
