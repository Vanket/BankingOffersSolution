

(() => {
    // 1. Скрываем метку 'webdriver'
    if (navigator.webdriver) {
        Object.defineProperty(navigator, 'webdriver', {
            get: () => false,
        });
    }

    // 2. Подделываем наличие плагинов
    if (navigator.plugins) {
        Object.defineProperty(navigator, 'plugins', {
            get: () => [
                { name: "PDF Viewer", filename: "internal-pdf-viewer", description: "Portable Document Format" },
                { name: "Chrome PDF Viewer", filename: "internal-pdf-viewer", description: "Portable Document Format" },
                { name: "Chromium PDF Viewer", filename: "internal-pdf-viewer", description: "Portable Document Format" },
                { name: "Microsoft Edge PDF Viewer", filename: "internal-pdf-viewer", description: "Portable Document Format" },
                { name: "WebKit built-in PDF", filename: "internal-pdf-viewer", description: "Portable Document Format" }
            ],
        });
    }

    // 3. Подделываем разрешения (Permissions API)
    if (window.navigator.permissions) {
        const originalQuery = window.navigator.permissions.query;
        window.navigator.permissions.query = (parameters) => (
            parameters.name === 'notifications' ?
                Promise.resolve({ state: Notification.permission }) :
                originalQuery(parameters)
        );
    }
})();