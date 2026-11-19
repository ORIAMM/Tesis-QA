# Phase2M4: latihan tangga bertahap, 17 Oktober 2026

**Update 18 Oktober: lesson 2 juga dibuat bertahap.** Lihat
[README_NEAR_SEARCH.md](README_NEAR_SEARCH.md) untuk profil jarak/yaw dan
window mastery baru. Tetap resume run yang sama; konfigurasi resume terbaru
memakai budget 15 juta (update 19 Oktober) dan time scale 40x. Budget/time scale di uraian berikut
merupakan riwayat konfigurasi, bukan pengaturan resume terbaru.

Update 18 Oktober: budget resume dinaikkan menjadi 7 juta step kumulatif dari
checkpoint 5.000.102, sesuai permintaan untuk mengamati tambahan training.
Lesson, reward, orientasi spawn, MaxStep, dan time scale tidak diubah.
Angka 5 juta di uraian awal berikut adalah budget sebelum perpanjangan ini.

Lanjutkan checkpoint step 3.000.090. Tidak perlu mengulang Phase 1 atau
menghapus kemampuan eksplorasi yang sudah didapat. Collider, prefab, offset
tangga, scene utama, jumlah input dan jumlah aksi tidak diubah.

Stop Play Mode, tunggu kompilasi Unity selesai, lalu jalankan dari root proyek:

```powershell
uv run python Assets/Scripts/AI/Training/train_phase2.py Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch_Resume.yaml --run-id=Phase2M4 --resume
```

Buka AI_Phase2_SmallWFC, Behavior Type Default, Model kosong, lalu Play setelah
terminal menunggu koneksi. Jangan --force. Budget resume menjadi total 5 juta
step kumulatif (sekitar 2 juta tambahan), bukan 5 juta step baru. Time scale 40
dan MaxStep 4000/6000 tetap. Training utama tidak dijalankan otomatis.

## Lesson 1 mempunyai tiga tingkat latihan

| Level latihan | Spawn/yaw | Petunjuk arah |
| --- | --- | --- |
| 0 | Tile mulut tangga, bergeser 0,30 cell mendekati kaki ramp; yaw tepat menuju tangga | Seluruh episode |
| 1 | Tile mulut tangga, bergeser 0,15 cell; yaw acak +/-15 derajat | 50% episode |
| 2 | Spawn normal 0–1 tile; yaw acak +/-30 derajat | Tidak ada |

Setiap level memerlukan 100 episode terakhir, minimal 75 percobaan naik,
dan keberhasilan naik >=80%. Window dikosongkan saat level berganti. Hanya
setelah level 2 TANPA petunjuk lolos, main lesson maju ke lesson 2 (pencarian
dekat naik/turun), kemudian lesson 3 (campuran jarak). Petunjuk tidak
menggerakkan agent secara otomatis: policy tetap memilih maju/mundur dan belok.

Petunjuk memakai slot arah/visibility yang memang sudah ada, seperti format
goal pada Phase 1. Tidak ada sensor/tag tangga baru. Ini bantuan training yang
di-fade, bukan kemampuan menemukan tangga mandiri; keberhasilan level berpemandu
tidak dijadikan bukti penguasaan pencarian tanpa bantuan.

Saat pertama resume dari run 3 juta step, window 100 kegagalan lesson 1 lama
dikosongkan karena tugasnya berubah. Bobot policy, optimizer, step, dan main
lesson 1 tetap dipertahankan. State level/window baru disimpan sebagai
phase2_stair_practice dalam training_status.json. Resume berikutnya memuat
level/window yang sama, tidak selalu kembali ke level 0.

## Reward dan pengingat kemampuan

Dengan parameter stair_practice=1, jarak shaping mengikuti rute hingga lantai
tujuan, termasuk bagian vertikal tangga; tidak berhenti di mulut/trigger tangga.
PBRS masih memakai koreksi terminal yang sama.

Bonus auxiliary tambahan maksimal +0,30 per episode hanya dibayarkan saat
rekor progres di tangga terpilih meningkat. Agent harus grounded, pernah masuk
trigger tangga, berada pada sel rute terpilih dan dekat centerline; progres
membutuhkan perpindahan horizontal DAN perubahan ketinggian ke arah tujuan.
Gerakan di bawah lantai atas, di samping rute, jatuh/airborne, atau naik-turun
ke ketinggian yang sudah dicapai tidak mendapat bonus baru. Bonus mendukung
naik dan turun secara simetris, tetapi tidak mengisi ulang budget saat revisit.
Reward milestone dan reward sukses yang lama tetap berlaku.

Khusus latihan lesson 1, pendekatan ke tangga juga mendapat bonus rekor
maksimal +0,10 per episode. Mundur lalu mendekat sampai titik lama tidak
membayar ulang. Reward novelty tile/bonus area lantai atas dimatikan hanya
pada latihan lesson 1 agar mencari koridor lain tidak menjadi alternatif
reward yang lebih mudah. Reward eksplorasi lesson 0, 2/3 dan coverage tetap.

Parameter ini mengubah reward training dan arti jarak shaping, sehingga angka
reward/jarak sebelum dan sesudah perubahan tidak langsung sebanding. Jelaskan
auxiliary reward dan bantuan arah sebagai scaffolding training dalam tesis.

Untuk run baru dari Phase1M, main lesson 0 menyisipkan 5 episode latihan tangga
berpemandu per blok 20 episode, agar latihan satu lantai tidak meninggalkan
kemampuan tangga terlalu lama. Main lesson 2/3 menyisipkan 4 episode latihan
pengingat per blok 20. Episode pengingat dikecualikan dari mastery main lesson
yang sedang dinilai; ia tidak dapat menaikkan lesson pencarian tanpa bantuan.
Perhatikan AI/Main Lesson atau AI/Mastery/lesson untuk tahap utama, bukan hanya
rata-rata AI/Lesson (yang juga mencakup episode pengingat).

Evaluasi held-out biasa tidak mengaktifkan stair_practice: tanpa petunjuk,
tanpa bonus auxiliary ini. Gerbang evaluasi tangga tetap wajib sebelum coverage
dua lantai. Practice tidak aktif pada lesson coverage 4/5.

## Grafik yang diperiksa

- AI/Practice/level, AI/Practice/episodes, AI/Practice/up_success: level dan
  window aktual pada trainer (tersimpan untuk resume).
- AI/Stair Practice Guided: porsi episode yang memakai petunjuk.
- AI/Stair Climb Progress dan AI/Stair Climb Reward: progres terbaik dan bonus.
- AI/Stair Approach Reward: bonus pendekatan, maksimal +0,10 per episode.
- AI/Stair Rehearsal, AI/Stair Rehearsal Success: frekuensi/keberhasilan pengingat.
- AI/Stair Ascent Success, AI/Stair Descent Success, AI/Ascent Transitions,
  AI/Descent Transitions: tetap periksa perpindahan lantai yang sesungguhnya.

Cadangan sebelum perubahan:
results/Phase2M4/before_stair_practice_20261017 (checkpoint, status, konfigurasi).
File checkpoint utama dan status lama tidak ditimpa oleh perubahan kode ini;
keduanya baru diperbarui oleh trainer ketika pengguna memulai resume.

Periksa lagi sekitar step 3,1–3,2 juta, terutama progres naik dan success level
0. Jika keduanya tetap nol, hentikan untuk diagnosis perilaku; jangan menunggu
sampai 5 juta hanya karena budget tersedia. Lolos uji implementasi tidak
menjamin policy langsung naik tangga sebelum mendapat pembaruan training.
