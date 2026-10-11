#!/usr/bin/env bash
# Jalankan service TTS kiosk.
#
# Contoh:
#   ./run.sh --engine edge --voice id-ID-GadisNeural
#   ./run.sh --engine edge --voice id-ID-GadisNeural --port 9292 --token rahasia
#   ./run.sh --player "mpv --no-video --really-quiet"
#
# Bila ada virtualenv lokal (.venv) hasil ./setup.sh, edge-tts dari venv
# dipakai otomatis.
set -euo pipefail

cd "$(dirname "$0")"

if [ -d ".venv/bin" ]; then
    export PATH="$PWD/.venv/bin:$PATH"
fi

exec dotnet run --configuration Release -- "$@"
