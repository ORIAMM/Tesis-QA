# ML-Agents: tiga fase

**Terbaru: Phase3S — reward per lantai, revision 2.** Hanya anggaran eksplorasi
dipisah 0,125 per lantai; balance, reward tangga, 177 input, curriculum dan semua
penalti tetap. Fine-tuning disiapkan dari Phase3R 2.000.063 yang dicadangkan.
Budget disetujui **1 juta step baru**; training belum dimulai otomatis. Scene Phase 3 kini
revision 2; gunakan YAML Split, bukan resume Phase3R. Lihat
[README_PHASE3_SPLIT.md](README_PHASE3_SPLIT.md). Bagian di bawah adalah riwayat.

**Percobaan terbaru: Phase3R — reward seimbang tanpa input baru.** Scene Phase 3
kini memakai reward revision 1 dan timeout dua lantai 25 detik. Bobot sumber
6.000.091 dipertahankan. Gunakan YAML Balanced; budget disetujui 2 juta step baru,
bukan resume YAML legacy. Lihat [README_PHASE3_BALANCED.md](README_PHASE3_BALANCED.md).

**Terbaru: Phase 3 dari Phase2E 7.000.033 (177 input).** Setup map besar dua
lantai, coverage 10% → 20% berdasarkan mastery, Max Step episode 12.000,
budget awal 4 juta telah selesai di 4.000.073, time scale 40×. Perubahan 27 Oktober
menaikkan Max Step episode ke 12.000 dan budget TOTAL ke 6 juta (sekitar 2 juta
tambahan, disetujui pengguna). Resume Phase3, bukan initialize ulang Phase2E.
Gunakan driver `train_phase3.py`; lihat [README_PHASE3_CURRENT.md](README_PHASE3_CURRENT.md).

**Terbaru 26 Oktober: Phase2E — memori ramp episodik, 177 input.** Phase2D selesai
5.000.036 (level 9, mastery akhir 69%). Migrasi dari bobot akhir sudah disiapkan,
tanpa mengubah Phase2D atau mengulang Phase 1. Budget total kini **7 juta step**;
jangan memakai command Phase2D pada scene dengan Ramp Memory ON. Lihat
[README_RAMP_MEMORY.md](README_RAMP_MEMORY.md).

**Terbaru: Phase2D — 161 input, terrain 1,5/3/6 m bersamaan.** Migrasi dari
baseline Phase2C 2.000.052 sudah disiapkan, counter tetap 2 juta menuju total
5 juta (30x), tanpa mengulang Phase 1. Gunakan
[README_MULTIRANGE_TERRAIN.md](README_MULTIRANGE_TERRAIN.md) dan YAML MultiRange;
command Phase2C di bawah merupakan riwayat, bukan command untuk scene baru.

**25 Oktober: Phase2C resume dari 2.000.052 ke total 5 juta step (30x).**
Scene Phase 2/3 memakai profil terrain jauh **6 m**, dekat tetap **1,5 m**;
Phase 1 tetap 1,5/3 m. Jumlah input tetap 149, tidak perlu migrasi/ulang Phase 1.
Ini perubahan distribusi input yang perlu fine-tuning, bukan bukti peningkatan
perilaku. Lihat update dan command di
[README_BRANCH_OBSERVATION.md](README_BRANCH_OBSERVATION.md).

**Terbaru 24 Oktober: Phase2C — memori cabang sebagai OBSERVASI PPO, bukan
action assistance.** Scene Phase 2/3 sekarang 149 input; bobot Phase2B 2.000.101
sudah dimigrasikan tanpa mengulang Phase 1. Pakai
[README_BRANCH_OBSERVATION.md](README_BRANCH_OBSERVATION.md). Instruksi Phase2B
hybrid di dokumen lama merupakan riwayat, bukan run aktif yang disarankan.

**21 Oktober: Phase2T** memakai checkpoint Phase1T 1.000.005 dengan transisi
coverage satu lantai -> transfer naik/turun dekat-jauh -> coverage dua lantai
seimbang. Lihat [README_PHASE2T.md](README_PHASE2T.md). Gunakan driver
`train_phase2_terrain.py`, bukan driver Phase2M* atau mlagents-learn langsung.

