# Security headers for the static web app

The API sets its own headers on every response (`SecurityHeadersMiddleware`, pinned by `SecurityHeadersTests`): nosniff, no-referrer,
`Cross-Origin-Resource-Policy: same-site`, a Permissions-Policy that disables every powerful feature, `X-Frame-Options: DENY`, a CSP of
`default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`, `no-store` on `/api/auth/*`, and HSTS (one year, subdomains) over HTTPS.

The Angular PWA is **static files served by the reverse proxy**, so the API cannot set its headers. This page is the copy-paste set for the
proxy (Caddy and nginx). Nothing here changes `web/`; section 4 lists the two build facts the CSP depends on.

Recommended deployment: one host, the proxy serves the PWA and forwards `/api/*` to the API (the production build uses `apiBaseUrl: ''`).
That keeps `connect-src 'self'`, makes the refresh cookie (SameSite=Strict) trivially same-site, and needs no CORS. `Cors__Origins` on the API
still has to contain the web origin, because the cookie CSRF check uses that list (docs/security/refresh-cookie.md).

## 1. The header set

| Header | Value | Why |
|---|---|---|
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` (add `; preload` only after submitting the domain) | Browsers never fall back to http; the refresh cookie is `Secure` anyway. |
| `Content-Security-Policy` | see section 2 | Stops injected script from running or sending data away: the main defence for SEC-34's residual risk (the access token lives in memory). |
| `X-Content-Type-Options` | `nosniff` | No MIME sniffing of scripts and styles. |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Document ids and search text stay out of referrers sent elsewhere. |
| `Permissions-Policy` | `camera=(self), microphone=(), geolocation=(), payment=(), usb=(), accelerometer=(), gyroscope=(), magnetometer=(), interest-cohort=()` | Camera only for our own origin (barcode scanning is planned for Phase 2); drop `camera=(self)` to `camera=()` until then. |
| `X-Frame-Options` | `DENY` | Legacy twin of `frame-ancestors 'none'` (older browsers). |
| `Cross-Origin-Opener-Policy` | `same-origin` | Isolates the window from pages that open it. |
| `Cross-Origin-Resource-Policy` | `same-origin` | Other sites cannot embed our scripts, images or JSON. |
| `Cache-Control` | `no-cache` on the entry files, `public, max-age=31536000, immutable` on hashed files | A new release must reach users: the service worker checks `ngsw.json`; stale entry files would pin old code. |

Remove the `Server` header if the proxy sends a version.

## 2. Content-Security-Policy for an Angular PWA with a service worker

```
default-src 'self';
script-src 'self';
style-src 'self' 'unsafe-inline';
img-src 'self' data: blob:;
font-src 'self';
connect-src 'self';
worker-src 'self';
manifest-src 'self';
object-src 'none';
base-uri 'self';
form-action 'self';
frame-ancestors 'none';
upgrade-insecure-requests
```

Why each line:

- `script-src 'self'`: the production build emits only external module scripts (`main-*.js`, lazy chunks); there is no inline script and no `eval`. This is the line that matters. Do not add `'unsafe-inline'` or `'unsafe-eval'`.
- `style-src 'self' 'unsafe-inline'`: Angular injects component styles as `<style>` elements at runtime, and the CLI can inline critical CSS. A nonce would need a per-response value, which a static host cannot make. Style injection is a much smaller risk than script injection.
- `worker-src 'self'` and `manifest-src 'self'`: `ngsw-worker.js` and `manifest.webmanifest` come from the same origin. `connect-src 'self'` covers the service worker's own fetches and the API when it is behind the same host. If the API is on another origin (development, or a separate API host), use `connect-src 'self' https://api.example.com`.
- `img-src ... data: blob:`: inline SVG/data icons and image previews of attachments fetched with the bearer token and shown from a `blob:` URL.
- `form-action 'self'`, `base-uri 'self'`, `object-src 'none'`, `frame-ancestors 'none'`: no plugin content, no `<base>` hijack, no framing.
- Deploy it first as `Content-Security-Policy-Report-Only` for a week (add `report-uri` or `report-to` if you collect reports), open every screen with the service worker active, then switch to enforcing.
- Optional hardening once the above is enforced and quiet: `require-trusted-types-for 'script'; trusted-types angular angular#bundler` (Angular supports Trusted Types; test every screen and the service worker before enforcing).

## 3. Caddy (`Caddyfile`)

