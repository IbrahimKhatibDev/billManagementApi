// Applied before first paint, from a blocking <script> in <head>.
//
// This cannot be done from Blazor. The app renders ServerPrerendered, so
// IJSRuntime is unusable until OnAfterRenderAsync — by which time the page has
// already painted, and every load would flash the default theme before
// switching to the chosen one.
//
// The same file also owns the localStorage key, so ThemeSwitcher.razor never
// has to know what it is called.
(function () {
    'use strict';

    var MODE_KEY = 'bills.mode';
    var MODES = ['light', 'dark'];

    // Left over from when the theme was a palette × mode matrix. Nothing reads
    // it now; clearing it on the way past keeps a dead key from outliving the
    // feature in every visitor's browser.
    var RETIRED_KEY = 'bills.palette';

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

    function apply(mode) {
        var root = document.documentElement;
        root.setAttribute('data-mode', mode);

        // Bootstrap 5.3 reads this one, and mirroring it is what makes modals,
        // form controls, cards and tables follow the mode without being
        // restyled — including details the tokens can't reach, like the
        // form-select chevron, which 5.3 swaps for a light one under dark.
        root.setAttribute('data-bs-theme', mode);
    }

    function read() {
        return readStored(MODE_KEY, MODES, 'dark');
    }

    function save(mode) {
        try {
            window.localStorage.setItem(MODE_KEY, mode);
            window.localStorage.removeItem(RETIRED_KEY);
        } catch (e) {
            // See readStored. The choice still applies for this page.
        }

        apply(mode);
    }

    apply(read());

    window.billsTheme = { apply: apply, read: read, save: save };
})();
