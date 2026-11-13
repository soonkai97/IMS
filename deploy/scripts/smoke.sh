#!/usr/bin/env bash
# Smoke check of a running stack: health, the auth door, the security headers and no 5xx on a few anonymous reads.
#
#   SMOKE_BASE_URL=https://ims.example.com deploy/scripts/smoke.sh
#   SMOKE_BASE_URL=http://127.0.0.1 deploy/scripts/smoke.sh         (IP-address test over http: the HSTS check is skipped)
#
# Optional: SMOKE_USER and SMOKE_PASSWORD (a real account) add a login, an authenticated read and a refresh-cookie check.
# Exit 0 = all checks passed, 1 = at least one failed. Nothing is written; the password is never printed.
set -euo pipefail

base="${SMOKE_BASE_URL:-}"
[ -n "$base" ] || { echo "set SMOKE_BASE_URL" >&2; exit 2; }
base="${base%/}"
failed=0
pass() { printf 'ok    %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1" >&2; failed=1; }

status_of() { curl -sS -o /dev/null -w '%{http_code}' --max-time 20 "$@" || echo 000; }
headers_of() { curl -sS -o /dev/null -D - --max-time 20 "$@" || true; }
has_header() { printf '%s' "$1" | tr -d '\r' | grep -qi "^$2:"; }

# 1. health (the database check passes)
code=$(status_of "$base/health")
if [ "$code" = 200 ]; then pass "GET /health 200"; else fail "GET /health answered $code"; fi

# 2. the web app and the API answer, and nothing is a server error
for path in / /index.html /ngsw.json /manifest.webmanifest /api/auth/me /api/products; do
  code=$(status_of "$base$path")
  case "$code" in
    5*|000) fail "GET $path answered $code" ;;
    *) pass "GET $path $code (no 5xx)" ;;
  esac
done

# 3. the auth door refuses an anonymous caller
code=$(status_of "$base/api/auth/me")
if [ "$code" = 401 ]; then pass "GET /api/auth/me 401 without a token"; else fail "GET /api/auth/me answered $code, expected 401"; fi

# 4. security headers on the web app and on the API
web=$(headers_of "$base/")
api=$(headers_of "$base/api/auth/me")
for h in X-Content-Type-Options Referrer-Policy X-Frame-Options Permissions-Policy Content-Security-Policy Cross-Origin-Opener-Policy; do
  if has_header "$web" "$h"; then pass "web: $h present"; else fail "web: $h missing"; fi
done
for h in X-Content-Type-Options Content-Security-Policy; do
  if has_header "$api" "$h"; then pass "api: $h present"; else fail "api: $h missing"; fi
done
if printf '%s' "$api" | tr -d '\r' | grep -qi "^cache-control:.*no-store"; then pass "api: /api/auth/* is no-store"; else fail "api: /api/auth/me is not no-store"; fi
if printf '%s' "$web" | tr -d '\r' | grep -qi "^server:.*[0-9]"; then fail "web: the Server header names a version"; else pass "web: no Server version"; fi
case "$base" in
  https://*) if has_header "$web" Strict-Transport-Security; then pass "web: HSTS present"; else fail "web: HSTS missing over https"; fi ;;
  *) echo "skip  HSTS (plain http test)" ;;
esac

# 5. optional: a real login, an authenticated read, the refresh cookie flags
if [ -n "${SMOKE_USER:-}" ] && [ -n "${SMOKE_PASSWORD:-}" ]; then
  body=$(printf '{"username":"%s","password":"%s"}' "$SMOKE_USER" "$SMOKE_PASSWORD")
  resp=$(printf '%s' "$body" | curl -sS --max-time 20 -D "${TMPDIR:-/tmp}/smoke-headers.$$" -H 'Content-Type: application/json' --data-binary @- "$base/api/auth/login" || true)
  token=$(printf '%s' "$resp" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
  if [ -n "$token" ]; then
    pass "login"
    code=$(status_of -H "Authorization: Bearer $token" "$base/api/auth/me")
    if [ "$code" = 200 ]; then pass "authenticated read 200"; else fail "authenticated read answered $code"; fi
    if tr -d '\r' < "${TMPDIR:-/tmp}/smoke-headers.$$" | grep -i '^set-cookie:' | grep -qi 'httponly' ; then pass "refresh cookie is HttpOnly"; else fail "refresh cookie is not HttpOnly"; fi
  else
    fail "login did not return a token"
  fi
  rm -f "${TMPDIR:-/tmp}/smoke-headers.$$"
fi

if [ "$failed" -eq 0 ]; then echo "smoke: all checks passed"; else echo "smoke: FAILED" >&2; fi
exit "$failed"