**Seri baru sensor terrain (20 Oktober):** ketiga scene utama sekarang mengaktifkan
sensor profil tinggi/kemiringan lokal tanpa tag. Mulai dari **Phase1T**, jangan
resume atau initialize dari checkpoint lama yang inputnya berbeda. Lihat
[README_TERRAIN_SENSOR.md](README_TERRAIN_SENSOR.md) untuk command, kompatibilitas
baseline lama dan cara verifikasi. Catatan di bawah adalah riwayat schema lama
(93 input), bukan command untuk seri terrain baru (117 input).

**Terbaru 20 Oktober:** eksperimen Phase2M5 dimulai dari bobot Phase2M4 15 juta,
dengan bridge pencarian dekat -> 3 tile -> 4 tile, tanpa perubahan reward/sensor.
Gunakan [README_MIXED_SEARCH.md](README_MIXED_SEARCH.md) untuk command mulai/resume.
Phase2M4 dan petunjuk berikut tetap disimpan sebagai baseline/riwayat.

**Terbaru 19 Oktober:** Phase2M4 sudah lolos near-search dan berada di lesson 3
(pencarian dekat/jauh dalam Phase 2). Lanjut checkpoint 13.000.072; budget resume kini 15 juta step
kumulatif dengan time scale tetap 40x. Lihat
[README_NEAR_SEARCH.md](README_NEAR_SEARCH.md).

**Update Phase2M4, 17 Oktober 2026:** lanjut checkpoint 3 juta dengan latihan
tangga bertahap, bantuan arah di-fade sampai nol, serta episode pengingat.
Lihat [README_STAIR_PRACTICE.md](README_STAIR_PRACTICE.md). Gunakan --resume,
bukan restart --force; budget resume kini 5 juta kumulatif.

**Konfigurasi terbaru: Phase2M4 mastery (16 Oktober 2026).** Gunakan
`uv run python Assets/Scripts/AI/Training/train_phase2.py <yaml>`, bukan
`mlagents-learn` langsung, untuk curriculum berdasarkan keberhasilan aktual.
Anti-spin sekarang memberi peringatan dan kesempatan pulih sebelum end episode.
Lihat
[README_STAIR_SEARCH.md](README_STAIR_SEARCH.md) untuk mulai dari baseline
Phase1M dengan learning rate tetap, reward pencarian tangga, serta gerbang
evaluasi sebelum coverage. Instruksi Phase2M3 di bawah menjadi arsip run lama.

Gunakan Unity ML-Agents Behavior Name `Explorer`. Hasil run lama `Phase1`,
`Phase2`, dan `Phase3` tidak dihapus. Sensor memori baru memakai run ID
`Phase1M`, `Phase2M`, dan `Phase3M`; jangan resume atau pasang model lama
karena bentuk input sekarang berbeda.
Percobaan ulang Phase 2 dengan tiga lesson memakai ID baru `Phase2M2`, sehingga
hasil `Phase2M` lama tidak tertimpa. Phase 1 tetap memakai `Phase1M`.
Setelah evaluasi `Phase2M2` di 9,06 juta step (ascent masih lemah), percobaan
transfer bertahap berikutnya memakai ID `Phase2M3`. Hasil `Phase2M2` tetap
disimpan; jangan resume run tersebut dengan konfigurasi baru.

## Scene dan target

| Fase | Scene | Kurikulum |
| --- | --- | --- |
| 1 Movement Control | `Assets/Scenes/AI/AI_Phase1_Movement.unity` | Koridor lurus, lalu koridor bertangga menuju target |
| 2 Map Routing | `Assets/Scenes/AI/AI_Phase2_SmallWFC.unity` | `Phase2M3`: stage 1 naik terpandu, 2 pencarian dekat, 3 campuran dekat/jauh, 4–5 coverage 10%–20% dua lantai |
| 3 Complex Routing | `Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity` | Coverage WFC 24 x 24 satu lantai, lalu dua lantai |

Lesson coverage dua lantai juga mensyaratkan pernah melewati trigger tangga dan selisih
coverage lantai 0 dan 1 paling banyak 10 poin persentase. Map WFC dibuat
ulang berkala (40 episode pada Fase 2, 20 episode pada Fase 3), dan lesson
dua lantai hanya menerima map dengan rute tangga valid.

