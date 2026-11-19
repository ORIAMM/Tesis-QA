# Pengujian defect collision

**Untuk peserta manusia, gunakan build game baru:** `Builds/ColliderQuest/Mulai_Game.cmd`,
atau bagikan `Builds/ColliderQuest_Windows.zip`. Menu ini tidak meminta ID, menjalankan
lima trial, menampilkan lima waktu dan total, serta membuka Google Form setelah selesai.
Panduan lengkap ada di `PARTICIPANT_GAME.md` dan `Builds/ColliderQuest/BACA_DULU.txt`.
Peserta bisa mengakhiri trial sendiri dengan batas 150 detik waktu nyata.
Source/scene peserta terbaru memakai trial progresif **1,2,3,4,5 defect**, WASD/mouse,
Canvas responsif, penandaan F yang memeriksa collision, dan pemulihan ke pijakan aman
terakhir. Preview lalu build ulang dari Unity; build/ZIP lama belum memuat perubahan ini.

Alur di bawah ini adalah scene/build peneliti lama dengan pengaturan Manual/AI/Random.

Model: **Phase3R, checkpoint 2.000.063**. Tidak perlu menjalankan trainer.

1. Buka `Assets/Scenes/AI/QA_CollisionExperiment.unity`, lalu tekan **Play**. Untuk aplikasi Windows, buka `Builds/CollisionPilot/Jalankan_Pengujian.cmd`.
2. Pilih **Manual**, **AI**, atau **Random**.
3. Untuk manusia, isi ID peserta seperti P001 dan urutan 0–9. Bagikan urutan secara seimbang antar peserta.
4. Klik **Mulai sesi**. Semua mode menggunakan lima skenario tetap dengan map, spawn, collision, dan lokasi defect yang sama.

Kontrol manual: **W/S** maju/mundur, **A/D** belok, **↑/↓** mengatur pandangan, **F** melaporkan lantai yang dibidik tanda `+` dalam jarak enam meter. Tidak ada penanda lokasi defect atau umpan balik benar/salah selama permainan.

Setiap trial berlangsung **150 detik simulasi**. Manual terdiri atas lima trial; klik **Trial berikutnya** setelah waktu habis. AI dan Random menjalankan tiga pengulangan tiap skenario secara otomatis. Jika jatuh, karakter kembali ke spawn awal tanpa mengganti map atau menghapus temuan trial.

Hasil dari Editor disimpan di `results/DefectExperiments/<sesi>/`. Hasil aplikasi Windows disimpan di folder `results/DefectExperiments/` di sebelah aplikasi. Baca `summary.csv` untuk ringkasan dan JSON setiap trial untuk rincian. Pengujian yang dihentikan sebelum selesai ditandai `interrupted`.

Skenario ini merupakan **pilot**. Lokasi injection diketahui sistem penilaian, tetapi tidak diberikan kepada model atau detector. Detector memeriksa kehilangan collision pada lantai datar; tangga memakai collider ramp yang sengaja berbeda dari bentuk visual dan dikecualikan dari pemeriksaan.

Pengujian peserta manusia tetap harus dilakukan langsung. Pemeriksaan otomatis alur manual bukan data responden. Gunakan sesi latihan terpisah sebelum sesi penelitian dan simpan skenario baru yang belum digunakan untuk pengujian konfirmatori.

Penjelasan teknis dan definisi metrik ada di `Assets/Scripts/AI/Defects/README_COLLISION_EXPERIMENT.md`.
