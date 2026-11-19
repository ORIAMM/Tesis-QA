# Phase2M5 headless: satu training, evaluasi, dan model untuk ditonton

Pekerjaan yang diminta pengguna pada 20 Oktober 2026 memakai konfigurasi
Explorer_Phase2_MixedSearch.yaml: bobot awal 15 juta, budget 3 juta step baru,
time scale 40x. Unity berjalan tanpa jendela di salinan .ai-eval, sedangkan
hasil trainer tetap berada di results/Phase2M5 pada proyek utama.

Jangan menjalankan trainer Phase2M5 lain atau membuka .ai-eval sebagai Editor
kedua selama pekerjaan ini berlangsung. Unity utama tetap terpisah.
Port trainer headless 5017, bukan port Editor utama 5004. Tidak ada smoke
timeout 75 detik dan tidak ada pengurangan MaxStep pada training penelitian.

## Status dan hasil

- results/HeadlessRuns/Phase2M5_20261020/pipeline_status.json: tahap pekerjaan.
- Folder yang sama: trainer.out.log, trainer.err.log, unity-training.log,
  unity_status.json, dan log peluncuran evaluasi.
- results/Phase2M5/Explorer: PT/ONNX per checkpoint, interval 250 ribu step,
  keep_checkpoints 20 untuk budget 3 juta.
- results/Phase2M5/headless_evaluation/models: salinan permanen checkpoint
  yang benar-benar diuji, termasuk baseline 15 juta.
- results/Phase2M5/headless_evaluation/HASIL_EVALUASI.md dan summary.json:
  evaluasi per checkpoint dan per seed.
- Assets/Models/AI/Phase2M5: ONNX akhir, ONNX terpilih untuk ditonton, serta
  CARA_LIHAT_MODEL.md. Folder ini dibuat setelah evaluasi berhasil, bukan sebelum.

Pipeline mengevaluasi checkpoint yang tersedia terdekat ke 50%, 90%, dan 100%
dari jumlah step akhir, ditambah baseline Phase2M4 15 juta. Masing-masing 50
episode pada lima seed, spawn/target/yaw/arah/jarak sama. Tidak ada bantuan
arah atau shaping pencarian saat evaluasi. Masuk trigger tangga dan perpindahan
lantai selesai dilaporkan terpisah. Pemilihan ONNX mengutamakan keberhasilan
arah terlemah, bukan coverage besar pada episode gagal yang berjalan lama.

Ini validasi pemilihan checkpoint, bukan uji akhir tesis yang independen atau
persetujuan otomatis Phase 3. Tidak ada penambahan budget otomatis. Jika gagal,
pipeline menyimpan status error dan mempertahankan checkpoint yang sudah tersimpan.
Jangan menghapus AIHeadlessRun.lock saat prosesnya masih hidup.

## Peluncuran dan tindak lanjut

Job diluncurkan tersembunyi oleh asisten melalui run_phase2_headless.py.
Entry trainer train_phase2_headless.py hanya mengatasi port Editor secara lokal
di prosesnya; site-packages tidak diedit. Entry Unity RunHeadlessTraining
memakai scene produksi tanpa menyimpannya. Perubahan reward, sensor, collider,
motor, maupun Phase2M4 tidak menjadi bagian dari pekerjaan ini.

Tindak lanjut di chat dijadwalkan tiap 20 menit untuk memeriksa penyelesaian
atau kegagalan, tanpa update rutin saat kondisi normal. Tetap nyalakan komputer,
jangan Sleep/Shutdown; aplikasi Codex perlu berjalan untuk tindak lanjut chat.

Sebelum job utama, validasi terpisah HeadlessValidation20261020B memverifikasi
trainer -> Unity -> checkpoint akhir -> penutupan otomatis, dengan hasil step
1608. MaxStep tetap produksi; buffer/budget validasi saja diperkecil. Tes
tersebut bukan hasil penelitian. Penghentian Editor setelah native ML-Agents
keluar Play Mode sudah diperbaiki dan diuji ulang.

Catatan pengembangan: 20 unit test mastery/headless dan lima uji gate tersedia.
Pemroses laporan juga diuji pada 50 pasangan episode evaluasi nyata sebelumnya.
