# Phase2M4: mastery curriculum, anti-spin bertahap, lalu coverage

**Update 17 Oktober: run sudah 3 juta step, gunakan --resume, bukan --force.**
Lihat [README_STAIR_PRACTICE.md](README_STAIR_PRACTICE.md) untuk latihan tangga
bertahap dan budget resume 5 juta kumulatif. Instruksi restart di bawah hanya
untuk memulai run baru secara sengaja.

Versi mastery pada 16 Oktober 2026 menggantikan curriculum berbasis jumlah step.
Default ID tetap Phase2M4; snapshot Phase1M step 1.000.010
dipin di results/Phase2_initialization/Phase1M-1000010.pt. Bobot Phase 2 lama
tidak dipakai untuk reset ini. Penghitung step dan curriculum mulai dari nol.

## Mulai training

Stop Play Mode di Unity. Buka scene `Assets/Scenes/AI/AI_Phase2_SmallWFC.unity`.
Behavior Type harus Default dan Model kosong. Jalankan dari root proyek:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch.yaml --run-id=Phase2M4 --force
```

Lalu tekan Play setelah terminal menunggu koneksi. Script pembantu tetap menjalankan
trainer PPO ML-Agents 1.1.0; file library terpasang tidak diedit. Gunakan
entry point ini, bukan command mlagents-learn biasa: library bawaan hanya
mendukung transisi berdasarkan reward/progress, bukan keberhasilan per arah.
Angka threshold 1000000 di YAML sengaja menonaktifkan fallback bawaan;
itu BUKAN target reward. Driver Python memeriksa keberhasilan episode aktual.

Gunakan --force hanya untuk reset yang memang diminta,
bukan untuk melanjutkan run. Budget awal 3 juta step baru, time scale 40,
learning rate tetap 1e-4. Satu lantai memakai Max Step 4000; dua lantai tetap
6000. Periksa kemajuan sekitar 500 ribu step. Phase 1 tidak diulang.

Command Python di atas otomatis memindahkan hasil lama ke
results/restart_backups/<run-id>_<timestamp> sebelum reset. Ia menolak overwrite
jika port trainer masih dipakai. Launcher PowerShell berikut juga tersedia:

```powershell
powershell -ExecutionPolicy Bypass -File Assets/Scripts/AI/TrainingConfigs/Restart_Phase2.ps1
```

Default target adalah Phase2M4. Jika target belum memiliki hasil,
tidak ada data yang dipindah/dihapus. Jika sudah ada, folder hasil dipindahkan
ke results/restart_backups/<run-id>_<timestamp> sebelum training baru dimulai.
Opsi -RunId Phase2M3 tersedia hanya jika ID itulah yang ingin ditimpa;
hasil 8 jutanya juga dicadangkan. Gunakan -DryRun untuk melihat target tanpa
memindahkan data atau menjalankan training. Launcher menolak restart bila
trainer dengan ID yang sama masih berjalan. Cadangan tidak dibersihkan otomatis.

## Revisit dan perjalanan kembali

- Visit count dan sensor memori tetap dicatat. Area lama tidak memberi
  novelty reward tambahan dan tidak mendapat penalti berdasarkan jumlah visit.
- A-B-A sekali diperbolehkan. Penalti short loop baru berlaku setelah tiga
  putaran tertutup pada pola dua sampai empat area tanpa progres. Setelah itu,
  penalti paling sering satu kali per putaran, bukan setiap trigger entry.
- Area baru, midpoint tangga pertama, milestone tangga/floor pertama, atau
  rekor jarak rute transfer yang membaik >=0,25 tile menghapus bukti loop.
- Kelonggaran backtracking dibatasi TOTAL 30 detik per episode. Revisit pertama
  menyediakan 10 detik; jalur balik yang melewati 4/8 area lama berbeda atau
  milestone tangga/floor dapat menambah sampai cap itu.
  Waktu ekstra baru dipakai ketika timeout dasar habis; yang sudah dipakai
  tidak terisi ulang saat menemukan progres baru.
- Masuk trigger tangga berulang tidak mereset timer. Konfirmasi perpindahan
  lantai memberi kelonggaran hanya sekali per arah dalam episode; up-down
  berulang tidak mengisi ulang budget. Tidak ada reward positif tambahan untuk ini.
- Timer sekarang mengukur waktu sejak progres eksplorasi/navigasi yang sah,
  bukan semata sejak tile baru. Ukuran observasi tetap sama. Anti-spin,
  collision cost, step cost, dan Max Step tetap melindungi dari diam/loop panjang.

Grafik tambahan: AI/Backtracking Grace Granted dan AI/Backtracking Grace Used.
AI/Short Loops kini berarti loop berulang yang terkonfirmasi, sehingga definisinya
berbeda dari run lama yang langsung menghukum A-B-A.

Jika run terhenti, Stop Play Mode lalu gunakan YAML resume tanpa init_path:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch_Resume.yaml --run-id=Phase2M4 --resume
```

