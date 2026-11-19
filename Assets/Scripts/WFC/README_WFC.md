# WFC 3D - Kenney Wide Corridor

Generator ini hanya memakai tujuh jenis tile:

- `Wide_Void` sebagai pengisi kosong tanpa GameObject;
- `Wide_Corridor_Straight` dengan dua bukaan;
- `Wide_Corridor_Corner` dengan dua bukaan;
- `Wide_Corridor_End` dengan satu bukaan;
- `Wide_Corridor_Junction` dengan tiga bukaan;
- `Wide_Corridor_Intersection` dengan empat bukaan;
- `Wide_Stairs` untuk menghubungkan dua lantai.

Tidak ada mode room, preset, fixed module, atau pengaturan distribusi hallway.

## Setup

1. Tunggu Unity selesai compile.
2. Jalankan **Tools > WFC > Create or Update Kenney Wide Corridor Tile Set** satu kali.
3. Buat GameObject kosong, lalu tambahkan komponen `WFCGenerator`.
4. Isi **Tile Set** dengan
   `Assets/WFC/Kenney Wide Corridors/TS_Kenney_Wide_Corridors.asset`.
5. Gunakan **Cell Size `(8, 4, 8)`**.
6. Atur ukuran map melalui **Grid Size**, lalu tekan **Generate WFC**.

Menu setup aman dijalankan ulang. Menu tersebut memperbaiki referensi model, arah socket, rotasi visual,
dan menandai `TD_Wide_Corridor_End` sebagai Boundary End Cap.

## Aturan batas map

Sel pada perimeter horizontal hanya boleh berisi:

- `Wide_Void`; atau
- `Wide_Corridor_End` dengan satu-satunya bukaan menghadap ke dalam map.

Straight, Corner, Junction, Intersection, dan Stairs tidak boleh menembus batas. Field
**Boundary End Count Range** mengatur jumlah End pada perimeter: X adalah minimum dan Y maksimum.
Nilai bawaan `(2, 8)` berarti hasil mempunyai minimal dua dan maksimal delapan pintu akhir di batas.
End hanya muncul pada perimeter. Pembatasan ini mencegah banyak jaringan kecil berujung End terbentuk
di interior lalu berubah menjadi lubang saat konektivitas diperiksa.

## Konektivitas

WFC mencocokkan socket `WideWalkway` dengan `WideWalkway` dan sisi `Closed` dengan `Closed`.
Karena itu bukaan visual tidak boleh menghadap dinding atau Void. Dua sisi dinding boleh bersebelahan;
hal ini diperlukan agar modular tile dapat tersusun rapat tanpa diselingi Void.

Sesudah collapse, generator mencari seluruh jaringan koridor. Jaringan terbesar dipertahankan,
sedangkan pulau koridor lain otomatis diubah menjadi `Wide_Void`. Karena itu hasil akhir tidak
memiliki beberapa kelompok koridor yang saling terputus dan tidak perlu ditolak berulang kali.

## Tangga

Dengan **Grid Size Y = 1**, `Wide_Stairs` otomatis tidak dapat dipasang. Gunakan Y minimal `2`
untuk map bertingkat. Tangga memakai volume logis `(1, 2, 1)`, dengan koneksi bawah menghadap South
dan koneksi atas menghadap North. Prefab tangga memakai offset lokal `Z = 2`; offset tersebut ikut
diputar otomatis bersama orientasi tile WFC.

## Weight tile

Frekuensi bentuk diubah langsung pada asset Tile Definition, bukan pada generator. Nilai awal:

| Tile | Weight |
|---|---:|
| Void | 2 |
| Straight | 5 |
| Corner | 4 |
| End | 0.12 |
| Junction | 1.5 |
| Intersection | 0.75 |
| Stairs | 0.15 |

Naikkan weight untuk membuat suatu bentuk lebih sering muncul. Weight End sengaja kecil karena End
hanya bersaing dengan Void di sepanjang perimeter; nilai terlalu besar akan melewati batas maksimum
End. Bila koridor terlalu padat, naikkan weight Void. Bila terlalu kosong, turunkan weight Void secara
bertahap.

## Troubleshooting

- **Tile Set belum diisi**: drag `TS_Kenney_Wide_Corridors` ke Inspector generator.
- **Boundary End Cap belum ditandai**: jalankan ulang menu Create or Update, atau aktifkan
  `Is Boundary End Cap` hanya pada `TD_Wide_Corridor_End`.
- **Generation masih gagal**: pastikan semua tile menggunakan `Default Outer Socket = Closed` dan
  bukaan menggunakan `WideWalkway`, lalu jalankan ulang menu setup.
- **Model tidak sejajar**: pastikan Cell Size tetap `(8, 4, 8)` dan scale GameObject generator `(1,1,1)`.
- **Tangga tidak muncul**: gunakan Grid Size Y minimal 2; weight tangga memang dibuat kecil.
