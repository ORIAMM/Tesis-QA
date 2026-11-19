# Sandbox collision kecil

Buka `Assets/Scenes/AI/QA_CollisionSandbox.unity` lalu tekan Play dan pilih tab Game.
Menu Editor: **Tesis QA > Defect Experiment > Open small collision sandbox**.
Map WFC 6x2x6 sudah tersedia untuk dilihat di Scene view dan digenerate ulang saat Play
dengan seed/manifest yang sama. Sandbox ini terpisah dari lima trial peserta dan tidak
membuat data responden, timer studi, formulir, atau proses AI/training.

Kendali: WASD bergerak, Space melompat melewati celah, mouse melihat, F memeriksa/menandai lantai, Esc melepas kursor,
klik kiri untuk kembali bermain, R kembali ke pijakan awal yang aman.

- **1:** langsung dekat defect collider penuh, di lantai bawah. Seluruh collider mesh
  lantai yang dipilih dimatikan. Kotak merah membantu menunjukkan area demonstrasi.
- **2:** langsung dekat defect sebagian, di lantai atas. Hanya daerah collision 2,4x2,4 m
  yang dilubangi. Bagian lantai di luar kotak oranye tetap dapat diinjak. Ini mode awal.
- **3:** pulihkan collider pada kedua defect dan kembali ke pijakan awal mode terpilih.
  Bentuk visual tetap sama, sehingga dapat dibandingkan dengan mode 1 atau 2.
- **H:** tampilkan/sembunyikan garis bantu. Garis bantu tidak memiliki collider dan
  hanya dipakai untuk belajar mengenali perilaku defect, bukan pengujian peserta.

Coba mode 2: injak bagian luar kotak terlebih dahulu, lalu berjalan maju ke dalamnya.
Pemain akan jatuh menembus lantai yang tampak utuh dan dikembalikan ke tile sehat
terakhir yang dikunjungi sebelum jatuh. Posisi dan lantai checkpoint mengikuti
perjalanan pemain. Bagian tepi yang masih utuh pada tile defect sebagian tidak
dijadikan checkpoint. Jika belum mengunjungi tile sehat setelah teleport tombol 1/2,
gunakan tile sehat terdekat di lantai demo tersebut. R tetap reset manual ke posisi demo.
Pada mode 1, tembok/railing yang menyatu dengan mesh collider penuh juga kehilangan
collision. F sekarang bisa memeriksa permukaan tembok tersebut; mode 3 memulihkan
collision lantai dan tembok bersama-sama. AI memakai pemeriksaan tembok yang sama
melalui detector eksternal di scene QA_LinearCollisionExperiment.
Kemudian tekan 3 dan lakukan lintasan yang sama: collider yang dipulihkan akan menahan
pemain. Tekan H agar perbandingan tidak dibantu garis. F menerima lantai dengan collision
yang hilang dan menolak lantai sehat. Defect bersifat fisik; mesh visual tidak dilubangi.

Efek yang dirasakan melalui kontrol adalah hilangnya pijakan, gerakan jatuh, dan
pemulihan posisi. Outline hanya panduan demo.
Jika Unity meminta reload scene setelah file berubah, muat ulang scene sandbox.

PreviewCollisionMeshes.asset menyimpan mesh physics untuk preview Edit Mode. Runtime
menggunakan jalur injection dan pustaka mesh yang sama dengan eksperimen utama.
Manifest.json mencatat dua defect dan seed map demo. Uji otomatis menggunakan input W
dan CharacterController nyata untuk memastikan kedua defect menyebabkan jatuh/respawn,
sedangkan mode sehat menahan pemain; ini bukan data peserta manusia.
