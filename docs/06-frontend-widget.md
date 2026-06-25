# 06 — Frontend widget (web/widget)

Vložitelný chat widget v **TypeScriptu, bez frameworku** (vanilla + malý CSS), zabalený do jednoho IIFE bundlu. Cíl: malá velikost, snadné vložení do libovolného webu jedním `<script>` tagem.

## Vizuální identita (zvoleno)

- **Styl hlavičky:** světlá varianta — bílá/světlá hlavička, barevné jen logo, prvky a tlačítka. Vzdušné, sedí k optimistické identitě ENERKOM.
- **Barvy:** zelenotyrkysová (`#0F6E56` tmavá, `#1D9E75` střední, `#5DCAA5` světlá, `#E1F5EE` pozadí bubliny uživatele), sluneční akcent `#EF9F27` volitelně. (Pozn.: pokud dorazí přesné HEX z grafického manuálu, nahradit.)
- **Tón:** přátelské vykání, krátké a vstřícné odpovědi, lze 1 emoji.
- **Maskot:** **Elektron** — klučičí superhrdina v zeleném obleku se žlutým bleskem (postava z firemního videa).

## Maskot Elektron — asset a animace

- **Zdroj:** `Elektron.mp4` (firemní spot), postava nejlépe viditelná na bílém pozadí kolem času **~1:00**.
- **Rozhodnutí: používá se CELÁ postavička, ne kulatý výřez obličeje** (kruhový výřez ořezával bradu — vypadalo to špatně). Elektron stojí celý jak v launcheru, tak v hlavičce.
- **Vyseknuté assety** (ffmpeg, uloženo v repu):
  - `web/widget/assets/elektron-figure.png` — **hlavní asset**: celá postava (hlava + trup + stehna), celý obličej. Zobrazuje se přes `background-size: contain; background-position: bottom center` → nic se neořezává.
  - `web/widget/assets/elektron-figure-tall.png` — variant s o něco větším výřezem.
  - `web/widget/assets/elektron-body.png`, `elektron-avatar.png` — starší výřezy (ponechány, ale avatar se nepoužívá kvůli oříznuté bradě).
- **Animace (implementováno): cut-out Lottie.** Bez designéra — postava se nepřekreslovala do vektoru, místo toho se z čistého výřezu složil **pravý Lottie soubor z vrstev původní grafiky** (PNG částí), které se animují transformacemi. Zachová reálnou grafiku Elektrona, hraje přes `lottie-web`, stavy řídí chat.
  - **Pipeline (skriptovaná, reprodukovatelná) — 2 skripty:**
    1. **`web/widget/build_assets.py`** (z `mascot-frames/elektron_full.png`, jedna soudržná souřadná soustava):
       - odstranění pozadí **fixed-range flood-fillem** z okrajů (OpenCV) → průhledný výřez celé postavy (bílá, šedá patička, **žlutý roh paprsku**); 2× upscale,
       - odstranění **uzavřených bílých kapes pozadí pod obličejem** (mezery mezi/vedle nohou) + stažení matte o 1 px (bez bílého lemu); bílá v obličeji (oči, zuby) zůstává,
       - detekce očí (zelené duhovky) → **víčka pro mrkání**; detekce pusy → **„zavřená pusa" overlay** (feathered pleťová záplata + linka rtů) pro lip-sync,
       - zapíše `rig.json` (W,H, eyes, skin, lids, mouth_lid).
    2. **`web/widget/build_lottie.py`** poskládá soběstačný **`assets/elektron.json`** (~216 KB, PNG vložené base64): rig-null (pohyby těla), figura (dýchání scaleY), víčka (mrkání), pusa (lip-sync). Geometrie i rastr zmenšeny faktorem 0.55. Rebuild: `python build_assets.py && python build_lottie.py`.
  - **Rig:** jedna postava + oddělená víčka + overlay pusy. Pohyby těla (náklon/kývnutí/zavrtění/houpání) řídí rig-null; „dýchání" je scaleY figury; mrkání je scaleY víček; **mluvení** je skokové (hold-keyframes) zap./vyp. overlaye „zavřené pusy" nad otevřeným úsměvem. Pohyb je „loutkový" (transformace vrstev) — pro chat widget plně dostačuje.
- **Stavy maskota** (segmenty v jednom Lottie, přepínané přes `playSegments`):
  - *klid (idle)* `[0,90]` — dýchání + mrkání + jemné houpání,
  - *přemýšlí* `[90,195]` — náklon hlavy + pohup + **myšlenková bublina s pulzujícími tečkami nad hlavou** (vektorové shapes přímo v Lottie; kompozice má proto navíc horní/pravý okraj `PAD_T/PAD_R`). V chatu navíc text „Elektron přemýšlí…" s CSS tečkami,
  - *mluví* `[195,285]` — kývání hlavou + **lip-sync** (pusa se otevírá/zavírá) během streamu odpovědi,
  - *nenašel odpověď* `[285,360]` — zavrtění hlavou „ne", pak zpět do idle; + omluvný text + kontakt.
