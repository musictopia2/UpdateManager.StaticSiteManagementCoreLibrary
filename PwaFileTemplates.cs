namespace UpdateManager.StaticSiteManagementCoreLibrary;
internal static class PwaFileTemplates
{
    public static string CreateUpdateComponent(string buttonMarkup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonMarkup);

        return UpdateComponent.Replace(
            "__UPDATE_BUTTON_MARKUP__",
            buttonMarkup,
            StringComparison.Ordinal);
    }
    public const string UpdateComponent = """
    @using Microsoft.AspNetCore.Components
    @using Microsoft.JSInterop
    @implements IAsyncDisposable
    @inject IJSRuntime JS
    @inject NavigationManager Navigation

    @if (UpdateChecksEnabled && _updateAvailable)
    {
        <div role="status" aria-live="polite">
            __UPDATE_BUTTON_MARKUP__
        </div>
    }

    @code {
        private bool _updateAvailable;
        private bool _disposed;
        private IJSObjectReference? _module;
        private DotNetObjectReference<StaticSiteUpdate>? _reference;

        private static bool UpdateChecksEnabled
        {
            get
            {
    #if DEBUG
                return false;
    #else
                return true;
    #endif
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!UpdateChecksEnabled || !firstRender || _disposed)
            {
                return;
            }

            try
            {
                var module = await JS.InvokeAsync<IJSObjectReference>(
                    "import", "./serviceWorkerInterop.js");

                if (_disposed)
                {
                    await module.DisposeAsync();
                    return;
                }

                _module = module;
                _reference = DotNetObjectReference.Create(this);

                await _module.InvokeVoidAsync(
                    "registerAndListen", _reference);
            }
            catch (JSException)
            {
                // An unavailable update check must not prevent offline use.
            }
        }

        [JSInvokable]
        public async Task NotifyUpdateAvailable()
        {
            if (!UpdateChecksEnabled || _disposed)
            {
                return;
            }

            await InvokeAsync(() =>
            {
                if (!_disposed)
                {
                    _updateAvailable = true;
                    StateHasChanged();
                }
            });
        }

        private void Refresh()
        {
            Navigation.NavigateTo(Navigation.Uri, forceLoad: true);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (_module is not null)
                {
                    try
                    {
                        await _module.InvokeVoidAsync("dispose");
                    }
                    finally
                    {
                        await _module.DisposeAsync();
                    }
                }
            }
            catch (JSException)
            {
                // JavaScript may be unavailable during page shutdown.
            }
            finally
            {
                _reference?.Dispose();
            }
        }
    }
    """;

    public const string ServiceWorkerInterop = """
        let cleanup = null;

        export async function registerAndListen(dotNetHelper) {
            dispose();

            if (!('serviceWorker' in navigator)) {
                return;
            }

            const workerUrl = new URL('service-worker.js', document.baseURI);

            const registration = await navigator.serviceWorker.register(
                workerUrl.href,
                { updateViaCache: 'none' }
            );

            let disposed = false;
            let notified = false;
            const workerListeners = [];

            function notifyUpdate() {
                if (disposed || notified) {
                    return;
                }

                notified = true;

                dotNetHelper.invokeMethodAsync('NotifyUpdateAvailable')
                    .catch(error => {
                        if (!disposed) {
                            console.debug('Update notification failed.', error);
                        }
                    });
            }

            function watchWorker(worker) {
                if (!worker) {
                    return;
                }

                function stateChanged() {
                    if (worker.state === 'installed' &&
                        navigator.serviceWorker.controller) {
                        notifyUpdate();
                    }
                }

                worker.addEventListener('statechange', stateChanged);
                workerListeners.push({ worker, stateChanged });

                stateChanged();
            }

            function updateFound() {
                watchWorker(registration.installing);
            }

            async function checkForUpdate() {
                try {
                    await registration.update();
                } catch (error) {
                    console.debug(
                        'Update check unavailable; keeping the current version.',
                        error
                    );
                }
            }

            registration.addEventListener('updatefound', updateFound);

            watchWorker(registration.installing);

            if (registration.waiting && navigator.serviceWorker.controller) {
                notifyUpdate();
            }

            const timer = setInterval(checkForUpdate, 60_000);
            window.addEventListener('online', checkForUpdate);

            cleanup = () => {
                disposed = true;

                clearInterval(timer);
                window.removeEventListener('online', checkForUpdate);
                registration.removeEventListener('updatefound', updateFound);

                for (const entry of workerListeners) {
                    entry.worker.removeEventListener(
                        'statechange',
                        entry.stateChanged
                    );
                }
            };

            await checkForUpdate();
        }

        export function dispose() {
            if (cleanup) {
                cleanup();
                cleanup = null;
            }
        }
        """;

    public const string PublishedServiceWorker = """
        self.importScripts('./service-worker-assets.js');

        const version = '0.0';

        const scopeUrl = new URL(self.registration.scope);
        const indexUrl = new URL('index.html', scopeUrl).href;

        const cacheNamePrefix = `static-site:${scopeUrl.href}|`;
        const cacheName =
            `${cacheNamePrefix}${self.assetsManifest.version}`;

        const offlineAssetsInclude = [
            /\.dll$/,
            /\.pdb$/,
            /\.wasm$/,
            /\.html$/,
            /\.js$/,
            /\.json$/,
            /\.css$/,
            /\.woff2?$/,
            /\.ttf$/,
            /\.png$/,
            /\.jpe?g$/,
            /\.gif$/,
            /\.ico$/,
            /\.svg$/,
            /\.blat$/,
            /\.dat$/,
            /\.webmanifest$/
        ];

        self.addEventListener('install', event => {
            event.waitUntil((async () => {
                const assets = self.assetsManifest.assets
                    .filter(asset =>
                        offlineAssetsInclude.some(pattern =>
                            pattern.test(asset.url)))
                    .map(asset => ({
                        asset,
                        url: new URL(asset.url, scopeUrl)
                    }))
                    .filter(entry =>
                        entry.url.origin === scopeUrl.origin &&
                        entry.url.href.startsWith(scopeUrl.href) &&
                        entry.url.href !==
                            new URL('service-worker.js', scopeUrl).href);

                const requests = assets.map(entry =>
                    new Request(entry.url.href, {
                        integrity: entry.asset.hash,
                        cache: 'no-cache'
                    }));

                const cache = await caches.open(cacheName);

                // Activate only after the complete offline snapshot is cached.
                await cache.addAll(requests);
                await self.skipWaiting();
            })());
        });

        self.addEventListener('activate', event => {
            event.waitUntil((async () => {
                const keys = await caches.keys();

                await Promise.all(
                    keys
                        .filter(key =>
                            key.startsWith(cacheNamePrefix) &&
                            key !== cacheName)
                        .map(key => caches.delete(key))
                );

                await self.clients.claim();
            })());
        });

        self.addEventListener('fetch', event => {
            if (event.request.method !== 'GET') {
                return;
            }

            const requestUrl = new URL(event.request.url);

            if (requestUrl.origin !== scopeUrl.origin ||
                !requestUrl.href.startsWith(scopeUrl.href)) {
                return;
            }

            event.respondWith((async () => {
                const cache = await caches.open(cacheName);

                if (event.request.mode === 'navigate') {
                    const page = await cache.match(indexUrl);

                    if (page) {
                        return page;
                    }

                    return fetch(event.request);
                }

                const cached = await cache.match(event.request);

                return cached || fetch(event.request);
            })());
        });
        """;
}