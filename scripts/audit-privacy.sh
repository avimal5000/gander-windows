#!/usr/bin/env bash
# Privacy audit for the Windows build (mirrors the Mac app's scripts/audit-privacy.sh). It checks a published output
# folder, so run it on what you ship:
#
#   dotnet publish src/Gander.App -c Release -r win-x64 -o publish/Gander-win-x64
#   scripts/audit-privacy.sh publish/Gander-win-x64 [--allow-native <glob>]...
#
# Checks: 1 the source guard; 2 Gander's own assemblies reference no networking assembly and import no networking DLL;
# 3 the manifests ask for no administrator rights and no internet capability; 4 the only native libraries are the
# expected ones (ONNX Runtime, the .NET runtime, WPF); 5 the bundled face model matches the SHA-256 pinned in
# scripts/fetch-pose-model.sh. Works on macOS, Linux and Git Bash: it reads files, it does not run Gander.
# The runtime checks (sockets, camera light) are in docs/PRIVACY-AUDIT.md.
set -uo pipefail
cd "$(dirname "$0")/.."
repo=$(pwd)

folder=""
extra_native=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --allow-native) extra_native+=("$2"); shift 2 ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; exit 2 ;;
    *) folder="$1"; shift ;;
  esac
done
if [[ -z "$folder" || ! -d "$folder" ]]; then
  echo "Usage: scripts/audit-privacy.sh <published output folder> [--allow-native <glob>]..." >&2
  exit 2
fi

failures=0
pass() { echo "  pass: $1"; }
fail() { echo "  FAIL: $1" >&2; failures=$((failures + 1)); }
note() { echo "  note: $1"; }

# Printable strings of a binary, both as UTF-8 (metadata names) and as UTF-16 (string literals, DllImport names).
all_strings() {
  strings -a "$1" 2>/dev/null
  LC_ALL=C tr -d '\000' < "$1" | strings -a 2>/dev/null
}

is_managed() { file -b "$1" 2>/dev/null | grep -qi '\.net assembly'; }

echo "1. Source guard"
if scripts/privacy-guard.sh >/dev/null 2>&1; then pass "no networking, image persistence, input events or cursor movement in src/"
else fail "scripts/privacy-guard.sh reports forbidden APIs"; fi

