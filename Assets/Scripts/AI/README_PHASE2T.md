# Phase2T — transisi bertahap dari Phase1T

**Update terkini:** scene Phase 2/3 kini memakai 149 input dengan sensor
memori cabang. Untuk run baru gunakan [Phase2C](README_BRANCH_OBSERVATION.md).
Untuk menonton/mereproduksi model Phase2T lama (117 input), nonaktifkan Use
Branch Memory Sensor sebelum Play. Catatan action assistance di bawah diarsipkan;
agent baru tidak memanggil pengalihan aksi.

Update 23 Oktober: scene Phase 2/3 sekarang mengaktifkan bantuan cabang selesai
opsional. Baca [README_BRANCH_MEMORY.md](README_BRANCH_MEMORY.md). Matikan
`Use Completed Branch Assist` sebelum Play untuk mereproduksi PPO murni run
Phase2T lama; checkpoint dan input sensor tidak berubah.

21 Oktober 2026. Run BARU dari checkpoint Phase1T **1.000.005**, bukan resume
Phase1T atau Phase2M*. Sensor tetap 117 nilai; tidak perlu mengulang Phase 1 lagi.
Tidak ada perubahan prefab, collider, layout scene, atau reward dasar baseline.

## Mulai dan resume

1. Tunggu Unity compile, buka `Assets/Scenes/AI/AI_Phase2_SmallWFC.unity`.
2. Explorer Agent: Use Terrain Profile Sensor ON. Behavior Parameters:
   Behavior Name Explorer, Behavior Type Default, Model kosong, Vector Size tetap 10.
3. Tidak perlu rebuild. Map tetap 10 x 10; jumlah lantai diatur trainer.
4. Jalankan dari root proyek, kemudian tekan Play setelah trainer menunggu:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Terrain.yaml --run-id=Phase2T
```

Untuk berhenti: Ctrl+C sekali, tunggu checkpoint/export selesai, lalu stop Play.
Lanjut dengan YAML yang SAMA:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Terrain.yaml --run-id=Phase2T --resume
```

Jangan gunakan `mlagents-learn` langsung, driver Phase2M*, `--force`, atau
`--initialize-from`. Driver khusus mengatur kelulusan berdasarkan episode,
bukan reward rata-rata/jumlah step. Saat resume driver secara eksplisit
menghapus init_path Phase1T dari opsi runtime agar ML-Agents benar-benar
memuat checkpoint Phase2T. Driver juga memeriksa step checkpoint terhadap
training_status.json dan menolak mismatch, tanpa mengubah file apa pun.

Budget total diperpanjang menjadi **8 juta step** pada 23 Oktober 2026,
melanjutkan checkpoint 5.000.073 (tambahan sekitar 3 juta, bukan 8 juta lagi).
Time scale tetap **30x**. MaxStep tetap 4000 pada
satu lantai dan 6000 pada dua lantai (langkah simulasi per episode, bukan
jumlah total step trainer). Budget tidak otomatis diperpanjang jika belum
lulus seluruh level. Evaluasi dahulu sebelum memperpanjang.
Bobot awal dipatok ke `results/Phase1T/Explorer/Explorer-1000005.pt` beserta
ONNX pasangannya. Resume tidak memuat ulang bobot Phase 1 atau mereset level.

## Dua lesson utama, dua belas kenaikan kecil

| Level internal | Bagian | Kondisi utama |
| --- | --- | --- |
| 0 | Lesson 1 | Satu lantai, coverage 20% |
| 1 | Lesson 1 | Satu lantai, coverage 30% |
| 2 | Lesson 1 | Satu lantai, coverage 40% |
| 3 | Lesson 2A | Naik/turun, spawn di tile mulut tangga, yaw ±30° |
| 4 | Lesson 2A | Radius spawn 0–1 tile, yaw ±60° |
| 5 | Lesson 2A | Radius spawn 0–2 tile, yaw ±120° |
| 6 | Lesson 2A | Radius 0–3 tile, yaw acak 360°, peluang band jauh 25% |
| 7 | Lesson 2A | Radius 0–4 tile, yaw acak 360°, peluang band jauh 50% |
| 8 | Lesson 2B | Coverage minimal 10% di SETIAP lantai |
| 9 | Lesson 2B | Coverage minimal 20% di SETIAP lantai |
| 10 | Lesson 2B | Coverage minimal 30% di SETIAP lantai |
| 11 | Lesson 2B | Coverage minimal 40% di SETIAP lantai |

Tile = langkah rute socket WFC, bukan jarak garis lurus. Band jauh adalah
lebih dari 2 langkah dari mulut tangga; band dekat maksimal 2. Jika sebuah
map tidak punya band yang dipilih, spawn memakai kandidat valid yang ada;
kelulusan level 6/7 tetap memerlukan bukti keberhasilan episode band jauh.

