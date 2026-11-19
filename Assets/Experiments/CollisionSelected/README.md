# Lima trial dengan seed hasil kalibrasi AI

Profil ini menggantikan profil linear awal pada scene peserta dan scene evaluasi AI.
Model tetap Phase3R Explorer-2000063.onnx; tidak ada training ulang atau tambahan
observasi policy. Profil ini memakai keluaran aksi deterministik yang sudah ada
di model ONNX. Raycast lantai/tembok tetap menjadi detector eksternal.
Transisi trial/respawn otomatis diproses pada tick fisika, dan fase keputusan pertama
diselaraskan ulang setiap episode. Protokol: collision-small-large-study-v1.

| Trial | Grid WFC (X, lantai, Z) | Target |
| --- | --- | ---: |
| L1 | 6 x 2 x 6 | 1 |
| L2 | 6 x 2 x 6 | 2 |
| L3 | 6 x 2 x 6 | 2 |
| L4 | 12 x 2 x 12 | 1 |
| L5 | 12 x 2 x 12 | 2 |

Komposisi full collider hilang / lubang sebagian: 1/0, 1/1, 1/1, 1/0, 1/1.
Rancangan ini menggantikan permintaan 1,2,3,4,5 setelah pengguna merevisi jumlah target.

Validasi akhir: round_0011_validation pada small_large_seed_search_20261028.
Seluruh target ditemukan pada 15/15 run (5 trial x 3 pengulangan), dalam budget
150 detik per run. Enam laporan salah juga tetap tercatat; keberhasilan seluruh
target tidak berarti detector bebas false positive.

| Trial | Seed WFC diminta | Seed defect | Seed spawn | Median seluruh target ditemukan (detik simulasi) |
| --- | ---: | ---: | ---: | ---: |
| L1 | 281008198 | 411016382 | 28324 | 11.38 |
| L2 | 281104102 | 411032861 | 28323 | 127.99 |
| L3 | 283827465 | 416250625 | 52039 | 40.78 |
| L4 | 283113394 | 414424226 | 43790 | 11.38 |
| L5 | 281555774 | 411311646 | 29730 | 67.28 |

Seed WFC aktual dapat berbeda setelah retry generator. selected-seeds.csv
menyimpan keduanya; manifest menyimpan layout final. HASIL_AI.md dan
timing_by_run.csv pada direktori validasi menyimpan semua pengulangan.

Ukuran disimpan per skenario di protocol.gridSize dan diterapkan sebelum generator
WFC dijalankan. Kedua scene memakai CollisionSelected.asset dan manifest yang sama.
Layout, posisi defect, spawn, dan seed tidak diacak per peserta. Urutan tetap L1..L5.
Scene peserta tetap memakai Canvas, WASD/mouse/F, checkpoint tile sehat terakhir,
lima waktu hasil, dan tombol Google Form. Pengguna melakukan build sendiri dari scene itu.
Pemain manusia juga dapat menekan Space untuk melompati celah satu tile (tinggi
nominal 1,5 m, jangkauan nominal 10 m dengan input arah). Model AI tetap memakai
forward/turn tanpa aksi lompat. Nyatakan perbedaan kemampuan ini dalam metode;
fitur manusia ini tidak mengubah seed, manifest, maupun hasil evaluasi AI yang tersimpan.

Kriteria pemilihan: AI harus menemukan SEMUA ID defect unik pada setiap trial
dalam 150 detik simulasi. Gagal menemukan salah satu target berarti seed belum lolos.
Pencarian awal menjalankan lima trial. Tahap pencarian berikutnya dapat menjalankan
hanya skenario yang belum lolos, ditandai calibrationScenarioIndices pada session.txt.
Setelah semuanya lolos,
tiga pengulangan akhir dijalankan dari awal. Syarat publikasi adalah seluruh target
tercapai pada kelima trial dalam SEMUA tiga pengulangan kampanye akhir. Kampanye
validasi yang gagal tetap disimpan untuk memeriksa keterulangan simulasi;
mode deterministik tidak menjamin physics/frame scheduling selalu identik.

Ini adalah benchmark hasil kalibrasi berdasarkan performa AI, bukan sampel acak
atau evaluasi generalisasi pada seed yang belum pernah dilihat saat seleksi.
Wajib menyebutkan proses seleksi ini saat menggunakan hasil sebagai pembanding manusia.
Seluruh percobaan gagal dan hasil mentah dipertahankan di
results/DefectExperiments/small_large_seed_search_20261028.
Kandidat awal memakai seed dari kalibrasi terdahulu, dibangun ulang dengan jumlah
target baru dan diuji ulang. Tahap lanjutan dapat mempertahankan peta/spawn
dari jejak AI terdahulu yang menjelajahi minimal 30% tiap lantai, lalu menyaring
seed defect berdasarkan kedekatan dengan rute tersebut. Semua seed hasil saringan
tetap diuji ulang pada peta yang benar-benar memiliki defect. Tidak ada koordinat
defect yang dipindahkan sesudah evaluasi; penolakan filter juga dicatat di journal.
Kandidat juga disaring melalui graph walkway: sel yang kehilangan seluruh collider
dihapus dari graph, lalu akses dari spawn ke daerah target diperiksa. Saringan ini
tidak dihitung sebagai temuan AI. Pencarian spawn mencakup kedua lantai; spawn
terpilih akhirnya dibekukan dan dipakai sama oleh AI maupun manusia.
selected-seeds.csv menyimpan seed/ukuran/waktu keberhasilan pilihan;
selection.done menunjuk direktori kampanye akhir; search-journal.txt mencatat pencarian.

Inference seed 58231 tetap dicatat; profil ini menggunakan output aksi deterministik,
sehingga tidak melakukan sampling aksi stochastic. LSTM direset per episode. Time scale batch 20 hanya untuk
mempercepat komputasi; waktu gameplay pembanding adalah simulation_seconds AI.
Timer manusia memakai waktu aktif pada time scale 1. Timestamp seluruh target
ditemukan berbeda dari batas trial 150 detik. Trial manusia otomatis berhasil setelah
seluruh target ditemukan; timeout sebelum target lengkap dicatat gagal, bukan sukses.

Protokol dan hasil linear awal tetap tersedia di Assets/Experiments/CollisionLinear
dan results/DefectExperiments/linear_walls_ai_baseline_20261028. Tidak dicampur dengan
hasil profil terpilih ini karena ukuran, seed, dan hash protokol berubah.
Pencarian lama untuk jumlah 1,2,3,4,5 dihentikan sesuai revisi pengguna dan diarsipkan
di results/DefectExperiments/progressive_seed_search_fixedstep_20261028.
Percobaan pencarian stochastic yang belum memenuhi seluruh target juga tetap
disimpan di results/DefectExperiments/progressive_seed_search_20261028.
Percobaan deterministik sebelum perbaikan transisi fisika disimpan terpisah di
results/DefectExperiments/progressive_seed_search_deterministic_20261028.
