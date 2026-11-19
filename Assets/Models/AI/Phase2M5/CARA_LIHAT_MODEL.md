# Model Phase2M5 untuk ditonton

Terpilih dari checkpoint yang diuji: Phase2M5_2749901.
Model akhir: Phase2M5_3000013.

1. Stop Play dan jangan jalankan trainer untuk menonton model.
2. Buka AI_Phase2_SmallWFC, lalu File > Save As ke scene salinan, misalnya
   AI_Phase2M5_View. Jangan menyimpan pengaturan preview ke scene training asli.
3. Pada AIWFCTrainingAdapter di scene salinan, aktifkan Use Floor Count Curriculum,
   Use Floor Transfer Lesson, dan Default Graduated Transfer Lessons. Ubah
   Default Floor Lesson menjadi 3. Ini penting: scene asli masih default lesson 0
   dan graduated transfer nonaktif, sehingga drag model saja akan menampilkan task berbeda.
4. Pada Agent > Behavior Parameters, pasang Phase2M5_2749901.onnx, lalu pilih
   Behavior Type = Inference Only. Jangan Heuristic Only. Model akhir
   Phase2M5_3000013.onnx juga tersedia untuk dibandingkan.
5. Play. Tidak membutuhkan command Python. Pengaturan di atas menampilkan
   pencarian tangga campuran dekat/jauh dua lantai; bukan evaluasi coverage panjang.

Spawn/map preview tidak identik dengan protokol fixed-seed. Hasil visual tidak
menggantikan evaluasi berpasangan, dan model belum menguasai seluruh curriculum.

Laporan: C:\Users\riova\OneDrive\Documents\Proposal Tesis S2\Unity\Tesis QA\results\Phase2M5\headless_evaluation\HASIL_EVALUASI.md
Model terpilih belum berarti lulus Phase 3; periksa hasil naik/turun dan coverage per lantai.