## Perubahan reward

Hanya lesson transfer bertahap yang mendapat shaping, dan hanya ketika
parameter `stair_search_shaping=1`. BFS socket WFC menghitung jarak rute ke
mulut tangga yang terhubung ke lantai tujuan. Interpolasi menuju tetangga
rute memberikan progres di dalam tile; setelah memasuki trigger tangga,
potential dipertahankan di nilai tujuan hingga episode berakhir.

Potential: `Phi = 0.3 * max(0, 1 - distance_in_tiles / 8)`.
Reward tambahan setiap keputusan policy: `0.99 * Phi(next) - Phi(previous)`.
Keputusan pertama hanya menyimpan potential awal. Reward tidak dihitung
ulang pada physics tick di antara dua keputusan (Decision Period scene: 5).
Gamma shaping harus sama dengan gamma reward extrinsic YAML, yaitu 0.99.

Pada sukses, gagal, atau batas 6000 step, potential terminal dibuat nol
dengan koreksi `-Phi(previous)`, tepat sebelum reward terminal dikirim.
Karena itu, hasil discounted shaping menutup menjadi konstanta awal dan
bolak-balik tidak dapat memanen keuntungan. Berdiam memiliki reward shaping
per keputusan yang negatif. Jumlah shaping satu episode bisa negatif walau
agent berhasil; periksa success dan traversal, bukan tanda reward saja.

Khusus transfer dengan shaping aktif, batas langkah diperlakukan sebagai
terminal kegagalan finite-horizon, bukan timeout yang di-bootstrap. Ini
menjamin koreksi terminal dikirim sebelum ML-Agents menutup trajectory.
Run lama tanpa shaping mempertahankan perilaku timeout-nya.

Reward ini menggunakan informasi rute dari generator selama training.
Koordinat/jarak tangga tidak ditambahkan ke input policy. Ini adalah reward
shaping extrinsic, bukan curiosity murni; jelaskan penggunaannya dalam metode
penelitian. Coverage dan evaluasi held-out mematikan shaping ini.

## Lesson dan grafik

`stair_search_lesson` memakai index tersendiri:

- AI/Lesson 0: eksplorasi satu lantai, target coverage scene 20%.
  Naik setelah >=75% sukses dalam 100 episode terakhir.
- AI/Lesson 1: pendekatan tangga paling dekat (0–1 tile), menghadap tangga,
  hanya naik. Naik lesson setelah >=80% sukses naik dalam 100 episode terakhir
  dengan minimal 75 percobaan naik.
- AI/Lesson 2: spawn 0–2 tile, 75% naik dan 25% turun, yaw acak.
  Naik setelah 100 episode terakhir memuat minimal 20 percobaan per arah,
  dan keberhasilan masing-masing arah >=75%. Rata-rata gabungan tidak cukup.
- AI/Lesson 3: campuran dekat 0–2 dan jauh 3–4 tile, jika tersedia.
  Ini lesson akhir pencarian; coverage tetap menunggu evaluasi held-out.

Tidak ada perpindahan otomatis di step 1 juta/1,5 juta. Budget 3 juta tetap
merupakan batas latihan, bukan bukti penguasaan; jika belum menguasai tahapnya,
agent tetap di tahap itu sampai budget habis. Jangan resume run lama berbasis
step dengan konfigurasi ini. State mastery dan 100 outcome terakhir tersimpan
bersama training_status.json agar resume run BARU mempertahankan lesson/window.
Window dikosongkan setiap lesson naik. Driver saat ini mendukung satu
environment/area dengan satu agent Explorer, sesuai scene Phase 2.

Grafik AI/Mastery/episodes, success, up_success, down_success, up_trials,
dan down_trials menampilkan window mastery. AI/Mastery Outcome adalah kode
internal pengiriman episode, BUKAN reward atau persentase keberhasilan.
Runtime juga menahan lesson >=4 ketika require_stair_eval_gate=1 tetapi
stair_eval_gate_passed belum diset oleh konfigurasi hasil pemeriksaan.

## Anti-spin baru (waktu simulasi)

- Kurang dari 0,5 unit gerak 3D selama 4 detik: peringatan -0,02, tidak
  langsung end. Jika tetap demikian 4 detik berikutnya: end dengan penalti
  noDisplacementPenalty scene (Phase 2: -0,25).
