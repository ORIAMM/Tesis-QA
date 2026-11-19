# Phase2B — pilot fine-tuning dengan bantuan memori cabang

**ARSIP, digantikan Phase2C.** Run Phase2B selesai di 2.000.101 step dan
bobotnya menjadi sumber migrasi sensor cabang. Agent baru tidak lagi memakai
action assistance; jangan resume YAML ini pada scene baru. Baca
[README_BRANCH_OBSERVATION.md](README_BRANCH_OBSERVATION.md).

24 Oktober 2026. **Belum otomatis dijalankan.** Run baru, baseline Phase2T
tidak ditimpa. Checkpoint awal `results/Phase2T/Explorer/Explorer-8000127.pt`.
Sensor tetap 117 nilai; tidak perlu mengulang Phase 1 atau rebuild scene.

## Setup dan mulai

1. Stop Play/trainer lain. Buka scene `Assets/Scenes/AI/AI_Phase2_SmallWFC.unity`.
2. ExplorerAgent: `Use Terrain Profile Sensor` ON,
   `Use Completed Branch Assist` ON.
3. Behavior Parameters: Behavior Name `Explorer`, Behavior Type `Default`,
   Model kosong, Vector Observation Size **10**. Jangan gunakan Inference Only.
4. AI Character Motor: Move Speed **6**, Rotation Speed tetap **180**.
   Scene Phase 2 sudah disesuaikan; pastikan nilai Inspector sama jika scene
   sebelumnya masih terbuka dengan perubahan belum tersimpan.
5. Dari terminal/CMD pada root proyek:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Branch.yaml --run-id=Phase2B
```

Tunggu trainer meminta koneksi, lalu tekan Play di Unity. Ini mode Editor,
bukan launcher headless. Jika command meminta Unity Play, bukan berarti macet.
Tidak perlu mengubah Default Terrain Level: trainer mengirim level awal **8**.
Pastikan console trainer menampilkan `terrain level 8, branch assistance True`.

- Budget **2.000.000 step BARU**; counter Phase2B dimulai dari 0 walaupun
  bobot berasal dari 8.000.127. Itu normal, bukan checkpoint salah atau lupa
  pelatihan sebelumnya. Ini run fine-tuning baru, bukan `--resume` Phase2T.
  ML-Agents memuat modul checkpoint yang cocok (termasuk state optimizer jika
  tersedia), tetapi counter dan jendela kelulusan run baru dimulai ulang.
  Normalisasi/input dan arsitektur kompatibel.
- Time scale **30x**, MaxStep episode tetap 4000/6000.
- Kecepatan akhir **6 unit/detik**, pemindaian memori cabang **0,1 detik
  simulasi** (sebelumnya 0,25). Scene Phase 3 juga memakai speed 6.
  Rotation Speed 180 dan Decision Period 5 tidak diubah. Kecepatan berasal
  dari motor di scene, bukan field `engine_settings` pada YAML.
- Awal level 8: coverage minimal 10% SETIAP lantai, selisih maksimal 10 pp,
  termasuk syarat traversal. Kemudian level 9/10/11 menurut mastery lama.
- Jendela kelulusan dikosongkan pada run baru agar bukti keberhasilan diukur
  ulang setelah bantuan aktif. Tetap ada 20% rehearsal transfer level 7 di
  level 8; bantuan cabang tidak aktif pada episode transfer.
- Tidak ada perubahan reward, penalti, learning rate, sensor, collider,
  generator atau aturan mastery. Dibanding baseline 8 juta, ada perubahan
  assistance DAN speed 4 ke 6. Adaptasi ini perlu diuji pada tangga/belokan.
- Jangan gunakan YAML lama atau `--resume` ketika PERTAMA memulai Phase2B.
  Run yang sudah ada ditolak tanpa overwrite. Hindari `--force`.

## Berhenti dan resume

Ctrl+C sekali, tunggu proses menyimpan/export dan kembali ke prompt, lalu
Stop Play. Resume memakai YAML/ID yang sama:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Branch.yaml --run-id=Phase2B --resume
```

Driver memeriksa step PT terhadap metadata, menghapus init_path dari opsi
runtime saat resume, mempertahankan level/jendela yang disimpan, dan menolak
perubahan mode baseline/hybrid. Resume tidak mengulang dari bobot 8 juta.
Checkpoint yang dipakai adalah checkpoint TERSIMPAN terakhir, bukan jaminan
persis angka di layar sebelum proses berhenti mendadak.

## Pantau sebelum menambah budget

```powershell
uv run tensorboard --logdir results/Phase2B
```

Pada sekitar 100–250 ribu step, periksa dahulu:

- `AI/Branch Assist Active`: 1 pada episode coverage, 0 pada rehearsal transfer.
- `AI/Branch Memory Nodes` dan `AI/Completed Branch Exits`.
- `AI/Branch Redirects Cumulative`: apakah intervensi benar-benar terjadi?
  Ini rata-rata sampling counter berjalan, bukan total akhir per episode.
- `AI/TerrainMastery/success`, lower/upper start success, coverage per lantai,
  selisih coverage, transfer selesai, tabrakan, dan step limit.

Jika intervensi selalu nol, jangan menganggap training lebih lama pasti
memperbaiki fitur: bisa jadi syarat konfirmasi terlalu konservatif. Bila
intervensi ada tetapi coverage/traversal menurun, bantuan perlu diperiksa,
bukan langsung memperpanjang training. Reward meningkat bukan bukti tunggal.
Model baru nanti diuji dengan assistance ON; mematikannya bukan sistem yang
sama. Untuk mengisolasi kontribusi fine-tuning, bandingkan model 8M dan
model Phase2B pada seed/spawn yang sama, sama-sama assistance ON; baseline
PPO murni assistance OFF menjadi kondisi pembanding tambahan.
Samakan pula speed **6** pada kedua kondisi. Jangan menyimpulkan peningkatan
strategi eksplorasi hanya dari coverage yang meningkat akibat kecepatan lebih
tinggi. Time scale saat pengujian manusia/inference perilaku adalah **1x**.

## Posisi Phase2B dan apakah mengulang fase sebelumnya?

Phase2B adalah NAMA RUN fine-tuning (B = Branch), bukan Phase 2 dari awal,
bukan Phase 3B lama, dan bukan pelajaran awal satu lantai. Alurnya:
Phase1T -> Phase2T (checkpoint 8.000.127, level 8) -> Phase2B.

Tidak perlu langsung mengulang Phase 1 maupun seluruh Phase 2: struktur input
tetap dan bobot kemampuan lama digunakan. Run ini mulai di bagian coverage dua
lantai, level 8, lalu level 9..11 jika mastery terpenuhi. Tetap ada rehearsal
transfer level 7 agar kemampuan naik/turun tidak diabaikan. Counter run baru
0 bukan berarti agent belajar tanpa bobot sebelumnya.

Phase 1 yang tersimpan tetap speed 4 sebagai baseline historis, bukan scene
yang dipakai Phase2B. Perubahan speed tidak menjamin kontrol lama langsung
setara: bila hasil fine-tuning menunjukkan regresi naik/turun atau belokan,
evaluasi latihan kontrol/transfer terfokus dahulu, bukan otomatis menghapus
seluruh progress. Belum ada bukti training speed 6 berhasil hanya dari uji
kompilasi/graf.

Secara metodologi ini **hybrid PPO + aturan memori graf**, bukan sensor baru
atau memori cabang yang otomatis tersimpan di dalam bobot policy. Baca
[README_BRANCH_MEMORY.md](README_BRANCH_MEMORY.md) untuk batasannya.
