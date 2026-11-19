# Lesson 2 bertahap, 18 Oktober 2026

Update 20 Oktober: eksperimen terpisah Phase2M5 memakai bridge pada lesson 3.
Lihat [README_MIXED_SEARCH.md](README_MIXED_SEARCH.md). Petunjuk Phase2M4 berikut
adalah baseline yang dipertahankan, bukan command untuk eksperimen baru.

Lanjutkan Phase2M4 dengan checkpoint terakhir, bukan mengulang Phase 1/2.
Update 19 Oktober: budget resume menjadi 15 juta step kumulatif; time scale
tetap 40x. Checkpoint terakhir yang diperiksa adalah 13.000.072,
sehingga tersisa sekitar 2 juta step tambahan, bukan 15 juta step baru.
Reward, optimizer, ukuran observasi/aksi, prefab, collider, curriculum, dan
batas episode tidak diubah oleh penambahan budget/time scale ini.
Kecepatan motor dan MaxStep per episode tetap sama.

Stop trainer (Ctrl+C) dan Stop Play Mode sebelum memuat perubahan. Tunggu Unity
selesai kompilasi, lalu jalankan command resume yang sama:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch_Resume.yaml --run-id=Phase2M4 --resume
```

Buka AI_Phase2_SmallWFC, Behavior Default, Model kosong, lalu Play setelah
terminal menunggu koneksi. Tidak perlu rebuild scene. Jangan --force.

Pada checkpoint tersebut, seluruh level near-search sudah lolos: window akhir
level 3 mencatat naik 57/75 (76%) dan turun 23/25 (92%). Main lesson sekarang 3
(campuran pencarian dekat/jauh), bukan Phase 3. Window 100 episode main lesson 3
terakhir mencatat naik 45/75 (60%) dan turun 19/25 (76%). Lanjutkan latihan ini;
evaluasi tangga fixed-seed tetap diperlukan sebelum balanced coverage.
Episode pengingat terpandu tidak termasuk perhitungan ini.

## Tingkat di dalam main lesson 2

| Level lokal | Jarak maksimum dari tangga | Orientasi awal |
| --- | --- | --- |
| 0 | 0–1 tile | +/-60 derajat |
| 1 | 0–2 tile | +/-60 derajat |
| 2 | 0–2 tile | +/-120 derajat |
| 3 | 0–2 tile | +/-180 derajat, sama dengan acak 360 derajat |

Jarak diperluas dahulu tanpa mengubah yaw, kemudian cakupan yaw diperbesar.
Yaw dihitung dari segmen rute pertama yang terhubung menuju tangga, sehingga
spawn di koridor berbelok tidak diarahkan secara diagonal menembus dinding.
Ini hanya pengaturan orientasi SPAWN: agent tetap bebas berputar 360 derajat
saat bergerak. Tidak ada input petunjuk arah baru selama episode main lesson 2.

Setiap tingkat memerlukan 100 episode terakhir tanpa bantuan, minimal
20 percobaan per arah, dan sukses naik >=75% SERTA turun >=75%. Komposisi
spawn tetap 75% naik / 25% turun. Window dikosongkan setiap tingkat naik.
Baru setelah level 3 lolos, main lesson beralih ke lesson 3 (pencarian campuran
dekat/jauh). Jumlah step/reward tinggi tidak bisa melewati syarat mastery.

Episode pengingat lesson 1 yang berpemandu tetap berjalan seperti sebelumnya,
tetapi dikecualikan dari window near-search dan tidak dapat meloloskan level.
Evaluasi held-out standar tidak memakai pengaturan spawn bertahap ini.

## Resume dan migrasi

Checkpoint yang ditemukan saat implementasi: 5.000.102. Saat pertama resume
dengan near_search_curriculum=1, main lesson 2 tetap dipertahankan, sedangkan
window lama dari task yaw acak penuh dikosongkan dan level lokal mulai dari 0.
Bobot, optimizer, penghitung step, dan kelulusan latihan tangga lesson 1 tidak
direset. Ini bukan restart training.

Level dan window baru tersimpan dalam phase2_near_search di training_status.json.
Resume berikutnya mempertahankan level tersebut, tidak kembali ke level 0.
Jika run sudah mencapai main lesson 3/coverage sebelum migrasi, revisi ini
tidak memundurkannya ke lesson 2.

Cadangan checkpoint/status/konfigurasi sebelum perubahan ada di
results/Phase2M4/before_near_search_20261018. Trainer utama tidak dijalankan otomatis.

## Grafik

- AI/NearSearch/level: tingkat lokal pada trainer, bukan main lesson.
- AI/NearSearch/episodes, up_trials, down_trials, up_success, down_success:
  window mastery untuk tingkat yang sedang dinilai.
- AI/Near Search Yaw Range dan AI/Near Search Max Tiles: pengaturan spawn aktif.
- AI/Main Lesson atau AI/Mastery/lesson: tahap utama, tetap 2 selama level 0–3.
- AI/Near Search Outcome adalah kode pengiriman internal, bukan reward.

Nilai kesuksesan lama dan baru tidak langsung sebanding karena distribusi
spawn berubah. Penguasaan pada level mudah belum membuktikan pencarian dari
orientasi acak penuh; level 3 dan evaluasi tanpa bantuan tetap wajib.
