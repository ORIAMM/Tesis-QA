# Phase2E — memori ramp episodik sebagai observasi PPO

26 Oktober 2026. Tidak ada tag Stair, waypoint/planner, action mask, perubahan
reward atau gerakan otomatis. Lokasi hanya berasal dari collider miring yang
terukur oleh sensor terrain atau kontak ground nyata.

## Apa yang diingat

- Sensor terrain 1,5/3/6 m tetap sama. Ketika ML-Agents menulis pengamatannya,
  sampel permukaan yang terlihat dikirim ke memori. Capture diagnostik/Gizmos
  tidak mengisi memori episode.
- Kandidat kemiringan 8..50 derajat (juga dibatasi slope limit controller).
  Bukan pengenal semantik "tangga": slope lain yang memenuhi syarat juga dapat
  diingat. Anak tangga datar saja tidak menjadi kandidat; smooth ramp collider
  yang sudah ada diperlukan untuk observasi normal miring.
- Maksimum 32 landmark, 16 sampel per landmark. Pengamatan dari collider yang
  sama dikelompokkan dalam jarak 8 m/normal searah. Collider berbeda tidak
  digabung hanya karena berdekatan; ramp yang terdiri dari banyak collider bisa
  menghasilkan beberapa landmark. Tidak membaca bounds/nama/tag prefab atau WFC.
- Setelah dilihat, posisi tetap diingat saat di belakang atau tertutup tembok.
  Ramp yang BELUM terlihat tidak boleh masuk memori melalui tembok. Tidak
  mengarang lokasi/ujung ramp yang belum terlihat atau belum dilewati.
- "Touched" berarti pernah ada kontak ground terukur pada incline. Konfirmasi
  perpindahan tinggi membutuhkan rangkaian gerak yang didukung ground, dari
  permukaan datar, menyusuri ramp, lalu kembali ke permukaan datar dengan beda
  tinggi >=2 m dan perpindahan horizontal >=1 m. Teleport/jeda tanpa dukungan
  memutus bukti. Ini bukti perpindahan ketinggian, bukan oracle nomor lantai atau
  jaminan seluruh rute bebas cacat. Putar balik singkat tidak dihitung.
- Setelah konfirmasi, titik masuk/keluar yang benar-benar dilalui dapat menjadi
  lokasi referensi. Tanpa konfirmasi, hanya titik sampel yang benar-benar dilihat
  digunakan. Tidak menebak landing dari panjang/bounds collider.
- Seluruh memori direset saat episode baru (termasuk pergantian map). Checkpoint
  menyimpan bobot, bukan koordinat dari episode sebelumnya.

## Observasi tambahan: 16 nilai

Nama `ZZZ Explorer Ramp Memory v1` diurutkan SETELAH semua 161 input lama.
Total sekarang **177**: `[24,34,49,10,32,12,16]`. Vector Size dasar tetap **10**.

Dua landmark yang paling dekat secara spasial ditampilkan, masing-masing:

1. Ada catatan landmark (0/1).
2. Arah relatif kanan/kiri.
3. Arah relatif depan/belakang.
4. Jarak horizontal, dijepit pada skala 50 m.
5. Beda tinggi titik referensi, skala +/-8 m.
6. Kemiringan yang diingat dalam arah hadap sekarang, skala +/-90 derajat.
7. Pernah disentuh (0/1).
8. Perpindahan tinggi terkonfirmasi (0/1).

Arah diperbarui sesuai posisi/yaw agent saat ini, meskipun ramp tidak terlihat.
Ini arah geometris menuju lokasi yang DIINGAT, BUKAN arah rute bebas tembok.
PPO tetap harus mempelajari cara mencapainya; tidak ada jaminan hilang semua loop.
Tidak menambahkan floor-coverage oracle, penalti revisit baru, atau larangan
melewati jalur lama. Transit lewat area lama tetap boleh.

## Checkpoint migrasi

