// Root URL of this app: "/" when run on its own, "/app/" behind a reverse proxy.
// The layout renders it into <html data-app-base>. Every client-side URL goes through
// appUrl() so nothing hardcodes the root — a bare "/Inventory/..." would escape the
// path prefix and hit the other app instead.
window.APP_BASE = document.documentElement.dataset.appBase || "/";
window.appUrl = function (path) {
    return window.APP_BASE + String(path).replace(/^\//, "");
};
