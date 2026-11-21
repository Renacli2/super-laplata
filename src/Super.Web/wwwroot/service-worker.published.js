// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// La base es la carpeta del propio service worker: sirve igual en "/" (local) y en "/<repo>/" (GitHub Pages)
// sin tocar el archivo después de compilar (su hash está en service-worker-assets.js).
const baseUrl = new URL('./', self.location);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName && key !== dataCacheName)
        .map(key => caches.delete(key)));
}

// Datos diarios (data/*.json): primero la red; si no hay señal, la última copia guardada.
// Se guardan sin el "?v=" para que la copia sirva aunque cambie la fecha.
const dataCacheName = 'super-data';

// Con señal débil (adentro del súper) la red puede tardar mucho: si hay copia guardada, se espera
// a lo sumo esto y se usa la copia. La descarga sigue de fondo y actualiza la copia para la próxima vez.
const networkWaitMs = 4000;

// La app lee esta cabecera para avisar "Sin señal: usando los precios del …".
function markSaved(response) {
    const headers = new Headers(response.headers);
    headers.set('x-super-saved', '1');
    return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

async function fetchData(event) {
    const request = event.request;
    const url = new URL(request.url);
    const key = url.origin + url.pathname;
    const cache = await caches.open(dataCacheName);
    const cached = await cache.match(key);

    const network = fetch(request, { cache: 'no-cache' }).then(async response => {
        if (response.ok) await cache.put(key, response.clone());
        return response;
    });

    if (!cached) return network;

    // Que el service worker siga vivo hasta terminar de guardar la copia nueva.
    event.waitUntil(network.catch(() => null));
    const timeout = new Promise(resolve => setTimeout(() => resolve(null), networkWaitMs));
    try {
        const response = await Promise.race([network, timeout]);
        if (response && response.ok) return response;
    } catch {
        // sin red: se usa la copia
    }
    return markSaved(cached);
}

async function onFetch(event) {
    if (event.request.method === 'GET' && new URL(event.request.url).pathname.startsWith(new URL('data/', baseUrl).pathname)) {
        return fetchData(event);
    }

    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}
