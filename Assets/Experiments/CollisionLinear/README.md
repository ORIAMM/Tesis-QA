# Lima trial progresif

Profil ini adalah arsip rancangan 1,2,3,4,5. Scene peserta dan scene AI sekarang memakai
Assets/Experiments/CollisionSelected/CollisionSelected.asset dengan target 1,2,2,1,2,
tiga peta kecil diikuti dua peta besar. Lihat README pada folder CollisionSelected.

Protokol collision-linear-walls-v2 memakai checkpoint Phase3R 2.000.063 yang sama, tanpa training ulang.
Scene peserta: Assets/Scenes/AI/QA_ParticipantGame.unity.
Scene AI/peneliti dengan manifest sama: Assets/Scenes/AI/QA_LinearCollisionExperiment.unity.

| Trial | Full collider hilang | Lubang sebagian | Total |
| --- | ---: | ---: | ---: |
| 1 | 1 | 0 | 1 |
| 2 | 1 | 1 | 2 |
| 3 | 2 | 1 | 3 |
| 4 | 2 | 2 | 4 |
| 5 | 3 | 2 | 5 |

Urutan tetap 1..5, termasuk jika participantOrder/scenarioOrder diubah.
Manifests berisi posisi defect, layout, spawn, dan seed yang dibekukan.
Model tidak menerima label defect. Marker manusia divalidasi menggunakan mesh visual
dan physics support; percobaan F yang ditolak tetap dicatat terpisah pada markAttempts.

Seed tetap untuk AI dan setiap peserta:

| Trial | Map seed diminta | Map seed aktual | Defect seed | Spawn seed | Defect |
| --- | ---: | ---: | ---: | ---: | ---: |
| L1 | 170000001 | 170007920 | 170184691 | 28320 | 1 |
| L2 | 170004100 | 170004100 | 170344438 | 28321 | 2 |
| L3 | 170008199 | 170008199 | 170340341 | 28322 | 3 |
| L4 | 170012298 | 170012298 | 170336248 | 28323 | 4 |
| L5 | 170016397 | 170016397 | 170332159 | 28324 | 5 |

L1 menggunakan retry WFC sehingga seed aktual berbeda dari seed awal. Untuk mengulang
eksperimen, gunakan manifest beku, bukan hanya memasukkan ulang seed aktual ke generator.
Peserta A, B, dan seterusnya menjalani kelima manifest yang sama; seed tidak diacak per orang.
Stream sampling inference AI dimulai pada 58231, berlanjut antartrial/pengulangan;
memori LSTM direset pada setiap episode. Tiga pengulangan memakai manifest yang sama.

Detector local-floor-wall-v3 memeriksa lantai serta tembok/railing vertikal pada tile
non-tangga. Selain 54 ray lantai, ada 52 ray tembok pada tinggi relatif pemain
0.45, 0.75, 1.15, 1.65 m dengan sudut yaw -60..60 derajat. Jarak tetap 6 m dan kandidat
harus muncul pada dua scan berturut-turut. Probe physics pendek sepanjang normal
permukaan memastikan tembok yang lebih jauh tidak menutupi collision yang hilang.
Detector berada di luar sensor observasi ML-Agents, sehingga input/model tetap sama.

F manusia juga menerima tembok yang kehilangan collision. surfaceKind dan normal
dicatat pada findings. Scorer collision-region-wall-v3 menghubungkan laporan lantai
dan tembok dari collider yang sama ke defect ID yang sama, sehingga total target tetap
1,2,3,4,5 dan tidak bertambah karena satu collider memiliki beberapa permukaan.
Versi detector/scorer/protokol baru menghasilkan hash baru; hasil versi lama tetap terpisah.
Protokol pilot lama tidak otomatis mengaktifkan pemeriksaan tembok.

Batch AI untuk scene ini memakai executeMethod
TesisQA.AI.Editor.DefectExperimentSetup.RunLinearAutomatic, dengan -defectMode AI
dan -defectOutput direktori baru. Opsi -defectSmokeSeconds hanya untuk uji integrasi
singkat dan tidak menghasilkan data evaluasi berdurasi penuh.

Anggaran AI adalah 150 detik simulasi per trial. Trial AI tetap berjalan sampai batas
waktu, sehingga durasi trial bukan waktu berhasil menemukan seluruh defect. Waktu
temuan pertama dihitung dari finding true positive pertama. Waktu seluruh defect
ditemukan hanya terdefinisi jika semua ID defect unik sudah ditemukan; jika tidak,
laporkan belum selesai dalam 150 detik beserta jumlah temuan/recall, bukan waktu sukses 150.
Batch memakai timeScale 20: wall_seconds mengukur durasi komputasi yang dipercepat.
Untuk pembanding gameplay, gunakan simulation_seconds AI dan waktu aktif peserta
pada timeScale 1. Waktu peserta menekan Selesai trial dapat berbeda dari waktu temuan
terakhir; gunakan timestamp findings untuk membandingkan pencapaian target yang sama.

Baseline penuh Phase3R tanggal 28 Oktober 2026 tersimpan di
results/DefectExperiments/linear_walls_ai_baseline_20261028/HASIL_AI.md.
Lima skenario masing-masing dijalankan tiga kali. Data sumber JSON, summary.csv,
timing_by_run.csv, serta timing_by_scenario.json disimpan dalam folder yang sama.
Ringkasan memisahkan waktu temuan pertama, waktu semua target ditemukan, dan timeout.

F pada anomali yang sudah ditandai tidak menambah marker. Jatuh ke void memulihkan pemain
ke tile sehat terakhir pada lantai yang sama. Jika checkpoint tidak lagi aman,
cari tile sehat terdekat pada lantai checkpoint tersebut.
Temuan dan waktu trial tidak direset. Selesaikan trial melalui Esc > Selesai trial.

Protokol pilot sebelumnya (0,2,2,4,4 defect) dan hasilnya tetap disimpan terpisah.
Menu Editor Prepare linear 1 to 5 trials dapat memvalidasi dan menghubungkan kembali
protokol ini ke kedua scene; tidak membuat build Windows.
