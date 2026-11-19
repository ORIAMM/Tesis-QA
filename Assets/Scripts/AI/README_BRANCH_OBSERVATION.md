# Phase2C — memori cabang sebagai OBSERVASI PPO

**DIGANTIKAN oleh Phase2D 161 input (dekat/menengah/jauh bersamaan).**
Lihat [README_MULTIRANGE_TERRAIN.md](README_MULTIRANGE_TERRAIN.md) untuk setup,
migrasi dari baseline 2 juta, dan command resume menuju total 5 juta.
Instruksi Phase2C/149 input di bawah disimpan sebagai riwayat; scene Phase 2/3
sekarang mengaktifkan Distant Terrain dan tidak cocok dengan command lama
tanpa menyesuaikan opsi sensor sesuai model.

## Update 25 Oktober: lanjut 2 juta -> total 5 juta, terrain jarak jauh

Checkpoint resume terverifikasi: `results/Phase2C/Explorer/checkpoint.pt`, step
**2.000.052**. Budget YAML sekarang **5.000.000 total**, time scale tetap **30x**.
Gunakan command resume di bawah; jangan `--force` atau menjalankan migrasi ulang.
Training tidak dijalankan otomatis oleh perubahan sensor.

- Scene Phase 2 dan 3: **Use Long Range Terrain Profile ON**. Phase 1 tetap OFF.
  Buka ulang scene setelah compile, tidak perlu rebuild scene atau prefab tangga.
- Tetap enam probe/24 nilai terrain dan total **149 input**. Probe dekat kiri,
  tengah, kanan tetap **1,5 m**, rentang vertikal **+/-2 m**. Tiga probe jauh
  **diganti dari 3 m menjadi 6 m** (bukan menambah ring probe baru), dengan
  pencarian vertikal **+/-6 m**. Sudut tetap -30/0/+30 derajat.
- Normalisasi tinggi lama `/2` tetap dipakai; tinggi di luar +/-2 m dijepit ke
  -1/+1. Normal/slope dan flag permukaan terlihat tetap tersedia pada titik jauh.
- Tetap berbasis collider fisik dan line-of-sight, tanpa tag Stair, arah oracle,
  atau penggantian aksi PPO. Tidak mengukur setiap titik sepanjang 0..6 m:
  permukaan harus mengenai salah satu titik sampel dan terlihat, bukan tertutup
  tembok/lantai tepi. Collider smooth ramp diperlukan untuk slope kontinu;
  anak tangga datar memberikan perubahan tinggi, bukan normal permukaan miring.
- Bentuk input tidak berubah, jadi bobot, LSTM, normalizer dan optimizer dapat
  di-resume langsung; **tidak perlu mengulang Phase 1**. Namun distribusi tiga
  probe jauh berubah: ini fine-tuning dengan persepsi baru, bukan jaminan perilaku
  identik. Reward, curriculum, MaxStep, speed dan riwayat mastery tidak diubah.
- `AI/Terrain Far Distance` mencatat 6 pada episode baru (3 untuk profil lama).
  Catat batas perubahan ini saat membaca grafik training: data sebelum sekitar
  2 juta step dan setelahnya memakai jangkauan berbeda. Bandingkan model lama
  dengan opsi long range OFF, model hasil fine-tuning dengan opsi ON.
