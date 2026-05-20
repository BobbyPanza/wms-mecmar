const CACHE = 'wms-mecmar-v1';
const OFFLINE_URL = '/offline.html';

// Pre-cache solo la pagina offline all'installazione
self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE).then(cache => cache.add(OFFLINE_URL))
    );
    self.skipWaiting();
});

// Rimuovi cache vecchie all'attivazione
self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// Network-first: prova rete, fallback su cache, poi pagina offline
self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') return;
    if (!event.request.url.startsWith(self.location.origin)) return;

    // Non intercettare WebSocket / SignalR (_blazor hub)
    if (event.request.url.includes('_blazor')) return;

    event.respondWith(
        fetch(event.request)
            .then(response => {
                // Metti in cache asset statici (CSS, JS, immagini, font)
                const url = event.request.url;
                if (response.ok && /\.(css|js|png|jpg|svg|woff2?)(\?|$)/.test(url)) {
                    const clone = response.clone();
                    caches.open(CACHE).then(cache => cache.put(event.request, clone));
                }
                return response;
            })
            .catch(() =>
                caches.match(event.request)
                    .then(cached => cached || caches.match(OFFLINE_URL))
            )
    );
});
