#!/usr/bin/env bash
# Linux launcher equivalent to NexusForever.ClientConnector (Windows).
# Uses the same config.json shape: {"HostName":"...","Language":"en"}
# and the same WildStar command line as Program.cs.
set -euo pipefail

usage() {
	cat <<'EOF'
NexusForever client launcher (Linux)

Replaces the Windows-only ClientConnector.exe: reads or creates Client64/config.json
and starts WildStar64.exe (or WildStar32.exe) under Wine with Nexus auth flags.

To create config.json without Wine, use the workspace root scripts next to NexusForever/:
  ./nexusforever-write-client-config.sh "<Client64>" [hostname] [language]
  cp ./client-config.example.json "<Client64>/config.json"   # then edit HostName/Language

Usage:
  export WINEPREFIX="$HOME/.local/share/Steam/steamapps/compatdata/<appid>/pfx"
  ./nexusforever-client-connector.sh "/path/to/WildStar/Client64"

You can also set WILDSTAR_CLIENT64 instead of passing the path as the first argument.

Environment:
  WINEPREFIX          Required for Wine/Steam Proton prefixes (same as your game).
  WINE                Wine binary to use (default: wine64 if on PATH, else wine).
  WILDSTAR_CLIENT64   Path to Client64 if not passed as $1.

Delete config.json in Client64 to re-enter hostname and language (same as Windows).

To use Steam's Proton instead of system wine, run via your distro's "proton run" helper
or keep using WINEPREFIX + wine as long as that prefix matches the WildStar install.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
	usage
	exit 0
fi

CLIENT64="${1:-${WILDSTAR_CLIENT64:-}}"
if [[ -z "${WINEPREFIX:-}" ]]; then
	echo "warning: WINEPREFIX is unset; Wine will use the default prefix (often ~/.wine)." >&2
	echo "         For Steam/Proton WildStar, export WINEPREFIX to that game's compatdata .../pfx folder." >&2
fi

if [[ -z "$CLIENT64" ]]; then
	usage >&2
	exit 1
fi

if [[ ! -d "$CLIENT64" ]]; then
	echo "error: not a directory: $CLIENT64" >&2
	exit 1
fi

cd "$CLIENT64"
CONFIG_JSON="config.json"

if [[ ! -f "$CONFIG_JSON" ]]; then
	read -r -p "Type in your host name: " HOSTNAME
	read -r -p "Type in your language [en,de]: " LANGUAGE
	LANGUAGE=${LANGUAGE:-en}
	python3 -c 'import json,sys; json.dump({"HostName":sys.argv[1],"Language":sys.argv[2]}, open("config.json","w"))' "$HOSTNAME" "$LANGUAGE"
fi

HOSTNAME=$(python3 -c 'import json; print(json.load(open("config.json"))["HostName"])')
LANGUAGE=$(python3 -c 'import json; print(json.load(open("config.json")).get("Language") or "en")')

EXE=""
if [[ -f WildStar64.exe ]]; then
	EXE=WildStar64.exe
elif [[ -f WildStar32.exe ]]; then
	EXE=WildStar32.exe
else
	echo "error: neither WildStar64.exe nor WildStar32.exe found in $PWD" >&2
	exit 1
fi

if [[ -n "${WINE:-}" ]]; then
	:
elif command -v wine64 >/dev/null 2>&1; then
	WINE=wine64
elif command -v wine >/dev/null 2>&1; then
	WINE=wine
else
	echo "error: wine not found; install Wine or set WINE to your proton/wine binary" >&2
	exit 1
fi

# Mirrors NexusForever.ClientConnector Program.cs launch line.
exec "$WINE" "$PWD/$EXE" \
	/auth "$HOSTNAME" \
	/authNc "$HOSTNAME" \
	/lang "$LANGUAGE" \
	/patcher "$HOSTNAME" \
	/SettingsKey WildStar \
	/realmDataCenterId 9
