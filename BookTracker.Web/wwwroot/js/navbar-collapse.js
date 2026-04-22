// Closes the mobile Bootstrap navbar collapse when called. Invoked from
// MainLayout's NavigationManager.LocationChanged subscription so the
// hamburger menu closes itself after a nav link tap — otherwise the
// full-viewport open menu stays up and the user has to scroll back to
// see the page they landed on.
window.NavbarCollapse = {
    close: function () {
        document.querySelectorAll('.navbar-collapse.show').forEach(el => {
            const instance = window.bootstrap?.Collapse?.getInstance(el)
                ?? new window.bootstrap.Collapse(el, { toggle: false });
            instance.hide();
        });
    }
};