```caddyfile
ims.example.com {
	encode zstd gzip

	# --- security headers on every response (static files and API) ---
	header {
		Strict-Transport-Security "max-age=31536000; includeSubDomains"
		X-Content-Type-Options "nosniff"
		Referrer-Policy "strict-origin-when-cross-origin"
		Permissions-Policy "camera=(self), microphone=(), geolocation=(), payment=(), usb=(), accelerometer=(), gyroscope=(), magnetometer=(), interest-cohort=()"
		X-Frame-Options "DENY"
		Cross-Origin-Opener-Policy "same-origin"
		Cross-Origin-Resource-Policy "same-origin"
		-Server
	}

	# --- the API: forwarded as is. It sets its own (stricter) headers and CSP, so none is added here. ---
	@api path /api/* /health
	handle @api {
		reverse_proxy api:8080 {
			header_up X-Forwarded-Proto {scheme}
		}
	}

	# --- the PWA ---
	handle {
		root * /srv/ims-web
		header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; upgrade-insecure-requests"

		# Entry files and the service worker are never cached, hashed assets for a year.
		@entry path / /index.html /ngsw.json /ngsw-worker.js /safety-worker.js /manifest.webmanifest /config.json
		header @entry Cache-Control "no-cache"
		@hashed path_regexp hashed -[A-Za-z0-9_]{8}\.(js|css|woff2?|svg|png|webp)$
		header @hashed Cache-Control "public, max-age=31536000, immutable"

		try_files {path} /index.html
		file_server
	}
}
```

Notes: Caddy obtains and renews the certificate and redirects http to https on its own. `header` directives outside `handle` apply to every response, including the proxied API;
the API's own `Content-Security-Policy` stays because the PWA CSP is added only inside the PWA `handle`. If the API sits behind this proxy, set
`ForwardedHeaders__Enabled=true` and `ForwardedHeaders__KnownProxies=<proxy address>` on the API so the real client address reaches the rate limits and logs. Validate with `caddy validate --config Caddyfile`.

## 4. nginx

`add_header` is not inherited into a block that has its own `add_header`, so the shared set lives in one include that every block repeats.

`/etc/nginx/snippets/ims-security-headers.conf`:

```nginx
add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
add_header X-Content-Type-Options "nosniff" always;
add_header Referrer-Policy "strict-origin-when-cross-origin" always;
add_header Permissions-Policy "camera=(self), microphone=(), geolocation=(), payment=(), usb=(), accelerometer=(), gyroscope=(), magnetometer=(), interest-cohort=()" always;
add_header X-Frame-Options "DENY" always;
add_header Cross-Origin-Opener-Policy "same-origin" always;
add_header Cross-Origin-Resource-Policy "same-origin" always;
```

`/etc/nginx/conf.d/ims.conf`:

```nginx
server {
    listen 80;
    server_name ims.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name ims.example.com;
    ssl_certificate     /etc/letsencrypt/live/ims.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/ims.example.com/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    server_tokens off;

    root /srv/ims-web;
    index index.html;
    gzip on;
    gzip_types text/css application/javascript application/json image/svg+xml;

    # --- the API: the API sets its own headers; only HSTS-style transport headers are repeated by the API itself ---
    location ~ ^/(api/|health$) {
        proxy_pass http://api:8080;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 12m;          # uploads and imports (the API limit is 12 MB)
    }

    # --- entry files and the service worker: never cached ---
    location ~ ^/(index\.html|ngsw\.json|ngsw-worker\.js|safety-worker\.js|manifest\.webmanifest|config\.json)$ {
        include snippets/ims-security-headers.conf;
        add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; upgrade-insecure-requests" always;
        add_header Cache-Control "no-cache" always;
    }

    # --- hashed build output: cache for a year ---
    location ~* -[A-Za-z0-9_]{8}\.(js|css|woff2?|svg|png|webp)$ {
        include snippets/ims-security-headers.conf;
        add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; upgrade-insecure-requests" always;
        add_header Cache-Control "public, max-age=31536000, immutable" always;
    }

    # --- everything else is the single-page app (deep links fall back to index.html) ---
    location / {
        include snippets/ims-security-headers.conf;
        add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; upgrade-insecure-requests" always;
        add_header Cache-Control "no-cache" always;
        try_files $uri /index.html;
    }
}
```

Validate with `nginx -t`. Check the result with `curl -sI https://ims.example.com/` and a browser (DevTools console shows CSP violations; Report-Only mode first).

## 5. Facts about the web build that this CSP relies on (for fe-framework, no change made here)

1. **No inline script.** True for the current `index.html`. Do not add inline `<script>` or inline event handlers; a script must be a file.
2. **Critical CSS inlining.** The production build has Angular's default `optimization`, which inlines critical CSS and loads the full stylesheet with an `onload` attribute. An inline event handler is blocked by `script-src 'self'`, so the stylesheet would not apply. Set `"optimization": { "styles": { "inlineCritical": false } }` in the production configuration of `web/apps/main/project.json` (or accept `'unsafe-hashes'` plus the handler's hash, which is brittle). Verify by running the production build behind the headers above.
3. **Service worker.** `ngsw-worker.js` is a file, so `worker-src 'self'` suffices. The `dataGroups` in `ngsw-config.json` only match same-origin and API URLs; if a CDN, font host or analytics host is ever added, it must be added to the matching CSP directive deliberately.

## 6. Check list after deploying

- [ ] `curl -sI https://<host>/` shows all headers of section 1 and the CSP; `curl -sI https://<host>/api/auth/me` shows the API's own CSP (`default-src 'none'`).
- [ ] Browser console: no CSP violations on login, dashboard, a document editor, product import, the PWA install prompt and an update (new release).
- [ ] `https://securityheaders.com` (or `curl`) grade A or better; HSTS present only on https.
- [ ] Login, silent refresh after 15 minutes and logout work (the cookie: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`).
