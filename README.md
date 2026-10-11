# backend-tts — Text-to-Speech Kiosk

Service **Text-to-Speech (TTS)** untuk pengumuman nomor antrian di ruang
tunggu. Dibangun dengan **ASP.NET Core minimal API (.NET 10)** dan dijalankan
**melalui URL** — dipanggil oleh `erp/backend` (modul antrian) dan halaman
**Uji Suara** di `erp/antrian`.

- **Engine:** `edge` (Microsoft Edge Read Aloud via [`edge-tts`](https://github.com/rany2/edge-tts))
- **Voice default:** `id-ID-GadisNeural` (Bahasa Indonesia, perempuan)

## Menjalankan

```bash
# 1. Pasang engine edge-tts di venv lokal (sekali saja)
./setup.sh

# 2. Jalankan service
./run.sh --engine edge --voice id-ID-GadisNeural
# → http://0.0.0.0:9292
```

Buka `http://localhost:9292` untuk halaman uji (status, uji suara, uji panggil).

> Tanpa `./setup.sh`, service tetap mencari `edge-tts` di PATH lalu
> `python3 -m edge_tts`. Pasang manual dengan `pip install edge-tts` bila
> tidak memakai venv.

## Konfigurasi

Tiga sumber, prioritas dari rendah ke tinggi: `appsettings.json`
(section `Kiosk`) → environment variable (`Kiosk__Voice=...`) → argumen
command line.

| Argumen | Default | Keterangan |
| --- | --- | --- |
| `--engine` | `edge` | Engine TTS (hanya `edge`). |
| `--voice` | `id-ID-GadisNeural` | Voice edge-tts. |
| `--rate` | `+0%` | Kecepatan bicara, mis. `-10%`. |
| `--volume` | `+0%` | Volume. |
| `--pitch` | `+0Hz` | Nada suara. |
| `--host` / `--port` | `0.0.0.0` / `9292` | Alamat bind Kestrel. |
| `--token` | kosong | Bila diisi, endpoint aksi wajib `?token=`. |
| `--repeat` | `1` | Pengulangan default. |
| `--max-repeats` | `3` | Batas atas `repeat`. |
| `--player` | otomatis | Perintah pemutar audio, mis. `"mpv --no-video --really-quiet"`. |
| `--edge-tts` | otomatis | Perintah edge-tts, mis. `"python3 -m edge_tts"`. |
| `--spell-numbers` | `true` | Eja digit nomor antrian (`A003` → "A, nol, nol, tiga"). |
| `--call-template` | lihat bawah | Template teks panggilan awal. |
| `--serving-template` | lihat bawah | Template teks saat sedang diproses. |
| `--done-template` | lihat bawah | Template teks saat selesai. |
| `--max-text-length` | `1500` | Panjang maksimum teks `/speak`. |
| `--max-queue` | `10` | Kapasitas antrian bicara. |

### Pemutar audio

MP3 hasil edge-tts diputar lewat subprocess. Bila `--player` tidak diisi,
service mendeteksi otomatis dengan urutan: `mpv`, `ffplay`, `cvlc`, `play`
(sox), `paplay`, `aplay`. Untuk `paplay`/`aplay` (hanya WAV), audio
dikonversi otomatis memakai `ffmpeg` atau `sox` bila tersedia.

Disarankan memasang `mpv` agar MP3 langsung diputar:

```bash
sudo apt install mpv          # atau: sudo apt install ffmpeg
```

### Template panggilan

```
--call-template "Nomor antrian {number}{service}{customer}, silakan menuju {counter}."
--serving-template "Nomor antrian {number}{service}{customer}, sedang diproses di {counter}."
--done-template "Nomor antrian {number}{service}{customer}, telah selesai dilayani. Terima kasih."
```

Token `{number}`, `{counter}`, `{service}`, `{customer}` (menjadi " atas nama
<nama>" bila terisi; kosong → dihapus otomatis). Bila `{service}` kosong, sisa
koma/spasi dirapikan otomatis. `{counter}` kosong menjadi "loket pelayanan".

Template dipilih berdasarkan parameter `phase` pada `/call`:
`phase=call` (default) → `CallTemplate`, `phase=serving` → `ServingTemplate`,
`phase=done` → `DoneTemplate`.

Template juga dapat **ditimpa per panggilan** lewat parameter `template` pada
`/call` — dipakai oleh halaman **Pengaturan** di `erp/antrian` yang menyimpan
template di database (tabel `que.call_setting`). Nilai `template` yang diisi
menang atas konfigurasi service di atas.

## API

| Method | Endpoint | Keterangan |
| --- | --- | --- |
| GET | `/` | Halaman uji (HTML). |
| GET | `/health` | Info engine, voice, status bicara, panjang antrian. |
| GET | `/call` | Panggil nomor antrian → `?number=&counter=&service=&repeat=&phase=&template=`. |
| GET | `/speak` | Bacakan teks bebas → `?text=&repeat=`. |
| GET | `/stop` | Hentikan bacaan & kosongkan antrian. |
| GET | `/voices` | Daftar voice edge-tts. |

Semua endpoint aksi menerima token via `?token=` atau header `X-Kiosk-Token`
(bila `--token` diisi). Permintaan selalu dijawab cepat (fire-and-forget);
bila antrian penuh, respons `503`.

### Contoh

```bash
curl "http://localhost:9292/call?number=A003&counter=1&service=Umum"
curl "http://localhost:9292/speak?text=Selamat%20pagi&repeat=2"
curl "http://localhost:9292/stop"
```

## Integrasi dengan `erp/backend` / `erp/antrian`

Arahkan konfigurasi TTS di `erp/backend` (`appsettings.json` atau environment
variable) ke alamat service ini:

```
Kiosk__BaseUrl=http://127.0.0.1:9292
Kiosk__Repeats=1
Kiosk__Token=rahasia      # bila service dijalankan dengan --token
```

`KioskAnnouncer` di `erp/backend` akan memanggil `/call`, `/speak`, dan
`/stop` sesuai kontrak di atas.

## Struktur

```
erp/backend-tts
├── Program.cs                 # bootstrap, konfigurasi, host
├── appsettings.json           # default section "Kiosk"
├── run.sh / setup.sh          # jalankan service / pasang edge-tts
├── BackendTts.http            # contoh request
├── Endpoints/
│   └── KioskEndpoints.cs      # /health /call /speak /stop /voices
├── Models/
│   ├── KioskOptions.cs        # opsi + validasi + override CLI
│   └── SpeechJob.cs           # job & hasil
├── Services/
│   ├── ITtsEngine.cs          # kontrak engine
│   ├── EdgeTtsEngine.cs       # engine edge (edge-tts)
│   ├── AudioPlayer.cs         # pemutar audio subprocess
│   ├── MediaConverter.cs      # mp3 → wav (ffmpeg/sox)
│   └── SpeechService.cs       # antrian + worker latar
├── Helpers/                   # proses, PATH, ejaan teks
└── wwwroot/index.html         # halaman uji
```

## Produksi

```bash
dotnet publish -c Release -o publish
./publish/backend-tts --engine edge --voice id-ID-GadisNeural --port 9292
```

Jalankan sebagai service (systemd/supervisor) di PC kiosk dengan pengeras
suara terpasang. Karena antrian dijalankan di dalam proses, cukup satu
instance per perangkat audio.

### Deploy di server production (systemd)

Skrip `deploy/install.sh` memasang service di server production: menyiapkan venv
edge-tts, `dotnet publish`, membuat unit systemd, lalu `enable --now`. Jalankan
dari folder proyek:

```bash
sudo ./deploy/install.sh

# override sesuai server, mis.:
sudo RUN_USER=www-data HOST=127.0.0.1 PORT=9292 RATE=-10% ./deploy/install.sh
```

Prasyarat & catatan:

- **Server harus punya perangkat audio/pengeras suara** — TTS di server tanpa
  output suara tidak ada gunanya.
- Unit menambahkan `SupplementaryGroups=audio` (cukup untuk **ALSA**). Bila
  server memakai **PulseAudio** per-sesi desktop, jalankan service sebagai user
  desktop itu (`RUN_USER=<user-desktop>`) atau aktifkan PulseAudio system-wide —
  system user biasa tidak bisa mengakses sesi Pulse milik user lain.
- Bila API (`erp/backend`) ada di server yang sama, bind lokal saja:
  `sudo HOST=127.0.0.1 ./deploy/install.sh`, lalu set `Kiosk:BaseUrl` backend ke
  `http://127.0.0.1:9292`. Pakai `HOST=0.0.0.0` (default) hanya bila perlu
  dipanggil dari mesin lain — dan buka firewall port-nya.
- Set `TOKEN=...` bila service terpapar jaringan.
- Cek setelah pasang: `curl -s http://127.0.0.1:9292/health` (menampilkan
  engine, voice, dan **pemutar** yang dipakai).

### Menyalin ke PC kiosk

Deploy **tanpa** `bin/`, `obj/`, dan `.venv/`. Venv Python terikat ke mesin
pembuatnya (shebang-nya berisi path absolut), jadi jangan pernah disalin — buat
ulang di PC tujuan:

```bash
# salin proyek tanpa artefak build & venv
rsync -a --exclude bin --exclude obj --exclude .venv ./ kiosk:/opt/backend-tts/

# buat venv edge-tts di PC tujuan (sekali saja)
ssh kiosk 'cd /opt/backend-tts && ./setup.sh'
```

Bila folder berisi `.venv` hasil salinan sudah terlanjur ada, hapus dulu di PC
tujuan lalu buat ulang: `rm -rf .venv && ./setup.sh`.

## Masalah umum

### `Win32Exception (2): ... .venv/bin/edge-tts ... No such file or directory`

Venv Python tidak portabel: skrip `.venv/bin/edge-tts` menyimpan **path
absolut** di baris shebang. Menyalin atau me-rename folder proyek (mis.
`backend-kiosk` → `backend-tts`) membuat shebang menunjuk interpreter lama
sehingga gagal dijalankan. Buat ulang venv **di mesin tersebut**:

```bash
rm -rf .venv
./setup.sh
```

Jangan salin folder `.venv` antar mesin — folder ini sudah masuk `.gitignore`
dan harus dibuat ulang per mesin. Bila `edge-tts` tidak ditemukan, service
otomatis mencoba `python3 -m edge_tts`; bila keduanya tidak tersedia, jalankan
`./setup.sh` atau set `--edge-tts "python3 -m edge_tts"`.
# bacckend-tts