- **Integrace:** `web/widget/src/mascot.ts` — třída `Mascot` (load Lottie + `set("idle"|"thinking"|"talking"|"notfound")`, segmenty čte z `meta` v JSONu, `notfound` se přehraje jednou a vrátí do idle).
- **Náhledy** (statický server: `python -m http.server 8123 --directory web/widget`):
  - `preview.html` — izolovaný náhled 4 stavů (tlačítka) — ověřeno, že renderují a živě hrají,
  - `demo.html` — **kompletní chat widget shell** (plovoucí launcher + panel, maskot v hlavičce, mock backend): stavy řízené průběhem konverzace (send → *přemýšlí*, stream → *mluví*, off-topic → *nenašel* → *klid*). Ověřeno přes skriptované evaly.
  - Pozn.: launcher používá **statický** obrázek hlavy (ne druhá živá Lottie) a Lottie se pozastaví při zavřeném panelu — kvůli výkonu.
- **Možná rozšíření:** víc tvarů pusy pro bohatší lip-sync; pro plně vektorový (neomezeně škálovatelný) Lottie by bylo nutné postavu překreslit — vyžaduje designéra.

> Pozn.: `goToAndStop(globalFrame)` po `playSegments` adresuje snímky **relativně k segmentu** — maskota řiďte výhradně přes `Mascot.set()`, ne přímým skokem na globální snímek.

## Integrace na web (Webnode)

Web `www.enerkomhp.cz` běží na **Webnode**. Widget se vkládá jako jeden `<script>` tag — `widget.js` i API jsou hostované na Azure:

```html
<!-- Webnode: vlastní kód do hlavičky/patičky (globálně), nebo HTML blok na stránce -->
<script
  src="https://<azure-api-host>/widget.js"
  data-api-url="https://<azure-api-host>/api/chat"
  data-org-name="Enerkom HP"
  data-primary-color="#2b7a4b"
  defer></script>
```

**Způsob vložení (potvrzeno):** Webnode plán umožňuje **vlastní kód do hlavičky (`<head>`)** → script se vloží globálně a widget je automaticky na všech stránkách. Žádné per-stránku řešení ani fallback není potřeba.

Administrace Webnode: vlastní HTML/kód do hlavičky webu → vložit výše uvedený `<script>` tag (jednou, platí pro celý web).

- Skript si sám vytvoří plovoucí tlačítko (chat bublina) v rohu stránky.
- Konfigurace přes `data-*` atributy → žádná editace kódu na straně webu.
- Žádné externí závislosti (žádné CDN frameworky) → neovlivní hostitelský web.

## Chování (UX)

1. **Plovoucí tlačítko** vpravo dole → otevře panel chatu.
2. **Úvodní zpráva:** krátké přivítání („Ahoj, zeptej se mě na cokoli o naší organizaci.").
3. Uživatel napíše dotaz → odešle (Enter / tlačítko).
4. Zobrazí se „píše…" indikátor; odpověď se **streamuje** token po tokenu (SSE).
5. Pod odpovědí **citace** jako klikatelné odkazy (`[1] Pro dobrovolníky` → otevře `uri` v novém tabu).
6. Drží **historii** konverzace v paměti (pole zpráv) a posílá ji v `history` (ořezanou na posledních ~6).
7. Při chybě (429/503) zobrazí přívětivou hlášku z odpovědi, ne technickou chybu.

## Komunikace s API

- `fetch` na `data-api-url` (`POST /api/chat`), `Content-Type: application/json`.
- **Streamování:** číst `ReadableStream` z odpovědi, parsovat SSE události (`token`, `sources`, `done`).
  - Fallback: pokud stream selže / není podporován, použít `?stream=false` a vykreslit JSON odpověď najednou.
- Tělo požadavku: `{ question, history }`.

## Stav a model (TS)

```ts
type Role = "user" | "assistant";
interface Message { role: Role; content: string; }
interface Source { title: string; uri: string; type: string; }

interface WidgetConfig {
  apiUrl: string;
  orgName: string;
  primaryColor?: string;
}
```

## Styl a izolace

- Veškerý CSS **scoped** pod root prvek widgetu s prefixem tříd (`.enerkom-chat-…`) nebo v **Shadow DOM** (preferováno) — aby styly webu neovlivnily widget a naopak.
- Responsivní: na mobilu panel přes celou šířku.
- Přístupnost: focus management, `aria-label` na tlačítkách, klávesnice (Esc zavře, Enter odešle).

## Build

- **Vite** v knihovním režimu (`build.lib`) → výstup `widget.js` (IIFE, self-contained), volitelně minifikovaný.
- Žádný hash v názvu (stabilní URL pro `<script src>`), verzování přes query (`?v=1`).
- Výstup nasadit jako statický soubor (viz 07 — může hostit i `EnerkomChatbot.Api` ze `wwwroot`, nebo CDN/static storage).

## Bezpečnost

- Widget **nikdy** nedrží API klíč — jen volá náš endpoint.
- Sanitizovat/escapovat text odpovědi i citací před vložením do DOM (zabránit XSS); pokud renderujeme markdown, použít bezpečný renderer s escapováním HTML.

## Akceptační kritéria

- Vložení jedním `<script>` tagem funguje na statickém i CMS webu.
- Odpověď se streamuje, citace jsou klikatelné.
- Widget neovlivní vzhled hostitelského webu (Shadow DOM / scoping ověřen).
- Chybové stavy zobrazí přívětivou hlášku.