Phase2D selesai di **5.000.036**, level **9**. Sumber disalin permanen:
`results/RecoveryBackups/Phase2D_before_ramp_memory_5000036/Explorer-5000036.pt`.
SHA256 `bb1773069411f28eaf68f24ad80bea56c3f898510b26ffc3e562d44059d8ccd4`.

Tool `Training/prepare_ramp_memory.py` SUDAH menyiapkan run terpisah **Phase2E**.
Source Phase2D tidak diubah. Actor/critic/curiosity/LSTM dan normalizer lama
disalin; input 161->177 ditambah 16 kolom nol, normalizer baru segar. Optimizer
direset untuk fine-tuning; counter tetap **5.000.036**. Window mastery 0/100 pada
level **9**, bukan kembali ke Phase 1 atau mengulang level 8.

Encoding policy/value dan LSTM pada input lama sama, bahkan dengan input baru
nonnol: error maksimum sekitar **1,25e-6**. Input baru bisa menerima gradien.
Ini validasi kompatibilitas, bukan hasil training memori ramp. Model migrasi awal
masih mengabaikan 16 input baru sampai dilatih.

Model: `results/Phase2E/Explorer.onnx` dan `Explorer/Explorer-5000036.pt`.
Laporan: `results/Phase2E/migration.json`.

## Budget dan menjalankan training

**Budget disetujui 26 Oktober: total 7 juta step**, sekitar 2 juta tambahan dari
checkpoint 5.000.036 (tepatnya 1.999.964). YAML dan konfigurasi run telah
disesuaikan. Training belum dijalankan otomatis. Jangan `--force` atau mengganti
source dengan checkpoint acak.

Command dari root proyek:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_RampMemory.yaml --run-id=Phase2E --resume
```

Stop Play/trainer lama, tunggu compile, buka ulang `AI_Phase2_SmallWFC`.
Terrain Profile, Branch Memory, Distant Terrain, dan **Ramp Memory ON**;
Long Range Terrain Profile (mode lama pengganti 3->6 m) **OFF**.
Behavior Type Default, Model kosong, Vector Size 10. Tekan Play saat trainer
menunggu. Time scale tetap **30x**, speed 6, MaxStep/reward/curriculum tetap.
Counter sumber diwariskan; tidak berarti Phase2E sudah melatih 5 juta step baru.

Untuk menonton model Phase2D **161 input**, Ramp Memory harus **OFF sebelum
Play** (Terrain/Branch/Distant tetap ON, Long Range OFF). Untuk Phase2E 177 input,
Ramp Memory ON. Pilih Inference Only dan time scale 1x ketika menonton.
Phase 1 tidak diubah; scene Phase 2/3 sudah memakai pilihan 177 input.

Pantau `AI/Ramp Memory Active`, `AI/Ramps Remembered`, `AI/Ramps Touched`,
`AI/Ramps Height Traversed`, di samping mastery/coverage per episode dan transfer
naik/turun. Hitungan memory merupakan jumlah landmark, bukan jumlah episode,
bukan ground-truth jumlah tangga, dan bukan jumlah sukses tugas coverage.

## Validasi dan hasil sebelumnya

52 tes Python lolos: preservasi bobot/counter, schema ONNX, gradien input baru,
penolakan overwrite/run lama/budget habis, serta preflight trainer tanpa memulai
training. Tes lama juga diperbaiki agar tidak menganggap checkpoint run aktif
masih identik dengan bobot migrasi awal setelah training berjalan.

Uji Unity batch pada `.ai-eval` mencakup ramp terlihat/occluded, ingatan di
belakang, trigger, reset, deduplikasi, event sensor asli, dukungan ground pada
lintasan naik/turun, penolakan konfirmasi teleport/putar balik singkat, serta
schema 177 dan semua tes geometri terrain/cabang lama. Lihat
`Logs/ramp-memory-release.log` (exit 0). Uji lintasan sintetis bukan evaluasi policy.

Hasil Phase2D SEBELUM memori ramp: lihat
`results/Phase2D/HASIL_TRAINING_5000036.md`. Jangan mengatribusikan hasil training
Phase2D tersebut pada memori ramp yang baru ditambahkan.
