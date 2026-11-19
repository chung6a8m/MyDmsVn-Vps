#!/usr/bin/env bash
set -euo pipefail
umask 077
export LC_ALL=C
export PATH=/usr/bin:/bin

fail() { printf 'LocalSimulation.ServiceAdmissionRefused\n' >&2; exit 1; }
[[ "$(id -un)" == "mydmsvn-local-sim" ]] || fail
[[ "${ASPNETCORE_URLS:-}" == "http://127.0.0.1:5080" ]] || fail
[[ "${MYDMSVN_VPS08_PROXY_MODE:-}" == "loopback-nginx-v1" ]] || fail
[[ -z "${ASPNETCORE_FORWARDEDHEADERS_ENABLED:-}" ]] || fail
[[ -n "${CREDENTIALS_DIRECTORY:-}" && -d "$CREDENTIALS_DIRECTORY" ]] || fail
[[ -f /opt/mydmsvn-local-sim/current/MyDmsVn.Server.AspNetCoreHost.dll ]] || fail

load_credential() {
  local target="$1" name="$2" file value
  file="$CREDENTIALS_DIRECTORY/$name"
  [[ -f "$file" && ! -L "$file" && -r "$file" ]] || fail
  value="$(cat -- "$file")" || fail
  [[ -n "$value" && "$value" != *$'\n'* && "$value" != *$'\r'* ]] || fail
  printf -v "$target" '%s' "$value"
  export "$target"
}
load_credential MYDMSVN_WEB_SQL_CONNECTION_STRING sql_connection_string
load_credential MYDMSVN_WEB_JWT_KEYS jwt_keys
load_credential MYDMSVN_VPS08_PROXY_TOKEN proxy_token
exec /opt/mydmsvn-local-sim/runtime/dotnet /opt/mydmsvn-local-sim/current/MyDmsVn.Server.AspNetCoreHost.dll
