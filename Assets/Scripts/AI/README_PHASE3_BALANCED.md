# Phase3R — percobaan reward seimbang, tanpa input baru

**Riwayat revision 1.** Scene aktif sekarang memakai revision 2 (anggaran
eksplorasi per lantai); lihat [README_PHASE3_SPLIT.md](README_PHASE3_SPLIT.md).
Phase3R dan YAML Balanced dipertahankan, bukan di-overwrite. Jangan resume YAML
ini di scene revision 2; untuk replay historis pilih revision 1 secara eksplisit.

## Status pemeriksaan 27 Oktober 2026

Run **selesai pada 2.000.063 step**; jangan menjalankan command baru di bawah
untuk menimpa hasil, atau resume sebelum budget tambahan disetujui. Window
mastery 100 episode terakhir: 37% sukses, masih level 8 (target 10% dua lantai).
Uji inference model terakhir: 50 episode pada lima seed pengembangan,
22 sukses, 45 mencapai lantai lain. Laporan dan catatan tiap episode ada di
`results/Phase3R/exploration_timing_20261027/HASIL_TIMING.md` dan `PER_EPISODE.csv`.
Ini bukan rekonstruksi episode training lama atau uji akhir tesis independen.

Telemetri baru `AIEpisodeResult.floorTiming` mencatat step pertama kedatangan
fisik, waktu grounded di tiap bidang lantai, waktu transit/tidak pasti, snapshot
coverage pada kedatangan dan kenaikan sampai akhir episode. TensorBoard
memperoleh tag `AI/First Physical Floor Transfer Step`, `AI/Seconds On Floor 0/1`,
dan `AI/Floor 0/1 Coverage Gain After Transfer` untuk episode berikutnya.
Kedatangan fisik diukur pada permukaan <=10 derajat dalam 0,35 unit dari bidang
lantai, bukan tag Stair/trigger. Waktu memakai physics step x fixedDeltaTime,
bukan wall-clock 40x. Ini pencatatan saja: tidak menambah input, reward, planner,
atau tindakan otomatis; tidak perlu mengulang training akibat telemetri ini.

27 Oktober 2026. Tidak mengulang Phase 1. Mulai dari bobot Phase3 **6.000.091**,
177 input yang sama, LSTM 128, Max Step 12.000, speed 6, time scale 40×.
Gamma, learning rate, curiosity, arsitektur, sensor, curriculum mastery 10% → 20%,
dan semua penggerak PPO tetap. Dua perubahan diuji sebagai satu paket; tidak
mengklaim kontribusi kausal masing-masing tanpa ablation terpisah.
Ukuran/urutan input tidak berubah, tetapi nilai observasi waktu tanpa progres
kini dinormalisasi dengan timeout 25 detik, bukan 10. Karena distribusinya berubah,
kompatibel memuat bobot tidak berarti model lama langsung menguasai aturan baru.

## Profil baru (Phase3 Reward Revision = 1)

- Timeout tanpa progres dua lantai: **25 detik simulasi**, bukan 10. Grace
  backtracking yang sudah ada tetap terbatas total 30 detik, bukan unlimited.
  Penalti timeout dua lantai -0,35; anti-spin dua lantai efektif minimal 18 detik.
- Bonus khusus area lantai atas **tidak dipakai**. Kunjungan area baru di kedua
  lantai mendapat perlakuan simetris berdasarkan persentase, bukan jumlah tile.
- Reward eksplorasi umum: total maksimal **0,25**, dinormalisasi terhadap target
  lesson. Menjelajahi satu lantai terus-menerus tidak dapat memperbesar budget.
- Reward minimum coverage: total maksimal **1**, bertambah ketika lantai yang
  tertinggal mengejar. Budget penuh hanya saat target kedua lantai DAN batas
  selisih terpenuhi; contoh target 10%: 30%/10% belum penuh, 30%/20% penuh.
- Bonus sukses tetap **+1** dengan syarat traversal dan coverage yang sama.
  Milestone tangga lama tetap; kunjungan ulang sendiri tidak memperoleh reward.

Progress reward dihitung sebagai kenaikan terhadap reward terbaik yang sudah
diberikan. Coverage tidak diberikan sebagai input policy; ini hanya reward dari
environment saat training. Tidak ada action override, planner, atau rute oracle.
Ini progress shaping, bukan klaim potential-based shaping yang menjamin policy
optimal tidak berubah.

Formula target-normalized (L/U = coverage setiap lantai, T = target, D = batas selisih):

```
ExplorationTotal = 0.25 × clamp01(((L + U) / 2) / T)
BalanceTotal     = min(clamp01(min(L,U) / T), clamp01((min(L,U) + D) / max(L,U)))
reward          = max(0, Total - reward_yang_sudah_diberikan)
```

Pada L=U=0, BalanceTotal=0. Reward lama pada Inspector dipertahankan untuk
profil 0/Phase 1/2; profil 1 memakai budget rumus di atas, bukan bonus upper-floor
legacy yang masih terlihat. Telemetri: `AI/Phase3 Reward Revision`,
`AI/Exploration Reward Awarded`, `AI/Upper Floor Bonus Awarded` (harus 0),
`AI/Minimum Coverage Reward Awarded`, `AI/No New Area Timeout Limit`.

## Menjaga pembanding

Phase3 lama tidak di-overwrite. PT/ONNX dan status 6.000.091 disalin ke
`results/RecoveryBackups/Phase3_before_balanced_6000091/` dan hash PT diverifikasi.
Profil 0 mereproduksi aturan lama; perintah evaluasi historis secara eksplisit
memilih profil 0. Tidak boleh membandingkan reward numerik sebelum/sesudah
perubahan sebagai peningkatan. Gunakan success, coverage, traversal dan seed.

YAML baru memakai `phase3_reward_revision: 1`; driver menolak resume Phase3
legacy dengan profil baru. Gunakan run **Phase3R** agar window mastery dan
TensorBoard tidak mencampur reward lama/baru. Counter run baru mulai 0 tetapi
bobot tetap hasil 6 juta, bukan random. Hasil sebelumnya bukan dihapus.

## Status dan command

Budget disetujui **2 juta step fine-tuning** pada 27 Oktober. Counter Phase3R
mulai 0 dan berakhir sekitar 2 juta, dengan bobot awal dari Phase3 6.000.091.
Ini bukan 2 juta pada counter sumber yang sudah 6 juta. Driver tetap menolak
budget 0 jika suatu konfigurasi belum disetujui. Pemeriksaan kompatibilitas saja:

```bat
uv run python Assets/Scripts/AI/Training/train_phase3.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Balanced.yaml --run-id=Phase3R --check-only
```

Buka ulang scene
`Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity`, pastikan Phase3 Reward Revision
1 dan Behavior Type Default. Command training baru:

```bat
uv run python Assets/Scripts/AI/Training/train_phase3.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Balanced.yaml --run-id=Phase3R
```

Lalu Play di Unity. Untuk melanjutkan run Phase3R yang sudah ada, tambahkan
`--resume`. Jangan memakai YAML Phase3 legacy atau --force pada scene profil 1.
Tidak ada training yang dimulai otomatis oleh perubahan ini. Peningkatan
perilaku belum dibuktikan: perlu fine-tuning dan evaluasi paired kedua model
pada profil/timeout evaluasi yang SAMA. Seed uji akhir tesis harus terpisah dari
seed pengembangan/pemilihan model.
