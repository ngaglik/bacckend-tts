#!/usr/bin/env bash
# Siapkan engine edge (edge-tts) di virtualenv lokal .venv.
# Memakai venv agar tidak bentrok dengan paket sistem (PEP 668).
set -euo pipefail

cd "$(dirname "$0")"

PYTHON="${PYTHON:-python3}"

# --clear: buat ulang venv dari nol. Wajib bila folder proyek pernah
# dipindah/di-rename: skrip console (edge-tts) menyimpan path absolut lama
# di shebang sehingga menjadi rusak (gagal start, ENOENT).
"$PYTHON" -m venv --clear .venv
./.venv/bin/pip install --upgrade pip
./.venv/bin/pip install --upgrade edge-tts

echo
echo "Selesai. edge-tts terpasang di .venv/bin/edge-tts"
echo "Jalankan service: ./run.sh --engine edge --voice id-ID-GadisNeural"
