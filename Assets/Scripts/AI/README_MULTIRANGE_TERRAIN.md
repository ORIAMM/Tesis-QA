# Phase2D — terrain dekat, menengah, jauh bersamaan

**Update 26 Oktober:** Phase2D selesai 5 juta dan tetap disimpan. Scene Phase 2/3
kini menambahkan Ramp Memory 16 nilai (177 total), sehingga untuk model/command
Phase2D 161 input harus mematikan Ramp Memory sebelum Play. Untuk fitur baru
lihat [README_RAMP_MEMORY.md](README_RAMP_MEMORY.md); instruksi di bawah adalah
riwayat seri 161 input.

25 Oktober 2026: menggantikan percobaan Phase2C yang memindahkan probe 3 m
menjadi 6 m. Ketiga jarak sekarang aktif bersama, tanpa menghilangkan input lama.

## Apa yang dipasang

| Pengamatan | Jarak horizontal | Tinggi pencarian | Nilai |
|---|---|---|---:|
| Terrain lama dekat | 1,5 m, kiri/tengah/kanan | +/-2 m | 12 |
| Terrain lama menengah | 3 m, kiri/tengah/kanan | +/-2 m | 12 |
| Terrain tambahan jauh | 6 m, kiri/tengah/kanan | +/-6 m | 12 |

Sudut -30/0/+30 derajat relatif ke depan agent. Masing-masing titik memberikan
flag permukaan terlihat, tinggi relatif, slope depan, dan slope samping.
Tinggi input lama tetap `/2`; tinggi input baru `/6`. Nilai tetap -1..1.

Ini sembilan titik sampel permukaan, bukan penglihatan seluruh gambar/jaminan
melihat setiap ramp dalam radius 6 m. Penghalang tetap memutus line-of-sight.
Di kolom bertingkat, hanya permukaan yang benar-benar terlihat dari posisi mata
yang boleh terukur. Void/occluded bernilai 0 (unknown), bukan label buntu.
Tidak memakai tag Stair, posisi tangga oracle, graph WFC, atau mengganti aksi PPO.
Ray 360 derajat untuk penghalang dan memori kunjungan/cabang tetap ada.

Urutan input **161**:

```
obs_0 24  terrain lama 1,5/3 m
obs_1 34  ray penghalang
obs_2 49  memori kunjungan
obs_3 10  vektor dasar
obs_4 32  memori cabang
obs_5 12  terrain jauh 6 m
```

`ZZ Explorer Distant Terrain v1` sengaja diurutkan setelah semua sensor lama.
Base Vector Observation Size tetap **10**, bukan 161. Phase 1 tetap memakai
117 input/terrain 1,5/3 m; scene Phase 2 dan Phase 3 memakai 161 input.

## Bobot awal, counter dan budget

**Tidak mengulang Phase 1 dan tidak menimpa Phase2C.** Run terpisah **Phase2D**
SUDAH disiapkan oleh `Training/prepare_multirange_terrain.py`:

- Sumber permanen sebelum perubahan: `results/RecoveryBackups/Phase2C_before_long_range_2000052/Explorer-2000052.pt`.
  Sumber ini belajar dengan 1,5/3 m, bukan model 2,78 juta dari percobaan 1,5/6 m.
- SHA256 sumber dipatok dalam script; tujuan yang sudah ada ditolak, tanpa overwrite.
- Actor/critic/curiosity/LSTM dan normalizer lama disalin. Tiga matriks input
  256x149 menjadi 256x161, dengan 12 kolom baru **nol**. Normalizer baru segar.
- Optimizer **direset secara eksplisit** untuk arsitektur yang diperluas, bukan
  diklaim sebagai resume optimizer lama identik. Counter dipertahankan
  **2.000.052**, tujuan **5.000.000 total**, tersisa **2.999.948** step baru.
- Curriculum tetap level **8**, reward/speed/MaxStep/time scale tidak berubah.
  Window kelulusan dimulai dari 0/100 agar memakai episode persepsi baru.
- Policy dan value encoding/LSTM diuji dengan batch 1/4/16 dan input baru nonnol;
  error maksimum sekitar **3,46e-6**. Input baru mulai diabaikan, kemudian dapat
  dipelajari (uji gradien/update lolos). Ini bukan bukti performa eksplorasi.
- Laporan: `results/Phase2D/migration.json`. PT dan ONNX 161 input disediakan
  di `results/Phase2D/Explorer`, plus ONNX utama di `results/Phase2D/Explorer.onnx`.