echo "2. Gander's own assemblies ($folder)"
net_refs='System\.Net\.Http|System\.Net\.Sockets|System\.Net\.WebSockets|System\.Net\.WebClient|System\.Net\.Requests|System\.Net\.Security|System\.Net\.NetworkInformation|System\.Net\.Mail|System\.Net\.Quic|System\.Net\.ServicePoint|System\.Net\.HttpListener|System\.Net\.WebProxy|System\.Net\.Ping|Windows\.Networking|Windows\.Web\.Http'
net_types='HttpClient|TcpClient|UdpClient|TcpListener|ClientWebSocket|WebSocket|HttpWebRequest|WebClient|NetworkStream'
net_dlls='ws2_32|wsock32|winhttp|wininet|mswsock|dnsapi|iphlpapi|httpapi|urlmon|webservices|sensapi|rasapi32|wlanapi|bthprops'
own=()
while IFS= read -r f; do own+=("$f"); done < <(find "$folder" -maxdepth 1 \( -iname 'Gander*.dll' -o -iname 'Gander*.exe' \) | sort)
if ((${#own[@]} == 0)); then
  fail "no Gander*.dll or Gander*.exe in $folder (publish first)"
fi
for binary in "${own[@]+"${own[@]}"}"; do
  name=$(basename "$binary")
  strs=$(all_strings "$binary")
  if [[ "$name" == *.dll ]] && ! is_managed "$binary"; then
    note "$name is not a managed assembly (or 'file' cannot tell); scanning its text only"
  fi
  hit=$(grep -oE "$net_refs" <<<"$strs" | sort -u | tr '\n' ' ')
  if [[ -n "$hit" ]]; then fail "$name references networking assemblies: $hit"; else pass "$name references no System.Net.* / Windows.Networking assembly"; fi
  hit=$(grep -owE "$net_types" <<<"$strs" | sort -u | tr '\n' ' ')
  if [[ -n "$hit" ]]; then fail "$name names networking types: $hit"; else pass "$name names no HttpClient, socket or WebSocket type"; fi
  hit=$(grep -ioE "($net_dlls)(\.dll)?" <<<"$strs" | tr 'A-Z' 'a-z' | sort -u | tr '\n' ' ')
  if [[ -n "$hit" ]]; then fail "$name imports networking DLLs: $hit"; else pass "$name imports no networking DLL"; fi
  urls=$(grep -oE "https?://[A-Za-z0-9./_?=&%:#~-]+" <<<"$strs" | sort -u | tr '\n' ' ')
  if [[ -n "$urls" ]]; then note "$name embeds URL text (check it is only a namespace or a comment, never contacted): $urls"
  else pass "$name embeds no URLs"; fi
done
net_framework=$(find "$folder" -maxdepth 1 -iname 'System.Net.*.dll' -exec basename {} \; | sort | tr '\n' ' ')
if [[ -n "$net_framework" ]]; then
  note "the runtime's own networking assemblies are in the folder (self-contained publish): $net_framework"
  note "they are present but unreferenced by Gander's assemblies, as checked above"
fi

echo "3. Manifests"
manifests=()
while IFS= read -r m; do manifests+=("$m"); done < <(
  { find "$folder" -maxdepth 1 \( -iname '*.manifest' -o -iname '*.appxmanifest' \); \
    find src -name 'app.manifest' -o -name '*.appxmanifest' -o -name 'Package.appxmanifest' | grep -v '/obj/\|/bin/'; } 2>/dev/null | sort -u)
exe_manifest_checked=false
for manifest in "${manifests[@]+"${manifests[@]}"}"; do
  [[ -f "$manifest" ]] || continue
  if grep -qiE 'internetClient|internetClientServer|privateNetworkClientServer|enterpriseAuthentication|sharedUserCertificates' "$manifest"; then
    fail "$manifest declares an internet or network capability"
  else
    pass "$manifest declares no internet or network capability"
  fi
  if grep -qiE 'requestedExecutionLevel[^>]*level="(requireAdministrator|highestAvailable)"|uiAccess="true"' "$manifest"; then
    fail "$manifest asks for administrator rights or UI access"
  elif grep -qiE 'requestedExecutionLevel' "$manifest"; then
    pass "$manifest runs as the invoking user (asInvoker, no UI access)"
  fi
  exe_manifest_checked=true
done
$exe_manifest_checked || note "no manifest file found; the exe's embedded manifest was not checked"

echo "4. Native libraries"
# Expected: ONNX Runtime (head pose), the .NET runtime host, WPF's native pieces, and Windows API-set stubs.
allowed_native=(
  'onnxruntime.dll' 'onnxruntime_providers_shared.dll'
  'coreclr.dll' 'clrjit.dll' 'clrgc.dll' 'clretwrc.dll' 'hostfxr.dll' 'hostpolicy.dll' 'mscordaccore*.dll' 'mscordbi.dll'
  'mscorrc.dll' 'msquic.dll' 'ucrtbase.dll' 'vcruntime140*.dll' 'msvcp140*.dll' 'concrt140.dll' 'api-ms-win-*.dll'
  'System.IO.Compression.Native.dll' 'Microsoft.DiaSymReader.Native.*.dll' 'createdump.exe'
  'D3DCompiler_47_cor3.dll' 'PenImc_cor3.dll' 'PresentationNative_cor3.dll' 'wpfgfx_cor3.dll' 'vcruntime140_cor3.dll'
  'Gander.exe' 'GanderTools.exe'
)
allowed_native+=("${extra_native[@]+"${extra_native[@]}"}")
unexpected=""
count=0
while IFS= read -r f; do
  base=$(basename "$f")
  case "$base" in *.dll|*.exe) ;; *) continue ;; esac
  is_managed "$f" && continue
  count=$((count + 1))
  ok=false
  for pattern in "${allowed_native[@]}"; do
    # shellcheck disable=SC2254
    case "$base" in $pattern) ok=true; break ;; esac
  done
  # An apphost exe is a native stub of the same name as a managed dll.
  [[ "$base" == *.exe && -f "${f%.exe}.dll" ]] && ok=true
  $ok || unexpected+="${f#"$folder"/} "
done < <(find "$folder" -type f \( -iname '*.dll' -o -iname '*.exe' \))
if [[ -n "$unexpected" ]]; then fail "unexpected native libraries: $unexpected"
else pass "$count native files, all expected (ONNX Runtime, .NET runtime, WPF, Windows stubs)"; fi

echo "5. Face model"
want=$(sed -n 's/^SHA256="\([0-9a-f]\{64\}\)".*/\1/p' scripts/fetch-pose-model.sh | head -1)
model=$(find "$folder" -type f -name 'face_landmark.onnx' | head -1)
sha256_of() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }
if [[ -z "$want" ]]; then fail "no SHA256 pin found in scripts/fetch-pose-model.sh"
elif [[ -z "$model" ]]; then fail "no face_landmark.onnx in $folder (run scripts/fetch-pose-model.sh, then publish)"
else
  got=$(sha256_of "$model")
  if [[ "$got" == "$want" ]]; then pass "face_landmark.onnx matches the pinned SHA-256 ($want)"
  else fail "face_landmark.onnx hash $got differs from the pin $want"; fi
fi
onnx_count=$(find "$folder" -type f -iname '*.onnx' | wc -l | tr -d ' ')
if [[ "$onnx_count" == "1" ]]; then pass "exactly one model file is bundled"
else fail "$onnx_count model files in the folder (expected 1)"; fi

cd "$repo" >/dev/null
echo
if ((failures == 0)); then echo "Privacy audit passed."; else echo "Privacy audit FAILED: $failures check(s)." >&2; exit 1; fi