Naik/turun pada Lesson 2A bergantian 50:50. Arah awal dibantu oleh penempatan
spawn/yaw sesuai level, tetapi tidak ada waypoint/arah lokasi tangga yang
dikirim ke policy. Sensor terrain, ray horizontal, memori kunjungan dan LSTM
tetap sama. Reward shaping rute tangga yang sudah ada digunakan selama
latihan transfer; itu reward training, bukan observasi posisi tangga.

20% episode mengulang **satu level sebelumnya**, dalam blok 2 dari 10 episode.
Pengulangan tidak dihitung untuk kelulusan. Pengecualian level 0 (belum ada
sebelumnya) dan level 3 (awal dua lantai): tidak berganti ke satu lantai tiap
blok. Di awal Lesson 2B, pengulangan level 7 mempertahankan kemampuan transfer.
Tidak ada pergantian jumlah lantai saat pengulangan level 8 dan berikutnya.
Map diganti tiap 40 episode dan saat jumlah lantai berubah, bukan hanya
karena level coverage/yaw berubah. Spawn/yaw tetap bervariasi.

## Kelulusan dan syarat episode

- Setiap level memerlukan **100 episode utama terakhir**, success minimal 80%.
- Level transfer juga memerlukan masing-masing minimal 20 episode naik dan
  turun, dan success minimal 80% pada MASING-MASING arah.
- Level 6/7 juga memerlukan minimal 10 episode jauh (success 80%) dan minimal
  20 episode dekat (success 80%). Banyak keberhasilan dekat tidak menutupi gagal jauh.
- Level coverage dua lantai memerlukan minimal 20 episode yang mulai dari
  masing-masing lantai, dengan success minimal 80% untuk masing-masing kelompok.
- Level naik satu demi satu. Jendela bukti direset ketika level berubah;
  saat resume, level dan jendela dipulihkan, bukan diulang dari nol.
- Lesson 2A selesai hanya setelah melewati tangga dan transfer lantai terkonfirmasi,
  bukan karena memasuki trigger tangga saja. Lesson 2B TIDAK selesai saat transfer:
  coverage kedua lantai harus mencapai target dan selisihnya maksimal 10 poin
  persentase dalam episode yang sama. Trigger tangga tetap disyaratkan.
- Aturan backtracking/anti-loop tetap: melewati area lama tidak otomatis salah,
  kunjungan ulang tidak memperoleh reward area baru, loop terkonfirmasi dapat
  dikenai penalti. Tangga tidak memperoleh bonus berulang tiap disentuh.

Ini gate penguasaan TRAINING, bukan uji generalisasi independen. Lulus level
11 tidak otomatis berarti siap Phase 3: evaluasi model di seed baru untuk
coverage per lantai, naik/turun selesai, tabrakan, dan pemakaian MaxStep dahulu.

## Melihat perkembangannya

```powershell
uv run tensorboard --logdir results/Phase2T
```

Grafik penting: AI/TerrainMastery/level, episodes, success, up_success,
down_success, near_success, far_success, lower_success, upper_success;
AI/Terrain Rehearsal; AI/Coverage; AI/Floor 0 Coverage; AI/Floor 1 Coverage;
AI/Floor Transitions; AI/Ascent Transitions; AI/Descent Transitions;
AI/Episode Steps; AI/Step Limit; AI/Stationary Failure; AI/No New Area Timeout.
Coverage/reward umum mencampur episode utama dan pengulangan, sehingga
kelulusan dipantau lewat TerrainMastery yang memisahkan episode pengulangan.

Untuk menonton model tanpa Python: Behavior Type Inference Only dan pasang
model **Phase2T**, Use Terrain Profile Sensor ON. Pada WFC adapter aktifkan
Default Terrain Curriculum dan pilih Default Terrain Level (misalnya 11
untuk dua lantai/40%). Tanpa trainer tidak ada pengulangan atau kenaikan level.
Matikan Default Terrain Curriculum saat menonton baseline lama.

## Verifikasi implementasi

Tes Python memeriksa packet episode, pemisahan pengulangan, gate kedua arah,
band jauh, spawn floor, pemulihan jendela, dan penghapusan init_path saat resume.
Preflight Unity di salinan .ai-eval memeriksa generasi/spawn seluruh 12 level,
jumlah lantai, pasangan naik/turun, batas radius, yaw serta waypoint guide OFF.
Ini tes mekanisme/konfigurasi, bukan hasil belajar Phase2T. Training utama
tidak dijalankan otomatis.