- Training belum dijalankan otomatis. `--resume` dipakai karena bundle migrasi
  SUDAH memiliki checkpoint dan metadata pada counter sumber, bukan karena
  pernah ada 2 juta step training baru pada Phase2D.

## Cara melanjutkan

1. Stop Play/trainer sebelumnya. Tunggu Unity compile, lalu buka ulang
   `Assets/Scenes/AI/AI_Phase2_SmallWFC.unity`. Tidak perlu rebuild.
2. Explorer Agent: **Use Terrain Profile Sensor ON**, **Use Branch Memory Sensor
   ON**, **Use Distant Terrain Sensor ON**, **Use Long Range Terrain Profile OFF**.
   Opsi terakhir adalah mode penggantian 3->6 m yang lama, bukan sensor tambahan.
3. Behavior Name `Explorer`, Type **Default**, Model kosong, Vector Size **10**.
4. Di root proyek, jalankan lalu tekan Play ketika menunggu koneksi:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_MultiRange.yaml --run-id=Phase2D --resume
```

Pesan awal harus menyebut **2000052 steps, terrain level 8**. Command yang sama
dipakai setelah Ctrl+C selesai menyimpan. Jangan `--force`, jangan run-ID
Phase2C, jangan `--initialize-from`, dan jangan menjalankan tool migrasi lagi.
YAML lama tanpa `terrain_distant_observation: 1` tidak cocok dengan scene baru;
guard scene/trainer akan menghentikannya, bukan melatih dengan input salah.

Time scale tetap **30x**. TensorBoard:

```powershell
uv run tensorboard --logdir results/Phase2D
```

`AI/Terrain Far Distance` sekarang berarti jarak terjauh profil LAMA = **3**.
`AI/Terrain Distant Distance` harus **6**, `AI/Distant Terrain Active` harus **1**.
Nilai `AI/Branch Assist Active` harus tetap **0**.

## Menonton model / perbandingan

| Model | Branch memory | Distant Terrain | Long Range replacement |
|---|---|---|---|
| Baseline Phase2C 2.000.052 (149) | ON | OFF | OFF |
| Percobaan Phase2C setelah perubahan 1,5/6 m (149) | ON | OFF | ON |
| Phase2D hasil migrasi/fine-tuning (161) | ON | ON | OFF |

Terrain Profile tetap ON untuk ketiganya. Cocokkan opsi SEBELUM Play. Untuk
menonton, masukkan ONNX yang tepat ke Behavior Parameters, pilih Inference Only,
dan gunakan time scale 1x agar gerak dapat dilihat. ONNX migrasi awal belum
belajar sensor jauh; jangan menyebutnya model hasil training baru.

Scene Phase 3 mengikuti persepsi 161 yang sama, tetapi training Phase 3 baru
boleh disiapkan dengan checkpoint/config kompatibel saat Phase 2 sudah dievaluasi;
jangan langsung memakai YAML/ONNX Phase 3 lama yang berbeda schema.

Pantau mastery (100 episode utama), coverage minimum/difference per episode,
perpindahan naik/turun, collision dan timeout. Mean coverage kedua lantai yang
mirip tidak membuktikan coverage seimbang di tiap episode. Gunakan seed/spawn
yang sama untuk memilih model, dan seed berbeda untuk evaluasi akhir tesis.

## Pemeriksaan

- 44 unit test Python: preservasi bobot, gradien input baru, schema ONNX,
  metadata/counter resume, penolakan overwrite dan run lama, konfigurasi reward
  identik. Fake CLI dalam test tidak menjalankan training.
- Test Unity mencakup tiga jarak bersamaan, 24 nilai lama tidak berubah,
  urutan sensor 161, wall/trigger, lantai bertumpuk, ramp naik/turun, yaw dan void.
  Batch Unity exit **0**; replay geometri WFC juga lolos pada seed 283003,
  314159, 92017. Replay posisi ini bukan evaluasi performa policy/training.
  Lihat `Logs/terrain-multirange-checks.log` untuk hasil batch terakhir.

Untuk mereproduksi pada tujuan BARU (bukan sebelum tiap training):

```powershell
uv run python Assets/Scripts/AI/Training/prepare_multirange_terrain.py --destination results/NamaRunBaru
```

Script tidak memulai training atau mengubah run sumber. Jika migrasi gagal
sesudah membuat tujuan, jangan lanjut training dari bundle setengah jadi;
periksa log/diagnostik atau pilih tujuan baru.
