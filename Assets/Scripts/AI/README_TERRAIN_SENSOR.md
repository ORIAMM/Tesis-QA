# Sensor geometri lokal: seri training baru Phase1T

20 Oktober 2026. Rekomendasi: mulai Phase 1 dari bobot acak, bukan resume
Phase1M/Phase2M4/Phase2M5. Input baru berbeda bentuk. Semua hasil lama tetap
disimpan; jangan gunakan --force atau menimpa ID lama.

## Cara mulai

1. Tunggu Unity selesai compile, lalu buka Assets/Scenes/AI/AI_Phase1_Movement.unity.
2. Explorer Agent > Exploration Agent > Observation: Use Terrain Profile Sensor ON.
   Sudah diaktifkan di ketiga scene utama; tidak perlu rebuild scene.
3. Behavior Parameters: Behavior Name Explorer, Behavior Type Default, Model kosong.
   Vector Observation Size tetap **10**. Sensor terrain terpisah, jangan ubah menjadi 34/117.
4. Dari root proyek, jalankan:

```powershell
uv run mlagents-learn Assets/Scripts/AI/TrainingConfigs/Explorer_Phase1_Terrain.yaml --run-id=Phase1T
```

Tekan Play di Unity setelah trainer menunggu koneksi. Budget 1 juta step,
time scale 30x, MaxStep episode tidak diubah (1500). Curriculum tetap dua
lesson: koridor lurus menuju target, lalu koridor bertangga menuju target.
PPO, LSTM, curiosity, reward dan collider tetap sama; tidak ada bonus mendeteksi ramp.

Untuk berhenti gunakan Ctrl+C dan tunggu checkpoint/export selesai. Lanjutkan
hanya run baru ini dengan:

```powershell
uv run mlagents-learn Assets/Scripts/AI/TrainingConfigs/Explorer_Phase1_Terrain.yaml --run-id=Phase1T --resume
```

Tidak ada training yang dimulai otomatis oleh perubahan script ini.
Setelah Phase1T selesai, evaluasi keberhasilan naik DAN turun sebelum Phase 2.
Bobot Phase 2 berikutnya harus dari checkpoint **Phase1T**, bukan Phase1M.
YAML Phase2 lama masih menunjuk baseline lama: jangan memakainya untuk seri
terrain. Konfigurasi Phase2T sekarang tersedia setelah Phase1T selesai:
lihat [README_PHASE2T.md](README_PHASE2T.md) untuk curriculum halus dan command.
Phase3T tetap menunggu evaluasi coverage Phase2T.

## Isi observasi

ForwardTerrainSensorComponent mengirim 24 nilai: 6 probe x 4 fitur.
Urutan probe: dekat kiri/tengah/kanan, lalu jauh kiri/tengah/kanan.
Sudut -30/0/+30 derajat terhadap yaw agent; jarak horizontal 1,5 dan 3 unit.
Setiap probe membaca collider solid terdekat secara vertikal dalam rentang
2 unit di atas sampai 2 unit di bawah kaki, dan mensyaratkan garis pandang
dari posisi mata (1,6 unit di atas kaki) ke titik permukaan tidak terhalang.

Empat fitur per probe:

- Valid/terlihat (0 atau 1). Jika tidak valid, tiga fitur lain juga 0.
- Tinggi relatif kaki / 2, dibatasi -1..1.
- Sudut tanjakan menurut arah depan / 90 derajat, -1..1.
- Sudut tanjakan menurut arah kanan / 90 derajat, -1..1.

Tidak valid berarti tidak ada permukaan terukur, terhalang, di luar jangkauan,
atau mengenai sisi bawah/dinding (normal.y < 0,2), BUKAN label pasti lubang.
Ray horizontal lama tetap memberi konteks penghalang. Semua trigger dan layer
Ignore Raycast diabaikan; collider milik agent sendiri dikecualikan.
Tidak ada pembacaan tag Stair, nama prefab, graph WFC, tujuan tangga atau
label anomaly. Probe berputar mengikuti agent. Normal berasal dari Raycast,
bukan SphereCast. Pijakan tangga datar tetap dapat menghasilkan beda tinggi.
Pada prefab dengan smooth ramp, fitur mengikuti collider ramp, bukan mesh visual.

Input baru: 10 vektor dasar + 24 terrain + 34 ray horizontal + 49 memori =
117 nilai (dikirim sebagai beberapa sensor), dibanding 93 sebelumnya.
LSTM dan memori kunjungan tetap ada. Sensor otomatis dipasang sebelum spesifikasi
ML-Agents dibuat; ketiga fase memakai bentuk identik. Pilih agent saat Play
untuk melihat garis probe hijau (valid) / abu-abu (tidak valid) di Scene view.

## Menonton baseline lama

Sebelum Play, matikan Use Terrain Profile Sensor untuk model Phase1M/Phase2M*.
Model terrain baru memerlukan opsi ON. Jangan ganti opsi di tengah Play;
hentikan Play terlebih dahulu agar spesifikasi sensor dibuat ulang.
Menonaktifkan checkbox komponen sensor saja tidak cukup: gunakan opsi agent.
Scene arsip 3A/3B tidak diubah. Evaluator/headless CLI lama secara eksplisit
memakai schema lama; untuk seri terrain gunakan flag -aiTerrainSensor pada
entry Unity terkait. Pipeline otomatis Phase2M5 tetap workflow baseline,
bukan peluncur Phase1T/Phase2T.

## Batasan penelitian dan verifikasi

Sensor membantu keputusan lokal, bukan memberi tahu lokasi tangga yang belum
terlihat dan bukan jaminan coverage meningkat. Dokumentasikan jangkauan,
occlusion, collider ramp dan perbedaan persepsi AI/manusia. Baseline AI yang
dibandingkan harus memakai sensor sama, kecuali untuk ablation sensor.
Pengujian hasil tetap memakai seed/spawn sepadan dan seed final independen.

Tools > AI > Validate Forward Terrain Sensor menguji geometri sintetis:
lantai, ramp naik/turun, void, occlusion dinding, trigger, pijakan datar,
rotasi yaw, bentuk 24 nilai dan opt-out model lama. Tes ini memvalidasi sensor,
bukan membuktikan keberhasilan policy atau lintasan tangga map produksi.
