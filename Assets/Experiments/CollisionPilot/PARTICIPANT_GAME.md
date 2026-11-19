# Build game peserta

Scene khusus: `Assets/Scenes/AI/QA_ParticipantGame.unity`.
Build Windows: `Builds/ColliderQuest/ColliderQuest.exe`, launcher `Mulai_Game.cmd`.
Editor menu: **Tesis QA > Defect Experiment > Build participant game**.
Pembuatan build menggunakan path baru jika target .exe sudah ada.

## Preview dan UI Canvas

Buka scene lewat **Tesis QA > Defect Experiment > Open participant game scene**,
lalu tekan Play dan pilih tab Game. Hierarchy memiliki **Participant Canvas** dan
**Participant EventSystem**. Semua menu, HUD, jeda, penanda laporan, dan ringkasan hasil
menggunakan uGUI Canvas; UI peserta tidak memakai OnGUI.
Canvas memakai Screen Space Overlay dan CanvasScaler Scale With Screen Size,
reference resolution 1920x1080, Match Width Or Height 0.5. HUD memakai anchor layar;
kolom menu/hasil tersusun vertikal ketika lebar Canvas sempit. Elemen bisa diedit
langsung di Hierarchy/Inspector. Menu **Prepare participant Canvas** melengkapi scene
jika Canvas belum ada, tanpa menghapus Canvas yang sudah diedit.

Kendali manusia: **W/S** maju/mundur, **A/D** bergerak menyamping, **mouse** yaw/pitch,
**Space** melompat, **F** menandai lantai/tembok bidikan. Kecepatan diagonal dinormalisasi. Mouse terkunci selama
bermain; **Esc** membuka menu jeda dan melepas kursor. Pilih **Lanjut bermain** untuk
mengunci kursor kembali. Tidak ada tombol untuk menyelesaikan trial secara manual.
Sensitivity mouse dapat diatur pada komponen DefectExperimentSession di Inspector.
Lompat manusia berupa lompatan rendah dan jauh: tinggi nominal 1,5 m dan jangkauan
nominal 10 m saat terus bergerak ke satu arah di ruang bebas. Ini memungkinkan
menyeberangi satu tile WFC 8 m dari pijakan sehat dekat tepi. Gunakan W + Space
untuk lompat maju; WASD/mouse tetap mengarahkan gerak di udara. Benturan tembok
atau plafon dapat memperpendek lompatan. Tidak ada double jump; menahan Space
tidak mengulang lompatan. Input lompat diblokir saat pause/respawn dan dihapus saat reset.
Motor/model AI tidak memiliki aksi lompat. Perbedaan kemampuan manusia ini perlu
disebutkan dalam metode perbandingan; hasil baseline AI tidak diubah oleh fitur ini.
Kehilangan fokus game juga membuka jeda. Keluar dari menu dalam Editor menghentikan Play.

Perubahan ini disiapkan dalam source dan scene Editor. Build/ZIP yang dibuat sebelumnya
masih memakai versi kontrol lama; peneliti melakukan build ulang setelah preview sesuai.
Jika memakai File > Build Profiles, masukkan QA_ParticipantGame sebagai scene awal yang
aktif untuk build peserta. Fungsi Build participant game mempertahankan Canvas scene
yang sudah diedit dan tidak menyusun ulang UI dari scene AI.

Peserta langsung memilih Mulai bermain tanpa ID, pemilihan mode, seed, atau urutan.
HUD menampilkan instruksi F, kendali, trial saat ini, dan waktu tersisa.
Menu **Cara bermain** menyertakan dua screenshot contoh: kotak kuning menunjukkan
collider yang hilang hanya pada sebagian tile, sedangkan batas merah menunjukkan
collider hilang pada seluruh satu tile kotak. Garis warna pada gambar hanya ilustrasi,
bukan petunjuk lokasi defect di trial. Petunjuk singkat yang sama tampil pada HUD.
Gambar disimpan sebagai sprite pada CollisionSelected/Resources/ParticipantDefectGuide
agar ikut tersedia saat build. Lantai visual dapat tetap terlihat utuh pada kedua jenis defect.
F memberi konfirmasi pada anomali yang diterima; HUD menampilkan progres target unik.
Penanda hijau hanya dibuat jika lantai atau tembok visual pada bidikan kehilangan collision.
F pada permukaan sehat ditolak, dan F pada anomali yang sudah ditandai tidak membuat penanda baru.
Pemeriksaan memakai mesh visual dan probe physics, tanpa membaca label injection.
Semua percobaan F dicatat dalam markAttempts pada JSON (missing_collision,
supported_floor, already_marked, atau no_floor_in_range). Findings berisi penanda yang diterima.

