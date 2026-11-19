# ARSIP: memori cabang lama dengan action assistance

**DIGANTIKAN 24 Oktober 2026.** Implementasi aktif sekarang adalah sensor
observasi 32 nilai tanpa pengalihan aksi. Baca
[README_BRANCH_OBSERVATION.md](README_BRANCH_OBSERVATION.md), run Phase2C.
Instruksi hybrid di bawah merupakan riwayat dan tidak berlaku pada agent baru.

Pilot fine-tuning dari model 8 juta sudah disiapkan 24 Oktober:
[README_PHASE2B.md](README_PHASE2B.md), ID `Phase2B`, budget 2 juta step baru.
Belum otomatis dijalankan.

23 Oktober 2026. Metode **PPO + observed-branch action assistance**, bukan
kemampuan baru yang sudah dipelajari bobot model. Ini tidak menambahkan sensor
atau mengubah reward, curriculum, MaxStep, budget, prefab atau checkpoint.

## Pemakaian

- Scene Phase 2 dan Phase 3: `ExplorationAgent > Use Completed Branch Assist` ON.
  Phase 1 tetap OFF. Tidak perlu rebuild scene.
- Bantuan hanya aktif pada episode coverage. Latihan target Phase 1, lesson
  transfer naik/turun, dan Heuristic/WASD tidak diintervensi.
- Model Phase2T 8.000.127 bisa diuji pada scene sekarang dengan Inference Only,
  terrain sensor ON dan default terrain level 8 seperti sebelumnya. Sensor tetap
  117 nilai; tidak perlu mengulang Phase 1 hanya untuk fitur ini.
- Untuk membandingkan PPO murni, matikan `Use Completed Branch Assist` SEBELUM
  Play. Uji ON/OFF dengan model, seed, spawn dan anggaran episode yang sama.
- Checkpoint 8 juta/baseline tidak ditimpa. Tidak ada training otomatis.
  Jangan resume eksperimen baseline lalu menyebutnya PPO murni: perilaku
  lingkungan berubah saat bantuan ON. Gunakan run terpisah jika nanti fine-tune.

## Cara kerja dan perlindungan tangga

1. Setiap episode mulai dengan graf kosong. Graf dibangun melalui posisi yang
   benar-benar dilewati dan probe collider lokal 360 derajat, tanpa membaca
   tile/socket WFC, graph generator, tag Stair, ExplorationArea atau posisi tangga.
2. Lattice lokal berjarak 2 m, 8 arah. Node dianggap dikunjungi hanya ketika
   agent benar-benar mendekati pusatnya (0,6 m), bukan hanya karena terlihat.
   Ketinggian dibedakan per 0,25 m agar lantai berbeda tidak digabung.
   Pemindaian dilakukan tiap **0,1 detik simulasi**, disesuaikan untuk speed 6
   (sekitar 0,6 m perpindahan lurus maksimum per interval; bukan jaminan semua
   node pasti dikunjungi). Perubahan ini tidak menambah observasi model.
3. Probe memeriksa ruang tubuh dan permukaan lintasan. Dinding tegak harus
   terkonfirmasi di dua ketinggian. Void, data ambigu, buffer penuh, tangga,
   ramp dan perubahan tinggi tetap UNKNOWN, bukan dianggap tembok/buntu.
   Tidak ada sampling lantai di balik tembok untuk membuka koneksi.
4. Cabang selesai hanya jika SEMUA node dalam komponen di belakang pintu
   masuk sudah dikunjungi dan SEMUA keluarannya diketahui. Satu UNKNOWN atau
   node belum dikunjungi membatalkan status selesai. Status dihitung ulang;
   tidak ada blacklist permanen. Siklus yang terhubung ke pintu lain dari
   persimpangan dipertahankan sebagai transit.
5. Jika aksi PPO akan masuk lagi ke cabang selesai DAN ada alternatif yang
   terkonfirmasi terbuka/belum selesai, bantuan memutar lalu menggerakkan agent
   singkat menuju node alternatif. Setelah itu PPO kembali mengambil alih.
   Gerak tetap melalui CharacterController, bukan teleport. Bantuan mengecek
   ulang clearance dan kedaluwarsa maksimal 3 detik simulasi.
6. Jika semua arah selesai, tidak ada alternatif aman, atau agent perlu
   keluar dari cabang buntu, aksi PPO tidak diblokir. Melewati area lama tidak
   menambahkan penalti baru. Memori direset setiap episode/map baru.

Ini sengaja konservatif: cabang yang punya perubahan ketinggian belum akan
dipangkas, bahkan jika seluruh lantainya tampak sudah dikunjungi. Dengan begitu
rute menuju tangga tidak ditutup karena informasi kurang lengkap. Ini juga
bukan pemetaan geometri kontinu sempurna: lattice/probe terbatas dapat menyisakan
UNKNOWN dan tidak mengintervensi. Fitur ini tidak menjamin semua revisit hilang,
tidak mencari tangga secara otomatis, dan tidak memaksa coverage seimbang.
Coverage resmi tetap dihitung sistem area lama, bukan jumlah node memori.
Asumsi geometri map statis selama satu episode. Jika collider/rute berubah di
tengah episode, reset episode/memori dahulu; bukti dinding lama bisa kedaluwarsa.

## Validasi dan metrik

Menu `Tools > AI > Validate Completed Branch Memory` menjalankan pengujian
graf dan geometri sementara tanpa menyimpan scene. Batch check ada di
`TesisQA.AI.Editor.AIObservedBranchChecks.ValidateBatch` pada proyek `.ai-eval`.

TensorBoard saat trainer tersambung:

- `AI/Branch Memory Nodes`: jumlah node yang pernah diamati episode ini.
- `AI/Completed Branch Exits`: jumlah arah selesai pada lokasi scan terakhir.
- `AI/Branch Redirects Cumulative`: jumlah intervensi kumulatif episode ini,
  disampling tiap 50 physics step; **bukan** jumlah/rata-rata akhir per episode.

Bandingkan juga coverage per lantai PER EPISODE, keberhasilan naik/turun,
durasi, tabrakan dan pengulangan jalur. Node banyak bukan bukti memori berhasil;
nilai redirect selalu nol bisa berarti cabang belum terkonfirmasi, bukan bug.
Uji unit/geometri tidak membuktikan model lebih baik pada WFC sesungguhnya.
Belum ada evaluasi model paired atau klaim siap Phase 3 untuk perubahan ini.
Batch Unity 6000.5.4f1 pada 23 Oktober: exit code 0, `AI BRANCH CHECKS PASSED`
di `Logs/branch-memory-checks.log`. Semua 28 tes Python driver training lolos.

Untuk tesis tulis eksplisit bahwa memori graf dan pengalihan aksi adalah
algoritme tambahan, bukan murni PPO/LSTM/curiosity. Sensor masukan policy tidak
berubah, tetapi aksi aktual sesekali berbeda dari keluaran policy. Training
dengan assistance harus dievaluasi dengan konfigurasi assistance yang sama.
