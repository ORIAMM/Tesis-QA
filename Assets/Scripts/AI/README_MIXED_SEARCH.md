# Phase2M5: peralihan pencarian tangga dekat ke jauh

Update: pengguna meminta pelaksanaan headless dan evaluasi otomatis. Lihat
[README_HEADLESS.md](README_HEADLESS.md); jangan menjalankan command manual
di bawah bersamaan dengan job headless Phase2M5 yang masih aktif.

Update 20 Oktober 2026. Ini eksperimen terpisah dari Phase2M4, bukan mengulang
Phase 1. Bobot awal berasal dari checkpoint Phase2M4 15.000.072 yang dipin di
results/Phase2_initialization/Phase2M4-15000072.pt. Run Phase2M4 tidak diubah.

## Hasil pemeriksaan lintasan mandiri

Model 15 juta diuji ulang pada 50 episode fixed-seed tanpa training/bantuan.
Trace posisi/yaw/grounding/jarak rute dicatat tiap 10 physics step. Semua spawn
dan 50 hasil berhasil/gagal identik dengan evaluasi sebelum penambahan trace.

- 16 episode berhasil berpindah lantai.
- 24 gagal tanpa mencapai tile akses tangga yang dipilih.
- 8 mencapai tile akses tetapi tidak masuk trigger tangga.
- 2 masuk trigger tangga tetapi tidak menyelesaikan transfer lantai.

Artinya 32 dari 34 kegagalan terjadi sebelum masuk tangga. Ini mendukung
perbaikan pencarian/pendekatan, bukan perubahan collider secara spekulatif.
Tile akses adalah sel grid di depan tangga, bukan bukti bahwa agent sudah
menghadap bukaan. Trace adalah alat diagnosis saja, tidak menjadi observasi AI.
Data: results/AIValidation/trajectory_15000072_20261020/Phase2M4_15000072.json.

## Yang diubah

Semua level berikut berada di MAIN LESSON 3 dalam Phase 2, bukan Phase 3.

| Level lokal | Maksimum jarak rute | Porsi dekat (0-2 tile) / jauh (>2 tile) |
| --- | --- | --- |
| 0 | 2 tile | 100% / 0% |
| 1 | 3 tile | 80% / 20% |
| 2 | 3 tile | 50% / 50% |
| 3 | 4 tile | 80% / 20% |
| 4 | 4 tile | 50% / 50%, sama dengan kesulitan pencarian lama |

Rasio adalah peluang sampling episode pencarian, jika band tersedia pada map.
Statistik menilai jarak spawn aktual, bukan menganggap peluang sebagai hitungan.
Komposisi naik/turun 75%/25% dan episode pengingat terpandu 20% tetap seperti
sebelumnya; pengingat tidak dihitung untuk mastery pencarian.

Setiap level memerlukan window 100 episode tanpa bantuan, minimal 20 percobaan
per arah dan keberhasilan naik SERTA turun >=75%. Near juga memerlukan minimal
20 percobaan dan >=75%. Mulai level 1, far harus memiliki minimal 10 percobaan
dan >=75%, supaya contoh dekat tidak menutupi kegagalan pencarian jauh.
Saat naik level, hanya window penilaian yang dikosongkan; bobot tidak direset.
Level 4 tidak otomatis masuk coverage: gerbang evaluasi fixed-seed tetap wajib.

Seed awal latihan baru 173003; lima seed evaluasi 913701, 921620, 929539,
937458, 945377 secara eksplisit dikecualikan dari generator saat fitur baru aktif.
Evaluasi tetap memakai protokol lama yang sama, tanpa fitur mixed-search ini.

Yang TIDAK diubah: reward/penalti, curiosity, learning rate, MaxStep 4000/6000,
motor, collider, prefab, offset, bentuk/jumlah observasi dan aksi, maupun tag.
Fitur baru opt-in lewat YAML; konfigurasi Phase2M4 mempertahankan perilaku lama.

## Mulai run baru

Stop trainer lama dan Stop Play Unity, lalu tunggu kompilasi selesai. Dari root proyek:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_MixedSearch.yaml --run-id=Phase2M5
```

Buka AI_Phase2_SmallWFC, Behavior Type Default, Model kosong, lalu Play setelah
terminal menunggu koneksi. Tidak perlu rebuild scene atau mengatur Inspector.
Jangan --resume pada pemakaian pertama, jangan --force, dan jangan gunakan ID Phase2M4.

Budget 3 juta STEP BARU pada run Phase2M5, time scale 40x. Penghitung run ini
mulai 0, tetapi bobotnya dari 15 juta, bukan acak. Optimizer/critic/curiosity
yang tersedia ikut dimuat oleh init_path ML-Agents; hanya global step dan
window curriculum run baru yang dimulai ulang.
keep_checkpoints=20 mencukupi checkpoint per 250 ribu selama budget 3 juta.
Snapshot baseline dan model evaluasi disimpan terpisah dari rotasi trainer.

## Resume Phase2M5 setelah dihentikan

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_MixedSearch_Resume.yaml --run-id=Phase2M5 --resume
```

YAML resume sengaja tidak memiliki init_path, sehingga tidak kembali ke bobot
awal 15 juta. Level dan window phase2_mixed_search dipulihkan. Wrapper menolak
resume run lama tanpa metadata mixed-search agar tidak diam-diam mengganti curriculum.

## Pantau dan evaluasi

- AI/Mastery/lesson: tetap 3 selama pencarian.
- AI/MixedSearch/level: level lokal 0-4.
- AI/MixedSearch/up_success dan down_success: keberhasilan masing-masing arah.
- AI/MixedSearch/near_success, far_success dan jumlah trials: penguasaan setiap band.
- AI/MixedSearch/mastered: 1 hanya jika level terakhir memenuhi syarat.
- AI/Mixed Search Outcome: kode transport episode, bukan reward.

Bandingkan checkpoint yang dipin dengan evaluator RunComparison yang sama:
50 episode, lima seed, spawn/target/yaw identik, inference seed 58231. Jangan
menganggap mean reward training sebagai pengganti evaluasi ini. Keberhasilan
transfer dan jumlah perpindahan lantai harus dibedakan dari jumlah masuk trigger.
Coverage pada uji transfer bukan evaluasi eksplorasi panjang karena episode
berhasil berakhir setelah mencapai lantai tujuan. Belum ada klaim performa
model membaik sebelum run baru dilatih dan diuji.

Training utama tidak dijalankan otomatis; smoke test terisolasi bukan hasil
pelatihan penelitian dan tidak memakai ID Phase2M5.
