# Varianty maskota Elektron

Každá `varianta_N.html` je **samostatný zmrazený snímek** widgetu (vše inline — Lottie, animace, obrázky). Otevírá se dvojklikem, je nezávislá na pozdějších změnách assetů/skriptů → ideální pro porovnání variant vedle sebe.

## varianta_1 (2026-06-25)
Cut-out Lottie maskot v chat widgetu:
- **Launcher** (tlačítko vpravo dole): celá hlava Elektrona v kolečku (statická).
- **Hlavička panelu**: celý animovaný maskot (stojí dole).
- **Stavy řízené konverzací**:
  - *klid* — dýchání, mrkání, jemné houpání,
  - *přemýšlí* — náklon hlavy + **myšlenková bublina s pulzujícími tečkami nad hlavou**,
  - *mluví* — kývání hlavou + lip-sync (otevírání pusy),
  - *nenašel* — zavrtění hlavou „ne".
- Mock backend (žádné API), doba „hledání" ~2,2 s ať je vidět přemýšlení.

## varianta_2 (2026-06-25)
Maskot ve **vlastní zelené scéně vedle konverzace** (podklad: `../../../elektron-navrh.html`),
fonty Baloo 2 + Nunito. Větší citový rejstřík než varianta 1.
- **Sbalený widget** (launcher vpravo dole): hlava Elektrona v kolečku + „Zeptejte se Elektrona".
  Klik otevře panel s pop-in animací, křížek panel zase sbalí.
- **Panel**: vlevo zelená scéna s celým maskotem (mrkání, status pill), vpravo chat s bublinami.
- **Stavy řízené konverzací**:
  - *klid* — dýchání + mrkání,
  - *mává* — při otevření, bublina „Ahoj!",
  - *přemýšlí* — náklon hlavy + myšlenková bublina nad hlavou + tečky v chatu (~2,2 s),
  - *mluví* — houpání tělem,
  - *raduje se* — výskok + **konfety** (na poděkování),
  - *zmatený / nenašel* — kolébání + bublina „?".
- Mock backend (žádné API) s pár tématickými odpověďmi o energetické komunitě.

### Sestavení
`varianta_2.html` se generuje ze šablony + assetů vytažených z bundlu:
```
python build_variant2.py
```
Skript dekomprimuje maskot (PNG) a font subsety (woff2, latin + latin-ext) z
`elektron-navrh.html` a vloží je inline jako data-URI do `varianta_2.template.html`.
Šablonu uprav, skript znovu spusť. Výsledek je opět samostatný, offline, na dvojklik.