Scene peserta sekarang memakai protokol **collision-small-large-study-v1** pada
`Assets/Experiments/CollisionSelected/CollisionSelected.asset`: trial 1..5 berisi **1,2,2,1,2**
defect. Trial 1-3 memakai WFC 6x2x6, trial 4-5 memakai WFC 12x2x12.
Urutan tetap; scenarioOrder pada ParticipantSettings.json diabaikan oleh protokol
ini. Scene AI/peneliti yang cocok adalah
`Assets/Scenes/AI/QA_LinearCollisionExperiment.unity`, dengan lima manifest yang sama.
Scene QA_CollisionExperiment dan protokol collision-pilot-v1 lama tetap memakai 0,2,2,4,4;
hasil pilot lama tidak boleh diperlakukan sebagai hasil konfigurasi progresif baru.
Komposisi full/partial pada trial 1..5 adalah 1/0, 1/1, 1/1, 1/0, dan 1/1.
Seed diseleksi berdasarkan keberhasilan AI menemukan seluruh target dan dibekukan
bersama layout, spawn, dan posisi injeksi. Hasil lama 1,2,3,4,5 disimpan terpisah.
Inspect Walls aktif: detector AI dan F manusia dapat memeriksa tembok non-tangga.
Lantai dan tembok yang kehilangan collision dari collider yang sama dihitung sekali.
Ray tembok merupakan detector eksternal, bukan tambahan observasi atau kendali policy.
Peneliti dapat membuka scene evaluasi linear dan memilih AI atau Random dari menu sesi.
Trial otomatis berhasil tepat setelah semua ID defect target unik ditemukan melalui F,
dengan status complete dan finishReason all_defects_found. Temuan berulang atau laporan
yang tidak cocok dengan target tidak melengkapi progres. Peserta tidak dapat mengakhiri
trial sebelum target lengkap. Batas tetap 150 detik waktu aktif; jika habis sebelum
target lengkap, trial berakhir dengan status failed dan finishReason time_limit.
Laporan setelah deadline tidak dapat mengubah timeout menjadi sukses. Layar antartrial
menampilkan Berhasil/Gagal, lalu peserta menekan Mulai trial berikutnya saat siap.
Perubahan protokol ini harus dijelaskan dalam metode: wall_seconds adalah durasi nyata,
termasuk respawn, dengan menu, jeda, pembuatan peta, dan intermission dikecualikan.
Saat jatuh, pemain dikembalikan ke tile sehat terakhir yang dikunjungi, termasuk lantai atas.
Posisi aman memerlukan dukungan collision pada pusat tile dan footprint di sekitarnya;
tepi yang masih utuh pada tile defect sebagian tidak menjadi checkpoint.
Jika posisi itu sudah tidak aman, cari tile sehat terdekat pada lantai checkpoint. Peta, temuan,
dan waktu aktif tidak direset; input bergerak diberi tenggang 0.35 detik setelah pemulihan.
Durasi dan jumlah jeda dicatat sebagai pausedWallSeconds/pauseCount pada JSON serta
paused_seconds/pause_count pada Waktu_Trial.csv. Input manusia memiliki strafing;
model AI tetap memakai dua aksi forward/turn semula. Perbedaan kemampuan gerak ini
perlu dinyatakan dalam metode perbandingan; model tidak dilatih ulang.
F kini memberi umpan balik valid/tidak valid dan hanya menerima mismatch collision.
Setelah F diterima, area diberi kotak garis hijau di batas luarnya seperti panduan
sandbox. Untuk lantai, garis mengelilingi bounds permukaan tile pada full collider,
atau kotak lubang pada partial hole. Untuk tembok, garis mengelilingi proyeksi bounds
permukaan tembok yang ditandai. Bounds adalah umpan balik visual setelah temuan
diterima, bukan masukan detector atau navigasi. Penanda tetap ada setelah jatuh/respawn,
disembunyikan saat jeda/intermission, dan dibersihkan ketika trial berganti. Permukaan
sehat dan defect yang belum ditemukan tidak diberi highlight. Penanda tidak memiliki
collider. Material pada CollisionSelected/Resources ikut disertakan dalam build Unity.
Percobaan tembok memakai reason supported_wall/missing_wall_collision; findings
mencatat surfaceKind floor/wall serta normal untuk analisis jenis permukaan.
Precision dari findings yang diterima tidak mengukur seluruh penilaian mentah peserta;
gunakan markAttempts untuk menganalisis percobaan yang salah atau berulang.
simulation_seconds dan finishReason tetap dicatat untuk analisis. Perbandingan dengan AI
harus menjelaskan aturan penghentian manusia dan budget AI; waktu selesai saja tidak
membuktikan efektivitas penemuan defect. Gunakan temuan/recall dan cakupan bersama waktu.

Layar akhir langsung tampil sesudah trial kelima, berisi lima waktu, status Berhasil/Gagal, dan total.
Tombol Copy di kanan setiap waktu trial menyalin waktu trial berhasil dalam format
menit:detik (contoh 01:23.45), atau satu tanda `-` jika trial gagal, tanpa status atau
ringkasan trial lain. Waktu aktual tetap ditampilkan dan tersimpan di berkas hasil. Tombol salin gabungan
tidak lagi ditampilkan. Buka Google Form membuka
https://forms.gle/D6iTTSbCAvPstjng9 melalui browser sistem setelah peserta menekan tombol.
Tidak ada submit formulir atau unggah otomatis. Setelan URL dapat diganti tanpa rebuild.

Waktu_Trial.txt dan Waktu_Trial.csv tersimpan bersama JSON temuan dan summary.csv dalam
folder sesi lokal otomatis. CSV juga menyimpan status, found_targets, dan target_count,
sehingga durasi timeout tidak disalahartikan sebagai waktu keberhasilan. Tidak ada ID yang diminta dari peserta; field internal
participant bernilai anonymous untuk kompatibilitas alat analisis.

Smoke check build: jalankan .exe dengan `-batchmode -nographics -participantSmoke`
dan `-defectOutput <direktori baru>`. Ini hanya uji software: memulai dari menu melalui
API yang sama dengan tombol, menandai seluruh target pada dua trial pertama melalui
API aim/F, membiarkan tiga trial berikutnya gagal pada batas uji 2 detik, memastikan
waktu antartrial berhenti, dan memeriksa lima baris
ekspor serta URL Google Form. Batas 2 detik hanya aktif jika batch mode DAN flag smoke
diberikan. Penggunaan biasa memakai 150 detik dan tidak memulai otomatis.