- Gerakan melingkar kecil: harus menjauh 4 unit horizontal dari patokan.
  Window minimum 12 detik satu lantai / 18 detik dua lantai. Pelanggaran
  pertama memberi peringatan -0,02; window berikutnya tanpa pemulihan mengakhiri
  episode. Gerak di atas 0,5 unit tidak dapat mereset window radius ini.
- Progres sah (area baru, milestone tangga pertama, perpindahan lantai pertama
  per arah, atau rekor jarak rute transfer membaik 0,25 tile) mereset kedua window.
  Revisit trigger yang sama atau naik-turun berulang bukan progres baru.
- Pemeriksaan macet lama 3 detik tetap hanya untuk Phase 1. Ia tidak lagi
  memotong window pemulihan Phase 2/3. Kill height dan MaxStep tetap berlaku.
- Scene lama otomatis mendapat minimum 12/18 detik saat runtime, meskipun
  Inspector masih menyimpan nilai lama 5/8. Tidak perlu rebuild scene.
- Grafik: AI/Anti Spin Warnings, AI/Stationary Failure. AI/No Displacement
  Failure sekarang menghitung kegagalan setelah kesempatan pemulihan.

Tidak ada jaminan agent langsung berhenti berputar; ini menutup jalur terminasi
yang terlalu dini dan memberi sinyal kegagalan yang lebih spesifik untuk dianalisis.

Grafik tambahan:
`AI/Stair Search Shaping Active`, `AI/Stair Search Shaping Reward`,
`AI/Stair Search Initial Distance`, `AI/Stair Search Final Distance`,
dan `AI/Step Limit`. Grafik sukses naik/turun, coverage, revisit, dan timeout
yang lama tetap tersedia.

## Membuka coverage setelah evaluasi

Hentikan trainer dengan Ctrl+C agar checkpoint terakhir diekspor, lalu Stop
Play Mode. Evaluasi ONNX yang sama menggunakan alat batch Unity pada salinan
proyek. Nama asset evaluasi harus diawali `Phase2M4_`, misalnya
`Phase2M4_500000.onnx`. Evaluator menerima daftar model melalui
`-aiEvaluationModels` dan mencatat SHA-256 model dalam laporan JSON.

Kriteria gerbang:

- Minimal 50 episode lengkap pada lima seed tetap, AI/Lesson 3.
- Minimal 30 percobaan naik dan 10 turun; sukses masing-masing minimal 75%.
- Minimal sepuluh spawn dekat dan sepuluh spawn jauh.
- Shaping dimatikan, tidak ada error simulasi, protocol/seeds cocok.
- Hash model evaluasi harus cocok dengan `results/Phase2M4/Explorer.onnx`
  yang sedang menjadi checkpoint final run. Laporan model lama tidak berlaku
  jika training sudah dilanjutkan dan bobot berubah.

Setelah laporan ada:

```powershell
uv run python Assets/Scripts/AI/Evaluation/check_stair_gate.py --report "PATH_LAPORAN.json" --run-id Phase2M4
```

Jika gagal, alat hanya menampilkan alasan; tidak membuat konfigurasi coverage.
Jika lolos, alat membuat `Assets/Scripts/AI/TrainingConfigs/Phase2M4_Coverage_Resume.yaml`
beserta bukti `.gate.json`. Jalankan setelah Stop Play Mode:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2.py Assets/Scripts/AI/TrainingConfigs/Phase2M4_Coverage_Resume.yaml --run-id=Phase2M4 --resume
```

Konfigurasi hasil gate tidak memiliki init_path. Curriculum memakai key baru
`floor_lesson` sehingga dimulai di target 10% setiap lantai (AI/Lesson 4),
kemudian 20% (AI/Lesson 5), tanpa salah membaca index lesson pencarian.
Shaping mati; traversal dan selisih coverage maksimal 10 poin persentase
tetap disyaratkan. Budget menjadi minimal 4 juta step lokal / tambahan
minimal 2 juta setelah checkpoint yang disetujui. Pindah dari coverage 10%
ke 20% mensyaratkan >=75% episode sukses dalam 100 episode terakhir. Sukses
coverage berarti memenuhi target kedua lantai, traversal, dan keseimbangannya;
window pencarian tangga tidak dibawa sebagai bukti mastery coverage. Evaluasi akhir sebelum
Phase 3 tetap harus memeriksa coverage dan traversal aktual.

## Pemeriksaan implementasi

`test_stair_gate.py` memeriksa penolakan hasil tidak lengkap/tidak seimbang,
model stale, protocol tidak cocok, dan konversi konfigurasi coverage.
`AIStairSearchChecks.Validate` memeriksa identitas discounted potential,
penutupan terminal pada sukses/gagal, tidak ada bonus untuk diam, dan reset.
Evaluator Unity menjalankan pemeriksaan matematika itu sebelum simulasi.
