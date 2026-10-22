#!/usr/bin/env bash
# Area 9: secrets scan of the tracked working tree plus supply-chain commands. Run from the repository root:  bash tests/security/pl2-purchasing/p9-config.sh
# Output goes to stdout; nothing is written inside the repository. Needs git, grep; `dotnet`, `npm` and `gh` only for the optional last block (they query the public registries / GitHub).
set -u
cd "$(git rev-parse --show-toplevel)"
files() { git ls-files | grep -v -E '(^|/)(node_modules|bin|obj|package-lock\.json|openapi/|\.snap$)|\.(png|jpg|ico|woff2?|svg|xlsx|pdf)$'; }
scan() { # label, regex
  local n; n=$(files | xargs -d '\n' grep -I -n -E -- "$2" 2>/dev/null | grep -v -E 'example\.(test|com)|<redacted>|\$\{|your-|changeme|placeholder' | head -400)
  printf '%-34s %s hits\n' "$1" "$(printf '%s' "$n" | grep -c .)"; printf '%s\n' "$n" | head -${3:-6} | cut -c1-170 | sed 's/^/    /'
}
echo "== tracked files scanned: $(files | wc -l)"
scan "private key block"             '-----BEGIN ([A-Z]+ )?PRIVATE KEY-----' 3
scan "cloud/vendor key shapes"       '(AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|xox[baprs]-[A-Za-z0-9-]{10,}|sk-[A-Za-z0-9]{32,}|AIza[0-9A-Za-z_-]{35})' 3
scan "JWT-looking strings"           'eyJ[A-Za-z0-9_-]{20,}\.eyJ[A-Za-z0-9_-]{10,}\.' 3
scan "connection string with secret" '(Password|Pwd)=[^;"'"'"' $\{]{4,};' 10
scan "password/secret assignments"   '(password|passwd|secret|apikey|api_key|token)[A-Za-z_]*"?[ ]*[:=][ ]*"[^"$\{<][^"]{7,}"' 12
scan "env files tracked"             '^$' 0 >/dev/null; git ls-files | grep -E '(^|/)\.env($|\.)' | grep -v '\.example$' | sed 's/^/    tracked env file: /'
echo "== files that hold the documented dev/CI-only credentials (expected, labelled as throw-away):"
git grep -l -E 'Ci-Only#|Dast-Only#|Ci-Admin#|Dast-Admin#' -- . ':!docs' | sed 's/^/    /'
echo "== GitHub Actions: permissions blocks, pull_request_target, pinned or tag refs"
grep -n -E 'pull_request_target|permissions:|write-all' .github/workflows/*.yml | cut -c1-140 | sed 's/^/    /'
grep -h -o -E 'uses: [^ ]+' .github/workflows/*.yml | sort | uniq -c | sort -rn | sed 's/^/    /'
echo "== Dockerfile / compose"
grep -n -E '^(FROM|USER)|read_only|cap_drop|no-new-privileges|MSSQL_SA_PASSWORD|User Id=sa' Dockerfile docker-compose.yml | cut -c1-150 | sed 's/^/    /'
if [ "${1:-}" = "deps" ]; then
  echo "== dotnet vulnerable (all projects)"; dotnet list package --vulnerable --include-transitive 2>&1 | grep -E 'has no vulnerable|Severity|>' | head -20
  echo "== npm audit (production, then all)"; (cd web && npm audit --omit=dev 2>&1 | tail -3; npm audit 2>&1 | grep -E 'vulnerabilities|Severity' | head -3)
  echo "== GitHub alerts"; gh api 'repos/soonkai97/IMS/code-scanning/alerts?state=open' --jq 'length' ; gh api 'repos/soonkai97/IMS/dependabot/alerts?state=open' --jq '.[] | [.number,.security_advisory.severity,.dependency.package.name,.dependency.scope,.security_vulnerability.first_patched_version.identifier] | @tsv'
fi
