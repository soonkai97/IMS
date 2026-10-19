// Tiny static server for the production build (SPA fallback, correct MIME types). Used for PWA / service-worker checks:
//   npm run build:prod && npm run serve:prod   ->  http://localhost:4200
// API_URL=http://localhost:5109 makes /assets/config.json point the app at that API (the production build defaults to the same
// origin). The e2e scenarios that run with the service worker on need it, because the worker bypasses Playwright's routes.
import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(fileURLToPath(new URL('..', import.meta.url)), 'dist', 'main', 'browser');
const port = Number(process.env.PORT ?? 4200);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.webmanifest': 'application/manifest+json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon'
};
const apiUrl = process.env.API_URL;
const NO_CACHE = ['ngsw.json', 'config.json', 'index.html', 'ngsw-worker.js'];

createServer((request, response) => {
  let urlPath;
  try {
    urlPath = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
  } catch {
    // A malformed escape (GET /%) is the client's mistake, not a reason to stop the server.
    response.writeHead(400).end();
    return;
  }
  if (apiUrl && urlPath === '/assets/config.json') {
    response.writeHead(200, { 'Content-Type': types['.json'], 'Cache-Control': 'no-cache' });
    response.end(JSON.stringify({ apiBaseUrl: apiUrl }));
    return;
  }
  let file = join(root, normalize(urlPath));
  if (!file.startsWith(root) || !existsSync(file) || statSync(file).isDirectory()) {
    file = join(root, 'index.html');
  }
  response.writeHead(200, {
    'Content-Type': types[extname(file)] ?? 'application/octet-stream',
    'Cache-Control': NO_CACHE.some(name => file.endsWith(name))
      ? 'no-cache'
      : 'public, max-age=3600'
  });
  createReadStream(file).pipe(response);
}).listen(port, () => console.log(`Serving ${root} on http://localhost:${port}`));
