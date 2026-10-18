# Dış mekan kiti (Blender kaynağı)

Dış saha (sefer) manzarasının parçaları Blender'da kodla üretilir ve `Assets/Models/Outdoor` içine FBX olarak aktarılır.

## Yeniden üretmek
1. `python make_textures.py` — dokuları `Assets/Models/Outdoor/Textures` içine yazar (numpy + Pillow gerekir).
2. Blender'da (Scripting sekmesi) sırayla çalıştır: `kit_core.py`, `kit_ground_cars.py`, `kit_buildings.py`, `kit_props.py`, `kit_export.py`
   ve ardından:
   ```python
   items = build_everything()
   export_all(items)
   ```
   `kit_core.py` içindeki `PROJ` yolu kendi proje klasörüne göre düzenlenmeli.
3. Unity'ye dön: kit kendiliğinden yeniden kurulur (Aftershock → Dış Mekan Kitini Kur ile elle de çalıştırılır).

## Yeni parça eklemek
`kit_props.py` ya da `kit_ground_cars.py` içine bir fonksiyon yaz, `kit_export.py` → `build_everything()` içinde `add(obj, "kategori", zbias)` ile kaydet.
Kategoriler: ground, curb, groundback, car, van, pickup, barrier, sandbags, dumpster, building, skyline, prop, debris.
Binaların cephesi -Y'ye bakar; `zbias = -1` Unity tarafında cephenin kameraya dönmesini sağlar.
Parçalar yalnızca 7 malzeme kullanır: pal (renk paleti), asphalt, sidewalk, concrete, brick, plaster_beige, plaster_grey.
