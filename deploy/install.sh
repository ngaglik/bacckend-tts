#!/usr/bin/env bash
# Pasang / perbarui backend-tts sebagai systemd service di server production.
#
# Jalankan dari folder proyek, sebagai root:
#   sudo ./deploy/install.sh
#
# Semua nilai bisa di-override lewat environment variable, mis.:
#   sudo APP_DIR=/opt/backend-tts RUN_USER=www-data PORT=9292 HOST=0.0.0.0 \
#        VOICE=id-ID-GadisNeural RATE=-10% ./deploy/install.sh
#
# Catatan: APP_DIR harus berisi salinan proyek (BackendTts.csproj + setup.sh).
# Untuk deploy ke /opt, salin dulu proyeknya ke sana (tanpa .venv/bin/obj).
#
# Yang dilakukan skrip:
#   1. menyiapkan venv edge-tts (./setup.sh) bila belum ada / rusak
#   2. dotnet publish -c Release
#   3. memastikan user service ada & boleh akses audio
#   4. menulis unit systemd + enable --now
#
# Aman dijalankan berulang (idempotent).
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

APP_DIR="${APP_DIR:-$PROJECT_DIR}"
RUN_USER="${RUN_USER:-$(stat -c '%U' "$APP_DIR")}"
SERVICE_NAME="${SERVICE_NAME:-backend-tts}"
PORT="${PORT:-9292}"
HOST="${HOST:-0.0.0.0}"
VOICE="${VOICE:-id-ID-GadisNeural}"
RATE="${RATE:-+0%}"
TOKEN="${TOKEN:-}"
PLAYER="${PLAYER:-}"

die() { echo "ERROR: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "jalankan sebagai root (sudo)."
command -v dotnet >/dev/null || die "dotnet tidak ditemukan di PATH."
[[ -f "$APP_DIR/BackendTts.csproj" ]] || die "APP_DIR harus berisi proyek backend-tts (BackendTts.csproj): $APP_DIR"
[[ -x "$APP_DIR/setup.sh" ]] || die "setup.sh tidak ada/tidak executable di $APP_DIR"

echo "==> Proyek       : $PROJECT_DIR"
echo "==> APP_DIR      : $APP_DIR"
echo "==> Service user : $RUN_USER"
echo "==> Listen       : http://$HOST:$PORT"

# ── 1. Venv edge-tts ────────────────────────────────────────────────
# Venv tidak portabel: skrip .venv/bin/edge-tts menyimpan path absolut.
# Buat ulang bila hilang atau gagal dijalankan (mis. setelah folder dipindah).
if ! "$APP_DIR/.venv/bin/edge-tts" --help >/dev/null 2>&1; then
    echo "==> Menyiapkan venv edge-tts (./setup.sh)"
    (cd "$APP_DIR" && ./setup.sh)
else
    echo "==> Venv edge-tts OK"
fi

# ── 2. Publish rilis ────────────────────────────────────────────────
echo "==> Publish rilis (dotnet publish -c Release)"
(cd "$APP_DIR" && dotnet publish -c Release -o publish)

# ── 3. User & akses audio ───────────────────────────────────────────
if ! id -u "$RUN_USER" >/dev/null 2>&1; then
    echo "==> Membuat user sistem '$RUN_USER'"
    useradd --system --home-dir "$APP_DIR" --shell /usr/sbin/nologin "$RUN_USER"
fi

# Grup 'audio' cukup untuk ALSA. Untuk PulseAudio per-sesi desktop, jalankan
# service sebagai user desktop itu (lihat README) atau Pulse system-wide.
if getent group audio >/dev/null; then
    usermod -aG audio "$RUN_USER" || true
fi

chown -R "$RUN_USER":"$RUN_USER" "$APP_DIR"

# ── 4. Unit systemd ─────────────────────────────────────────────────
ARGS=(--engine edge --voice "$VOICE" --rate "$RATE" --host "$HOST" --port "$PORT")
if [[ -n "$PLAYER" ]]; then ARGS+=(--player "$PLAYER"); fi
if [[ -n "$TOKEN" ]]; then ARGS+=(--token "$TOKEN"); fi

UNIT="/etc/systemd/system/${SERVICE_NAME}.service"
echo "==> Menulis $UNIT"
cat > "$UNIT" <<EOF
[Unit]
Description=Antrian TTS backend (Text-to-Speech)
After=network-online.target sound.target
Wants=network-online.target

[Service]
Type=simple
User=$RUN_USER
Group=$RUN_USER
SupplementaryGroups=audio
WorkingDirectory=$APP_DIR/publish
Environment=PATH=$APP_DIR/.venv/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
ExecStart=$APP_DIR/publish/backend-tts ${ARGS[*]}
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable "$SERVICE_NAME"
# restart = mulai bila belum jalan, terapkan binary/konfig baru bila sudah jalan.
systemctl restart "$SERVICE_NAME"
systemctl --no-pager --full status "$SERVICE_NAME" || true

echo
echo "Selesai. Uji: curl -s http://127.0.0.1:$PORT/health"
echo "Ubah opsi: edit $UNIT lalu 'systemctl daemon-reload && systemctl restart $SERVICE_NAME'."