Pada percobaan baru `Phase2M2`, Phase 2 mempunyai lesson 0, 1, dan 2.
Lesson 0 mempertahankan target coverage satu lantai. Lesson 1 memakai WFC
dua lantai: episode naik dan turun bergantian, agent harus mencari tangga,
melewati trigger-nya, lalu masuk area lantai tujuan dengan posisi kaki dekat
lantainya. Spawn dipilih dari area yang benar-benar terhubung ke pasangan
tangga yang sama. Episode berakhir saat transfer berhasil; coverage belum menjadi
syarat. Reward area pada lesson ini dibatasi agar reward eksplorasi saja tidak
mudah menaikkan curriculum. Lesson 2 memakai syarat coverage dua lantai
seimbang yang sebelumnya berlaku pada lesson 1. Phase 3 tidak berubah.

Percobaan `Phase2M3` **tidak mengulang satu lantai**: bobot awal berasal dari
checkpoint `Phase2M2` di step 8.249.944, saat ascent lebih baik daripada di
akhir run. Stage 1: WFC dua lantai, **100% episode naik**, spawn maksimal satu
langkah koridor dari mulut tangga dan yaw menuju tangga ±30 derajat. Agent tetap
memilih aksi gerak sendiri; yang ditentukan hanya posisi awal dan syarat sukses.
Stage 2: 75% episode naik dan 25% turun, spawn 0–2 langkah koridor dari
tangga dengan yaw acak. Stage 3 memperluas spawn menjadi campuran dekat/jauh
sampai empat langkah. Semua lesson transfer mensyaratkan trigger tangga dan
tiba pada lantai tujuan, serta memberi bonus keberhasilan ascent +0,4.
Stage 4 dan 5: coverage dua lantai seimbang dengan target 10% lalu 20%;
agent bebas memilih rute dan harus menjangkau
kedua lantai. Observasi dan ukuran network tidak berubah; parameter baru
`graduated_transfer=1` membuat run lama tetap memakai aturan lamanya.

Phase2 menghentikan episode dengan penalti -0.25 jika agent tidak berpindah
4 unit dari titik patokan dalam 5 detik simulasi pada lesson satu lantai,
atau 8 detik pada lesson dua lantai; Phase3 memakai 3 detik.
Phase2 lesson satu lantai dan Phase3 mengakhiri episode dengan penalti -0.25
jika tidak menemukan area baru selama 10 detik simulasi; Phase2 lesson dua
lantai memberi waktu 25 detik dan penalti -0.35 saat batas itu terlewati.
MaxStep Phase2 tetap 4000 pada lesson satu lantai, tetapi menjadi 6000 pada
lesson dua lantai. Gerakan melingkar besar masih bisa melewati
ambang perpindahan, tetapi tidak mereset waktu area baru. Reward area unik adalah
+0.02; lantai 2 (floorIndex 1) mendapat bonus +0.02. Reward dasar dan bonus
lantai 2 masing-masing dibatasi 1 per episode; revisit tidak memperoleh reward
area baru. Reward Phase1 tidak diubah. Threshold reward curriculum Phase2/3 menjadi 1.1
agar kenaikan reward area tidak memindahkan lesson terlalu dini.

Ketiga scene utama memakai observasi dengan bentuk yang sama: 10 nilai vektor,
17 ray 360 derajat (34 nilai), dan sensor memori kunjungan 7 x 7 (49 nilai).
Total input 93 nilai; aksi tetap maju/mundur dan berbelok. Sensor memori
menghitung masuknya agent ke sel spasial 8 unit, memisahkan lantai tiap 4 unit,
dan menampilkan nilai 0..1 untuk 0..4 kunjungan di sekitar agent. Baris pertama
adalah arah depan terdekat (dibulatkan ke arah mata angin). Nilainya di-reset
setiap episode; 0 berarti tidak pernah dilewati, bukan kepastian lantai bisa
dilalui. Sensor tidak membaca layout WFC atau area yang belum dijelajahi.
Komponen ditambahkan otomatis pada scene lama saat Agent diinisialisasi;
rebuild scene berikutnya juga memasangnya secara permanen.
Fase 1 memperlihatkan arah target. Fase 2 dan 3 tidak memperlihatkan target;
slot arah dipakai untuk status lesson, waktu sejak area baru, dan rasio revisits.

