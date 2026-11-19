# Phase 3 — map besar, 177 input

**Catatan terbaru:** scene kini memakai reward per lantai revision 2;
lihat [README_PHASE3_SPLIT.md](README_PHASE3_SPLIT.md). Budget Phase3S: 1 juta step baru.
Revision 1 tetap tersedia untuk replay hasil Phase3R.
Instruksi berikut merupakan profil legacy (0). Jangan resume YAML legacy pada
scene revision 2. Riwayat revision 1: [README_PHASE3_BALANCED.md](README_PHASE3_BALANCED.md).

Setup 26 Oktober 2026. Fine-tuning dari **Phase2E 7.000.033**, bukan ulang Phase 1.
Bobot PPO, LSTM, critic, curiosity dan normalisasi tetap kompatibel. Input tetap
`[24,34,49,10,32,12,16]`; tidak menambah sensor atau action assistance.

## Scene dan budget

Buka `Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity`, bukan scene 3A/3B lama.
Map 24×24×2; minimal dua tangga; speed 6; Decision Period 5; Max Step **12.000**
per episode. Terrain, Branch Memory, Distant Terrain, Ramp Memory ON; Long Range
replacement OFF. Behavior Explorer = Default, Model boleh kosong saat training.

Budget awal **4 juta step BARU**, time scale **40×**. Run baru `Phase3` dimulai
dari counter 0 tetapi membawa bobot Phase2E. Ini tidak menambah/mengubah run
Phase2E. Sekitar counter lintas fase 11 juta saat selesai, bukan target 4 juta
pada counter sumber 7 juta. Budget awal tersebut telah selesai di **4.000.073**.
Pada 27 Oktober, Max Step episode diperbesar dari 7.500 ke **12.000** setelah
uji paired 20 episode menunjukkan sukses 10% → 45%. Tetap 177 input dan 40×;
tidak perlu mengulang Phase 1. Pengguna menyetujui budget TOTAL **6 juta step**
pada 27 Oktober: sekitar **2 juta tambahan** dari checkpoint 4.000.073. Gunakan
`--resume` pada run yang sama; jangan initialize ulang dari Phase2E.

## Curriculum berdasarkan penguasaan

Mulai langsung dua lantai karena sumber sudah dilatih dua lantai, tetapi target
map besar dilonggarkan: minimum **10% setiap lantai → 20% setiap lantai**.
Selisih coverage maksimal 10 poin persentase PER EPISODE, harus berpindah lantai.
Bukan menyamakan rata-rata coverage antar episode. Tidak memakai target object
sebagai tujuan, sensor tag Stair, forced movement, atau stair-search shaping.

Naik lesson setelah 100 episode utama terakhir: sukses minimal 80% keseluruhan,
minimal 20 sampel spawn dari setiap lantai, sukses minimal 80% pada masing-masing
kelompok spawn. Reward/step tinggi saja tidak menaikkan lesson. Target 20%
memakai 20% rehearsal target 10%; target 10% tidak rehearse transfer dekat tangga.
Rehearsal tidak dihitung dalam mastery. Window mastery disimpan untuk resume.
Level internal 8 = 10%, 9 = 20%; indeks lesson trainer hanya 0 dan 1.

Saat Play tanpa trainer, scene default menampilkan dua lantai target 10%.
Untuk menonton model akhir, sesuaikan Default Terrain Level ke 9 jika ingin
target evaluasi 20%, pasang ONNX Phase3 dan Behavior Type Inference Only.

## Command (Command Prompt, folder root proyek)

Command berikut untuk run BARU saja (riwayat). Folder `Phase3` sudah ada,
jadi jangan memulai ulang tanpa `--resume` atau memakai `--force`.
Periksa tanpa mulai training atau menulis hasil run:

```bat
uv run python Assets/Scripts/AI/Training/train_phase3.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3.yaml --run-id=Phase3 --check-only
```

Mulai run baru, lalu tekan Play di scene Phase 3:

```bat
uv run python Assets/Scripts/AI/Training/train_phase3.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3.yaml --run-id=Phase3
```

Hentikan normal memakai Ctrl+C dan tunggu ekspor selesai. Untuk melanjutkan
checkpoint 4.000.073 menuju TOTAL 6 juta, gunakan command berikut:

```bat
uv run python Assets/Scripts/AI/Training/train_phase3.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3.yaml --run-id=Phase3 --resume
```

Resume memakai checkpoint Phase3 terakhir dan menonaktifkan init_path sehingga
tidak memuat ulang sumber 7 juta. Driver menolak folder lama pada run baru,
checkpoint/status tidak sinkron, input berbeda, --force dan --initialize-from.
Jangan memakai driver Phase2 atau `mlagents-learn` biasa untuk YAML ini.
Model akhir: `results/Phase3/Explorer.onnx`, checkpoint tiap 250.000 step.
Salinan permanen checkpoint sebelum perubahan episode tersedia di
`results/RecoveryBackups/Phase3_before_12000_4000073/` (PT, ONNX, training_status).

Belum ada training tambahan yang dimulai otomatis. Checkpoint terakhir tetap
4.000.073; mastery terakhir 23/100, level 8 (target 10%). Pengujian 12.000 step
bukan bukti menguasai target 20% atau siap uji akhir tesis. Lihat laporan
`results/Phase3/budget_validation_20261027/HASIL_UJI.md`. Perubahan Max Step juga
mengubah observasi StepCount/MaxStep, sehingga perilaku dapat berbeda sejak awal.
