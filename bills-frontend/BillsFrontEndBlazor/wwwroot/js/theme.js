// Applied before first paint, from a blocking <script> in <head>.
//
// This cannot be done from Blazor. The app renders ServerPrerendered, so
// IJSRuntime is unusable until OnAfterRenderAsync — by which time the page has
// already painted, and every load would flash the default theme before
// switching to the chosen one.
//
// The same file also owns the two localStorage keys, so ThemeSwitcher.razor
// never has to know what they are called.
(function () {
    'use strict';

    var PALETTE_KEY = 'bills.palette';
    var MODE_KEY = 'bills.mode';
    var PALETTES = ['nocturne', 'current'];
    var MODES = ['light', 'dark'];

    function readStored(key, allowed, fallback) {
        try {
            var value = window.localStorage.getItem(key);
            return allowed.indexOf(value) === -1 ? fallback : value;
        } catch (e) {
            // Private browsing and blocked storage both throw on getItem. A
            // theme is a preference, not a feature: falling back is the whole
            // of the handling this deserves.
            return fallback;
        }
    }

    function apply(palette, mode) {
        var root = document.documentElement;
        root.setAttribute('data-palette', palette);
        root.setAttribute('data-mode', mode);

        // Bootstrap 5.3 reads this one, and mirroring it is what makes modals,
        // form controls, cards and tables follow the mode without being
        // restyled — including details the tokens can't reach, like the
        // form-select chevron, which 5.3 swaps for a light one under dark.
        root.setAttribute('data-bs-theme', mode);
    }

    function read() {
        return {
            palette: readStored(PALETTE_KEY, PALETTES, 'nocturne'),
            mode: readStored(MODE_KEY, MODES, 'dark')
        };
    }

    function save(palette, mode) {
        try {
            window.localStorage.setItem(PALETTE_KEY, palette);
            window.localStorage.setItem(MODE_KEY, mode);
        } catch (e) {
            // See readStored. The choice still applies for this page.
        }

        apply(palette, mode);
    }

    var chosen = read();
    apply(chosen.palette, chosen.mode);

    window.billsTheme = { apply: apply, read: read, save: save };
})();