## Siapkan scene

Setelah script Unity selesai dikompilasi, buka ulang scene fase yang sesuai
dan pastikan `Behavior Type = Default` serta field Model kosong saat training.
Tidak perlu rebuild scene untuk sensor memori: agent memasangnya otomatis
sebelum ML-Agents membuat spesifikasi observasi. Menu
`Tools > AI > Create or Rebuild Training Scenes` hanya dipakai jika memang
ingin membuat ulang scene; menu itu dapat menimpa perubahan scene manual.
Scene 3A/3B lama tetap ada sebagai arsip, tetapi tidak dipakai dalam workflow
baru.

## Training berurutan

Jalankan perintah dari root proyek, kemudian tekan Play di scene Phase 2.
Phase 1 dan eksplorasi satu lantai sudah dilatih. Untuk transfer bertahap,
gunakan run ID **baru** dan jangan memakai `--resume` pada awal run:

```powershell
uv run mlagents-learn Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_EasyTransfer.yaml --run-id=Phase2M3
```

Checkpoint `Phase2M2` step 8.249.944 menjadi bobot awal `Phase2M3`.
Curriculum menaikkan lesson berdasarkan ambang reward; lihat `AI/Lesson`,
`AI/Floor Transfer Spawn Steps`, dan tingkat sukses kedua arah di TensorBoard.
Run ini dimulai dengan batas 6 juta step; konfigurasi terbaru mempunyai
batas 8 juta step dan time scale 40. Kecepatan
nyata perlu dinilai dari step per detik; jika agent/tangga menjadi tidak stabil
atau step per detik tidak meningkat, kembalikan kedua YAML ke 25.

Untuk melanjutkan run `Phase2M3`, gunakan YAML tanpa `init_path`:

Setelah evaluasi checkpoint 6.000.112 (14 Oktober 2026), batas resume menjadi
8 juta step. State curriculum tetap index 1, sehingga resume memulai
**NearStairSearch** (AI/Lesson 2).

| AI/Lesson | Latihan | Spawn / syarat selesai |
|---|---|---|
| 1 | GuidedAscent | Naik, 0–1 tile dari mulut tangga; yaw awal diarahkan |
| 2 | NearStairSearch | Naik/turun 75%/25%, 0–2 tile, yaw acak |
| 3 | MixedDistanceStairSearch | Naik/turun 75%/25%; campuran 50% jarak 0–2 dan 50% jarak 3–4 tile, jika tersedia |
| 4 | BalancedCoverage10Percent | Minimal 10% pada masing-masing lantai dan traversal tangga |
| 5 | BalancedCoverage20Percent | Minimal 20% pada masing-masing lantai dan traversal tangga |

Pada lesson transfer bertahap, jarak dihitung dari socket/path tile; batas
jarak Euclidean tidak menghapus contoh yang dekat tangga. Filter ring terjauh
tetap dipakai oleh curriculum tangga lama. Pada lesson 4/5, selisih coverage
antar-lantai maksimal 10 poin persentase sesuai konfigurasi scene. Pantau
`AI/Lesson`, `AI/Floor Transfer Spawn Steps`, dan `AI/Coverage Target`.

Promosi otomatis memakai rata-rata reward (ambang 0.8, 0.8, 1.0, 1.2), bukan
jaminan tingkat keberhasilan naik/turun. Evaluasi lagi setelah sekitar
500 ribu step tambahan; sebelum Phase 3 pastikan coverage per lantai dan
traversal sudah stabil. Observasi, arsitektur model, limit step episode dua
lantai (6000), serta time scale 40 tetap sama.

```powershell
uv run mlagents-learn Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_EasyTransfer_Resume.yaml --run-id=Phase2M3 --resume
```

Sebelum training, pastikan `Behavior Type = Default`, Model kosong, dan script
Unity selesai dikompilasi. Jangan pakai `--force`, agar run lama aman.

Bagian di bawah mengarsipkan perintah dan evaluasi percobaan `Phase2M2`.