- Baseline model 2 juta, PT dan metadata disimpan permanen di
  `results/RecoveryBackups/Phase2C_before_long_range_2000052`, di luar rotasi
  `keep_checkpoints`. Checkpoint asli tidak dimigrasikan atau diubah.

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml --run-id=Phase2C --resume
```

Stop Play/trainer lama, tunggu compile, buka ulang `AI_Phase2_SmallWFC`, Model
kosong dan Behavior Type Default. Jalankan command dari root proyek, lalu Play
ketika menunggu Unity. Saat menonton ONNX, pilih Inference Only dan cocokkan
profil terrain dengan kondisi training checkpoint-nya.

Validasi perubahan: 37 unit test Python lolos; actor/critic/curiosity/LSTM dan
optimizer checkpoint 2.000.052 berhasil strict-load dengan schema yang sama.
Unity batch pada salinan `.ai-eval` exit 0: probe dekat tidak berubah, sampel 6 m
datar/naik/turun, batas +/-6 m, occlusion tembok, trigger, lantai bertumpuk,
yaw, void, serta pemasangan profil pada ketiga scene. Log final:
`Logs/terrain-long-range-final.log`. Ini validasi sensor/kompatibilitas, bukan
bukti coverage atau keberhasilan training meningkat.

---

24 Oktober 2026. Ini menggantikan action assistance Phase2B. **Tidak ada
pengalihan gerakan, waypoint, action mask, atau tambahan penalti cabang.** PPO
tetap memilih semua aksi. `ExplorationAgent` tidak lagi memanggil helper
`AICompletedBranchAssist.Filter`; kelas lama hanya tersimpan untuk riwayat/test.

## Mulai: tidak mengulang Phase 1

Run Phase2B telah menyimpan checkpoint **2.000.101**. Checkpoint ini dibawa
ke schema baru melalui migrasi eksplisit, bukan silent partial-load ML-Agents:

- Sumber tidak diubah: `results/Phase2B/Explorer/Explorer-2000101.pt`.
- Hasil: `results/BranchObservationInit/Phase2C/Explorer-0.pt` dan `.onnx`.
- Laporan: `results/BranchObservationInit/Phase2C/migration.json`.
- Bobot actor/critic/LSTM/curiosity dan normalisasi lama disalin. Hanya 32
  kolom input baru ditambahkan dengan bobot nol; normalisasi fitur baru segar.
- Optimizer direset, counter baru 0. Ini NEW fine-tuning run, bukan resume.
- Uji encoding policy dengan batch 1/4/16, observasi lama dan recurrent state
  yang sama, menghasilkan perbedaan maksimum sekitar 1,88e-6. Ini kesetaraan
  numerik saat inisialisasi, BUKAN bukti kinerja eksplorasi baru sudah baik.
- Awalnya policy belum menggunakan informasi cabang karena bobot baru nol.
  Training diperlukan untuk mempelajari penggunaannya. Uji gradien memastikan
  bobot input baru dapat berubah, bukan fitur yang terputus dari network.

Tidak perlu memulai Phase 1/seluruh Phase 2 dari bobot acak. Lanjutkan bagian
coverage dua lantai pada **level 8**, lalu level 9..11 berdasarkan mastery.
20% rehearsal pada level 8 tetap latihan transfer level 7. Tidak ada perubahan
reward, curiosity strength, MaxStep, jadwal map, atau aturan kelulusan.
Jika kontrol speed 6 ternyata memburuk, evaluasi latihan transfer terfokus;
kompatibilitas input sendiri bukan alasan mengulang semua fase.

## Setup Unity dan command

1. Stop trainer/Play lama, tunggu Unity compile, buka ulang scene
   `Assets/Scenes/AI/AI_Phase2_SmallWFC.unity`. Tidak perlu rebuild.
2. ExplorerAgent: **Use Terrain Profile Sensor ON**, **Use Branch Memory Sensor ON**.
   Opsi hybrid lama sudah dinonaktifkan pada scene dan tidak digunakan lagi.
3. Behavior Name `Explorer`, Behavior Type `Default`, Model **kosong**.
   Base Vector Observation Size tetap **10**, bukan 149.
4. Motor speed **6**, rotation **180**, Decision Period **5**.
5. Dari root proyek, lalu tekan Play ketika trainer menunggu:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml --run-id=Phase2C
```

Budget awal **2 juta step baru** (kini diperpanjang menjadi total 5 juta, lihat
update di atas), time scale **30x**. Training belum dijalankan
otomatis. Counter 0 berarti counter run baru, bukan kehilangan bobot lama.
Jangan memakai YAML Phase2B lama atau `--resume` pada mulai pertama Phase2C.
Checkpoint Phase1T/Phase2T/Phase2B tidak ditimpa.

