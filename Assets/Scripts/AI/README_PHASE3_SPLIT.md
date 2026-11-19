# Phase3S — reward eksplorasi terpisah per lantai (revision 2)

Hanya saran nomor 1 diterapkan. Scene Phase 3 dan scene builder sekarang memakai
`phase3RewardRevision: 2`. Profile 0/1 tetap ada untuk mereproduksi evaluasi lama.
YAML baru: `TrainingConfigs/Explorer_Phase3_Split.yaml`.

## Perubahan reward

Dengan L/U = coverage bawah/atas dan T = target lesson:

```
exploration = 0.125 * clamp01(L / T) + 0.125 * clamp01(U / T)
reward baru = max(0, exploration - reward eksplorasi yang sudah dibayar)
```

Anggaran maksimum tetap 0,25. Kelebihan coverage di satu lantai tidak mengambil
jatah 0,125 lantai lainnya. Contoh target 10%:

| Coverage L/U | Revision 1 (pooled) | Revision 2 (split) |
|---|---:|---:|
| 20% / 0% | 0,2500 | 0,1250 |
| 20% / 5% | 0,2500 | 0,1875 |
| 10% / 10% | 0,2500 | 0,2500 |

Ini total reward eksplorasi yang sudah layak diperoleh, bukan pembayaran ulang
setiap step. Revisit tidak membayar lagi; kedua lantai simetris. Reward minimum
coverage/keseimbangan tetap maksimal 1 dengan rumus revision 1. Syarat sukses
tetap target tiap lantai, selisih maksimal 10 pp, dan traversal yang diperlukan.
Tidak melonggarkan evaluasi agar angka sukses terlihat naik.

## Yang tidak berubah

- 177 input `[24,34,49,10,32,12,16]`, LSTM 128, ray, memori, motor, speed 6.
- Map 24x24 dua lantai; curriculum mastery 10% lalu 20%; tanpa latihan tambahan
  pasca-tangga, pemaksaan gerak, atau rehearsal baru.
- MaxStep episode 12.000, time scale 40x, timeout/grace/anti-spin/penalti.
- Reward masuk trigger tangga pertama +0,08; midpoint +0,08;
  milestone lantai lain pertama +0,2; reward sukses dan balance tidak diubah.
- PPO learning rate 1e-4, gamma 0,99, curiosity 0,002, time_horizon 128.

Reward area dan balance bisa diberikan pada kunjungan yang sama: itu kombinasi
dua tujuan dengan anggaran terpisah, bukan bug atau penghapusan kemampuan motor.

## Sumber bobot dan keamanan run

Bobot awal: **Phase3R 2.000.063**, bukan random atau ulang Phase 1.
Salinan PT dan ONNX permanen ada di
`results/RecoveryBackups/Phase3R_before_split_2000063/`.
SHA256 PT: `cc47d3f9651666b5482ef89ff2209472d554193315a5dcb847d50cba9f30c5fb`.
SHA256 ONNX: `a6a6410d44a5bffa810553e1f0baa3b9fe4b699a2c8d73c32eb00c6c32d7a9a2`.

Run baru yang disarankan: **Phase3S**. Counter baru dimulai 0, tetapi bobot PPO,
critic, curiosity, normalisasi dan LSTM dimuat dari sumber terverifikasi tersebut.
Driver memverifikasi bentuk semua modul dan menolak resume Phase3R/Phase3 dengan
revision berbeda. Jangan `--force` atau overwrite baseline.

**Budget disetujui: 1 juta step BARU untuk Phase3S.** `max_steps: 1000000`.
Training belum dimulai otomatis. Counter Phase3S mulai 0 dan berhenti sekitar
1 juta, membawa bobot sumber Phase3R 2.000.063; bukan target counter 3 juta dan
bukan resume Phase3R. Pemeriksaan kompatibilitas saja:

```bat
uv run python Assets/Scripts/AI/Training/train_phase3.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Split.yaml --run-id=Phase3S --check-only
```

Untuk memulai training dari Command Prompt di folder proyek:

```bat
uv run python Assets/Scripts/AI/Training/train_phase3.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Split.yaml --run-id=Phase3S
```

Lalu Play di Unity. Untuk melanjutkan Phase3S yang sudah ada, tambahkan
`--resume` ke command yang sama; jangan `--force`. Buka ulang scene
`Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity` agar nilai revision 2 dari disk
terbaca; jangan rebuild scene kecuali memang diperlukan. Jika scene masih punya
perubahan Inspector yang belum disimpan, jangan menimpanya: cocokkan field
Phase3 Reward Revision ke 2. Gunakan Behavior Type Default saat training.

## Risiko kemampuan tangga menurun

Mengubah reward tidak langsung mengubah model ONNX lama atau menghapus bobot
kemampuan tangga. Namun fine-tuning memperbarui jaringan policy yang sama,
sehingga kemampuan lama tetap dapat mengalami regresi/catastrophic forgetting.
Menjaga reward tangga dan training dua lantai membantu mempertahankan dorongan
untuk traversal, tetapi BUKAN jaminan kemampuan itu tidak menurun.

Gunakan pemilihan checkpoint berdasarkan gabungan coverage DAN traversal:

- Baseline Phase3R: 45/50 mencapai lantai lain, naik 21/22, turun 24/28 pada
  lima seed pengembangan; 22/50 memenuhi coverage seimbang.
- Saat membandingkan model baru, uji ulang sumber dan kandidat dengan profile
  revision 2 yang SAMA, seed/spawn/yaw sama, MaxStep 12.000 dan jumlah episode
  sama. Baseline revision 1 lama hanya konteks, bukan perbandingan kausal langsung.
- Periksa naik dan turun secara terpisah, first physical transfer step, waktu
  di lantai baru, dan gain coverage setelah transfer; jangan pilih dari reward
  total saja. Penurunan kecil pada 50 episode belum tentu regresi yang pasti.
- Jika traversal memburuk konsisten, jangan otomatis memakai checkpoint paling
  akhir. Pertahankan model pembanding; rehearsal tangga atau perubahan learning
  rate memerlukan eksperimen/izin terpisah, tidak diterapkan diam-diam di sini.

Cadangan melindungi hasil lama dari kehilangan data, bukan mencegah lupa dalam
model baru. Belum ada klaim peningkatan perilaku tanpa fine-tuning dan evaluasi.