Untuk mengulang Phase 2 dengan tiga lesson, buka scene Phase 2 yang sama,
pastikan `Behavior Type = Default` dan Model kosong. Ini adalah run **baru**,
jadi jangan gunakan `--resume` atau `--force` saat pertama kali menjalankannya.

Run ini menginisialisasi bobot dari checkpoint terakhir `Phase1M`, memakai
time scale 25 dan batas total 12 juta step. Kenaikan lesson 1 ke 2 ditentukan
oleh rata-rata reward, bukan jaminan matematis kedua arah sudah andal. Cek
`AI/Lesson`, `AI/Ascent Reached`, `AI/Descent Reached`,
`AI/Stair Ascent Success`, dan `AI/Stair Descent Success` di TensorBoard sebelum
menilai model. Kedua arah sebaiknya meningkat, bukan hanya salah satunya.

Jika run `Phase2M2` berhenti, tutup Play dahulu, lalu lanjutkan dengan YAML
resume khusus tanpa `init_path`:

```powershell
uv run mlagents-learn Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Memory_3Lesson_Resume.yaml --run-id=Phase2M2 --resume
```

Jangan jalankan Phase 3 lama langsung sebagai kelanjutan `Phase2M3`: YAML
Phase3M saat ini masih menunjuk checkpoint `Phase2M`. Saat Phase 2 baru
selesai dan hasilnya dinilai, ubah `init_path` Phase 3 ke checkpoint
`results/Phase2M3/Explorer/checkpoint.pt` dan gunakan ID Phase 3 baru agar
perbandingan run lama tetap aman.

Run lama `Phase2M` dan `Phase3M` tetap tersimpan. Jika perlu melanjutkannya,
gunakan YAML khusus resume yang **tidak** memiliki `init_path`. ML-Agents 1.1
mendahulukan `init_path` meskipun `--resume` aktif; YAML run pertama dapat
memuat kembali checkpoint Phase1M. Jangan gunakan `--force` saat resume:

```powershell
uv run mlagents-learn Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Memory_Resume.yaml --run-id=Phase2M --resume
uv run mlagents-learn Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Memory_Resume.yaml --run-id=Phase3M --resume
```

Batas total Phase2M adalah 8 juta step. Konfigurasi run lama tetap ada sebagai
arsip eksperimen, tetapi tidak cocok dengan sensor baru.

Gunakan TensorBoard untuk melihat `Environment/Cumulative Reward`,
`AI/Success`, `AI/Lesson`, coverage tiap lantai, dan stair traversal:

```powershell
uv run tensorboard --logdir results
```

Pada episode baru setelah pembaruan metrik, `AI/Floor Transitions` mencatat
jumlah perpindahan lantai per episode; `AI/Ascent Transitions` dan
`AI/Descent Transitions` memisahkan arahnya. Perpindahan dihitung ketika agent
memasuki area lantai tujuan dengan posisi kaki dekat lantai tersebut, termasuk
jika ia kembali ke lantai sebelumnya. `AI/Ascent Reached` hanya dicatat pada
episode rute bawah-ke-atas, dan `AI/Descent Reached` hanya pada rute
atas-ke-bawah. Rata-rata kedua metrik terakhir adalah tingkat keberhasilan
*mencapai lantai tujuan*, berbeda dari `AI/Stair Ascent Success` dan
`AI/Stair Descent Success` yang mensyaratkan seluruh target episode selesai.
Metrik ini tidak mengubah reward maupun observasi model dan tidak dapat
ditambahkan secara retroaktif ke log run lama.

Untuk Play Mode tanpa trainer, atur `Default Phase One Lesson` pada environment
Phase1 atau `Default Floor Lesson` pada WFC adapter Phase2 ke 0, 1, 2, atau 3
(Phase3 tetap 0 atau 1). Untuk menguji aturan Phase2M3 tanpa trainer, aktifkan
`Default Graduated Transfer Lessons` pada WFC adapter; jika tidak, aturan
Phase2M2 lama tetap dipakai.
Nilai ini dipakai hanya ketika trainer tidak mengirim parameter curriculum.

Training default ML-Agents dapat berjalan cepat. Jika ingin mengamati gerak
agent lebih pelan, tambahkan pada YAML fase yang sedang dilatih:

```yaml
engine_settings:
  time_scale: 1
```