Resume setelah Ctrl+C tersimpan selesai:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml --run-id=Phase2C --resume
```

Driver memeriksa mode sensor/step metadata dan menghapus init_path saat resume,
sehingga tidak memuat ulang checkpoint inisialisasi. Memakai YAML baru untuk
resume Phase2B lama ditolak. Jangan `--force`.

Migrasi SUDAH disiapkan. Untuk mereproduksi migrasi di folder baru, gunakan
`Training/prepare_branch_observation.py --source <PT> --destination <folder>`;
folder tujuan harus baru di bawah `results/BranchObservationInit`. Tool menolak
overwrite dan schema sumber selain `[24,34,49,10]`. Tidak perlu menjalankannya
lagi sebelum command training di atas.

## Observasi yang benar-benar diterima model

Urutan sensor disimpan kompatibel sebagai prefix:

| Input | Isi | Jumlah |
|---|---|---:|
| obs_0 | Terrain | 24 |
| obs_1 | Ray | 34 |
| obs_2 | Memori jumlah kunjungan 7x7 | 49 |
| obs_3 | Vector gerak/status | 10 |
| obs_4 | Memori cabang baru | 32 |

Total **149 nilai**, recurrent/LSTM 128 tetap. Nama sensor baru sengaja
`Z Explorer Branch Memory v1` agar diurutkan SETELAH VectorSensor; ini mencegah
pergeseran 117 kolom lama. Sensor terpisah, sehingga Vector Size tetap 10.

Delapan arah relatif agent (dibulatkan ke yaw 45 derajat): depan, depan-kanan,
kanan, belakang-kanan, belakang, belakang-kiri, kiri, depan-kiri. Tiap arah
mengirim empat angka:

1. Status koneksi: **-1 tertutup**, **0 belum diketahui**, **1 terbuka**.
2. Proporsi node komponen teramati yang area dekatnya telah dikunjungi, 0..1.
3. Proporsi port yang masih UNKNOWN dalam komponen, 0..1.
4. Estimasi cabang lokal selesai pada graf, 0/1.

Contoh `[1, 1, 0, 1]` berarti arah terbuka menuju komponen lokal yang seluruh
area representasinya telah dikunjungi dan tidak menyisakan port unknown.
PPO tetap BOLEH melewatinya untuk transit. Nilai ini bukan perintah belok.
Memori direset setiap episode; checkpoint tidak menyimpan jejak map episode.

## Mengapa detektor diubah?

Versi lama mengharuskan melewati pusat setiap node grid kecil. Titik samping
koridor lebar menjadi frontier permanen. Selain itu, CapsuleCast dapat mengenai
dinding sedikit DI LUAR ujung garis pusat karena radius tubuh, tetapi ray saksi
berhenti terlalu pendek; dinding tersebut keliru dibiarkan UNKNOWN.
Grid juga membuat siklus kecil dari jalur paralel di koridor lebar.

Sensor baru melakukan pemetaan terbatas pada permukaan yang terlihat dari
agent, lattice 2 m, radius pemilihan pusat node 6 m, maksimal 4 pusat node diproses per scan
0,1 detik. Area node diberi status dikunjungi ketika berada maksimal 2,5 m dari
lintasan aktual DAN terlihat/terhubung melalui clearance tubuh pada permukaan
datar. Ini definisi kunjungan REGION spasial, bukan klaim setiap titik lantai
tersentuh. Tembok di antaranya mencegah pemberian status lintas tembok.

Ringkasan arah memotong penampang lokal pada graf, bukan hanya satu titik,
agar lajur paralel tidak otomatis dianggap jalur transit lain. Unknown atau
komponen yang belum dikunjungi mencegah selesai. Bukti dinding harus terlihat
dari posisi agent; tidak membaca map lengkap/socket WFC, tag Stair, collider
ExplorationArea, koordinat defect atau jalur oracle. Data fisika di balik
tembok tidak dimasukkan ke graf. WFC hanya digunakan oleh TEST sebagai oracle
untuk memilih posisi replay, bukan oleh sensor/policy.

## Batasan dan evaluasi

- Selesai adalah estimasi pada representasi graf lokal/resolusi sampling,
  bukan jaminan semua ruang kontinu atau semua defect sudah diperiksa.
- Ramp, tangga, void dan data ambigu tetap UNKNOWN, bukan ditutup sebagai
  buntu. Graf permukaan datar berbeda ketinggian dipisahkan; belum menyimpulkan
  seluruh cabang lintas lantai selesai. Sensor terrain/ray tetap membantu
  traversal. Koneksi yang belum jelas tidak dilarang karena tidak ada mask.
- Geometri diasumsikan statis per episode. Perubahan collider di tengah episode
  memerlukan reset memori; bukti lama dapat kedaluwarsa.
- Sensor ini tidak otomatis memberi strategi terbaik atau menjamin tidak
  revisit. Policy harus mempelajari relevansinya melalui training.
- Tidak ada klaim peningkatan coverage/siap Phase 3 dari uji geometri/migrasi.
  Coverage resmi tetap dari trigger area lama, bukan jumlah node sensor.

Pantau dengan `uv run tensorboard --logdir results/Phase2C`: `AI/Branch
Observation Active`, `AI/Branch Memory Nodes`, `AI/Completed Branch Exits`,
`AI/Branch Unknown Ports`, `AI/Branch Unknown Fraction`, mastery success, coverage per lantai dan selisih
PER EPISODE, transfer selesai, collision, timeout. `AI/Branch Assist Active`
harus 0. Tidak ada counter redirect baru karena tidak ada redirect.

Model lama 117 input hanya dapat ditonton dengan Use Branch Memory Sensor OFF
SEBELUM Play. Model baru 149 input perlu ON. Model initial hasil migrasi belum
belajar fitur cabang; jangan menganggapnya hasil training Phase2C. Untuk survey
dan uji perilaku gunakan time scale 1x, speed 6 yang sama untuk kondisi yang
dibandingkan. Seed pemilihan model berbeda dari seed uji akhir tesis.

Untuk tesis tulis PPO + curiosity dengan fitur memori spasial/cabang episodik
hasil preprocessing sensor. Ini bukan kontrol hybrid planner+PPO, karena tidak
ada modul lain memilih/mengganti aksi, tetapi juga bukan persepsi kamera mentah
atau kemampuan mengenali cabang yang sepenuhnya dipelajari tanpa rekayasa fitur.

## Hasil pemeriksaan implementasi (bukan hasil training)

Unity 6000.5.4f1 pada salinan `.ai-eval` berhasil memeriksa schema/urutan sensor
dan geometri: void, ramp naik/turun, anak tangga, trigger, tembok occluder,
koridor lebar buntu, area belum dikunjungi, jalur transit, yaw, reset episode,
serta tidak adanya perubahan posisi/rotasi oleh sensor.

Replay posisi geometris pada tiga map WFC juga menghasilkan label selesai:

| Seed | Posisi tile replay | Node teramati | Jumlah observasi selesai |
|---|---:|---:|---:|
| 283003 | 107 | 1246 | 11 |
| 314159 | 118 | 1360 | 6 |
| 92017 | 86 | 1007 | 13 |

Hitungan terakhir adalah akumulasi keluaran arah pada replay, BUKAN jumlah
cabang unik, episode berhasil atau keberhasilan PPO. Replay memindahkan probe
ke posisi uji; tidak menunjukkan agent mampu mencapai posisi itu sendiri.
Log: `Logs/branch-observation-checks.log`. Ini seed preflight/pemilihan desain,
bukan seed uji tesis independen. Semua 37 tes Python driver/migrasi lolos.
