// EnerkomChatbot chat widget — embeddable IIFE entry (variant 2). Auto-initialises on load.
//
// Embed:
//   <script src=".../widget.js"
//           data-api-url="https://.../api/chat"
//           data-org-name="Enerkom HP" defer></script>
//
// Variant 2: the Elektron mascot lives in its own green stage beside the chat and reacts to
// the conversation (waves on open, thinks, talks, celebrates with confetti, looks confused when
// it has no answer). Mascot is a single PNG animated with CSS — no Lottie, smaller bundle.
// Everything (mascot PNG + Baloo 2/Nunito fonts + CSS) is inlined; the widget renders inside a
// Shadow DOM so host-page styles neither leak in nor out, and it never holds an API key.

import { buildCss } from "./styles";
import { ChatApiError, GENERIC_ERROR, sendChat } from "./api";
import type { Message, Source, WidgetConfig } from "./types";

// Inlined as base64 data: URIs at build time (no external requests).
import mascotUrl from "../assets/elektron-mascot.png?inline";
import balooLatin from "../assets/fonts/baloo2-latin.woff2?inline";
import balooExt from "../assets/fonts/baloo2-latinext.woff2?inline";
import nunitoLatin from "../assets/fonts/nunito-latin.woff2?inline";
import nunitoExt from "../assets/fonts/nunito-latinext.woff2?inline";

type State = "idle" | "thinking" | "talking" | "happy" | "wave" | "confused";

// Persisted across page navigations (multi-page host site reloads the widget on each page).
// sessionStorage scope: survives same-tab navigation, clears when the tab closes — keeps the
// conversation alive while browsing without long-term storage of (potentially personal) text.
const STORE_KEY = "enerkom-chat-v1";

type TurnKind = "user" | "bot" | "error";

interface PersistedTurn {
  kind: TurnKind;
  text: string;
  sources?: Source[];
}

interface PersistedState {
  open: boolean;
  greeted: boolean;
  turns: PersistedTurn[];
  history: Message[];
}

function loadState(): PersistedState | null {
  try {
    const raw = window.sessionStorage.getItem(STORE_KEY);
    if (!raw) return null;
    const data = JSON.parse(raw) as PersistedState;
    if (!data || !Array.isArray(data.turns) || !Array.isArray(data.history)) return null;
    return data;
  } catch {
    return null; // storage disabled (private mode) or corrupt — start fresh
  }
}

function saveState(state: PersistedState): void {
  try {
    window.sessionStorage.setItem(STORE_KEY, JSON.stringify(state));
  } catch {
    // storage unavailable/full — degrade gracefully to per-page conversation
  }
}

const GREETING = "Dobrý den, jsem Elektron. Zeptejte se mě na cokoli o naší organizaci ⚡";
const NOT_FOUND =
  "To bohužel přesně nevím. Zkuste dotaz prosím přeformulovat, nebo nás kontaktujte.";
const HISTORY_LIMIT = 6;

const STATUS: Record<State, string> = {
  idle: "Online",
  thinking: "Přemýšlím…",
  talking: "Odpovídám…",
  happy: "Jupí! 🎉",
  wave: "Vítejte!",
  confused: "Hmm…?",
};

const GRATITUDE = /děk|díky|dik|super|paráda|skvěl[áée... ]?|wow|👍|🙏/i;

const UNICODE_EXT =
  "U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+0304,U+0308,U+0329,U+1D00-1DBF,U+1E00-1E9F,U+1EF2-1EFF,U+2020,U+20A0-20AB,U+20AD-20C0,U+2113,U+2C60-2C7F,U+A720-A7FF";
const UNICODE_LAT =
  "U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD";

let fontsRequested = false;

/** Load Baloo 2 + Nunito via the FontFace API (works inside Shadow DOM, unlike @font-face). */
function loadFonts(): void {
  if (fontsRequested) return;
  const fontSet = (document as Document & { fonts?: FontFaceSet }).fonts;
  if (!fontSet || typeof FontFace === "undefined") return;
  fontsRequested = true;

  const defs: ReadonlyArray<[string, string, string, string]> = [
    ["Baloo 2", balooExt, "500 800", UNICODE_EXT],
    ["Baloo 2", balooLatin, "500 800", UNICODE_LAT],
    ["Nunito", nunitoExt, "400 800", UNICODE_EXT],
    ["Nunito", nunitoLatin, "400 800", UNICODE_LAT],
  ];

  for (const [family, url, weight, unicodeRange] of defs) {
    try {
      const face = new FontFace(family, `url(${url})`, { weight, unicodeRange, display: "swap" });
      void face.load().then((f) => fontSet.add(f)).catch(() => {});
    } catch {
      // ignore — system fonts are a fine fallback
    }
  }
}

function readConfig(): WidgetConfig | null {
  const script =
    (document.currentScript as HTMLScriptElement | null) ?? findScriptByDataAttr();
  if (!script) return null;

  const apiUrl = script.getAttribute("data-api-url")?.trim();
  if (!apiUrl) {
    console.warn("[enerkom-widget] missing data-api-url — widget not initialised.");
    return null;
  }
  return {
    apiUrl,
    orgName: script.getAttribute("data-org-name")?.trim() || "naše organizace",
    // data-primary-color is intentionally ignored in variant 2 (green Elektron identity).
    primaryColor: undefined,
  };
}

function findScriptByDataAttr(): HTMLScriptElement | null {
  const scripts = document.querySelectorAll<HTMLScriptElement>("script[data-api-url]");
  return scripts.length > 0 ? scripts[scripts.length - 1] : null;
}

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  return node;
}

class ChatWidget {
  private readonly cfg: WidgetConfig;
  private readonly host: HTMLDivElement;
  private readonly shadow: ShadowRoot;

  private launcher!: HTMLButtonElement;
  private panel!: HTMLDivElement;
  private stage!: HTMLDivElement;
  private statusEl!: HTMLSpanElement;
  private confettiBox!: HTMLDivElement;
  private msgs!: HTMLDivElement;
  private typing!: HTMLDivElement;
  private input!: HTMLInputElement;
  private sendBtn!: HTMLButtonElement;

  private readonly history: Message[] = [];
  private readonly turns: PersistedTurn[] = [];
  private greeted = false;
  private isOpen = false;
  private busy = false;
  private timers: number[] = [];

  constructor(cfg: WidgetConfig) {
    this.cfg = cfg;
    this.host = document.createElement("div");
    this.host.setAttribute("data-enerkom-widget", "");
    this.shadow = this.host.attachShadow({ mode: "open" });
    document.body.appendChild(this.host);
    loadFonts();
    this.render();
    this.restore();
  }

  /** Rehydrate a conversation persisted by a previous page in this tab session. */
  private restore(): void {
    const saved = loadState();
    if (!saved) return;
    this.greeted = saved.greeted;
    this.history.push(...saved.history);
    for (const t of saved.turns) {
      this.turns.push(t);
      if (t.kind === "user") this.renderUser(t.text);
      else if (t.kind === "bot") this.renderBot(t.text, t.sources);
      else this.renderError(t.text);
    }
    if (saved.open) {
      // Reopen without re-greeting or stealing focus on page load.
      this.isOpen = true;
      this.panel.classList.add("open");
      this.launcher.hidden = true;
    }
    this.scrollToBottom();
  }

  private persist(): void {
    saveState({
      open: this.isOpen,
      greeted: this.greeted,
      turns: this.turns,
      history: this.history,
    });
  }

  private render(): void {
    const style = document.createElement("style");
    style.textContent = buildCss();
    this.shadow.appendChild(style);

    const root = el("div", "ek-root");

    // ---- launcher ----
    this.launcher = el("button", "ek-launch");
    this.launcher.type = "button";
    this.launcher.setAttribute("aria-label", "Otevřít chat s Elektronem");
    const av = el("span", "av");
    av.setAttribute("aria-hidden", "true");
    const avImg = el("img");
    avImg.src = mascotUrl;
    avImg.alt = "";
    av.appendChild(avImg);
    this.launcher.append(av, document.createTextNode("Zeptejte se Elektrona"));
    this.launcher.addEventListener("click", () => this.open());

    // ---- panel ----
    this.panel = el("div", "ek-panel");
    this.panel.setAttribute("role", "dialog");
    this.panel.setAttribute("aria-label", `Chat – ${this.cfg.orgName}`);
    this.panel.append(this.buildStage(), this.buildChat());
    this.panel.addEventListener("keydown", (e) => {
      if (e.key === "Escape") this.close();
    });

    root.append(this.launcher, this.panel);
    this.shadow.appendChild(root);
  }

  private buildStage(): HTMLDivElement {
    this.stage = el("div", "ek-stage s-idle");

    const blobA = el("div", "blob a");
    const blobB = el("div", "blob b");

    const pill = el("div", "ek-pill");
    const pillInner = el("div");
    pillInner.append(el("span", "dot"));
    this.statusEl = el("span");
    this.statusEl.textContent = STATUS.idle;
    pillInner.appendChild(this.statusEl);
    pill.appendChild(pillInner);

    this.confettiBox = el("div", "ek-confetti");

    const mascot = el("div", "ek-mascot");
    mascot.append(
      this.bubble("think", [el("span"), el("span"), el("span")], true),
      this.bubble("wave", [document.createTextNode("Ahoj!")]),
      this.bubble("conf", [document.createTextNode("?")]),
      el("div", "ek-shadow"),
    );
    const body3d = el("div", "ek-body3d");
    const mImg = el("img");
    mImg.src = mascotUrl;
    mImg.alt = "Elektron";
    body3d.append(mImg, el("div", "ek-eye l"), el("div", "ek-eye r"));
    mascot.appendChild(body3d);

    this.stage.append(blobA, blobB, pill, this.confettiBox, mascot);
    return this.stage;
  }

  private bubble(kind: "think" | "wave" | "conf", children: Node[], dots = false): HTMLDivElement {
    const wrap = el("div", `ek-bubble ${kind}`);
    const box = el("div", "box");
    box.append(...children);
    if (!dots) box.appendChild(el("div", "nib"));
    wrap.appendChild(box);
    if (dots) wrap.appendChild(el("div", "tail"));
    return wrap;
  }

  private buildChat(): HTMLDivElement {
    const chat = el("div", "ek-chat");

    const head = el("div", "ek-head");
    const logo = el("div", "logo");
    const bolt = el("span");
    bolt.textContent = "⚡";
    logo.appendChild(bolt);
    const meta = el("div", "meta");
    const name = el("b");
    name.textContent = "Elektron";
    const org = el("small");
    org.textContent = this.cfg.orgName;
    meta.append(name, org);
    const close = el("button", "x");
    close.type = "button";
    close.setAttribute("aria-label", "Zavřít chat");
    close.textContent = "×";
    close.addEventListener("click", () => this.close());
    head.append(logo, meta, close);

    this.msgs = el("div", "ek-msgs");
    this.msgs.setAttribute("role", "log");
    this.msgs.setAttribute("aria-live", "polite");
    this.typing = el("div", "typing");
    this.typing.append(el("span"), el("span"), el("span"));
    this.msgs.appendChild(this.typing);

    const foot = el("div", "ek-foot");
    this.input = el("input");
    this.input.type = "text";
    this.input.setAttribute("autocomplete", "off");
    this.input.placeholder = "Napište Elektronovi…";
    this.input.setAttribute("aria-label", "Napište dotaz");
    this.sendBtn = el("button");
    this.sendBtn.type = "button";
    this.sendBtn.setAttribute("aria-label", "Odeslat");
    this.sendBtn.textContent = "➤";
    this.sendBtn.addEventListener("click", () => void this.send());
    this.input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        e.preventDefault();
        void this.send();
      } else if (e.key === "Escape") {
        this.close();
      }
    });
    foot.append(this.input, this.sendBtn);

    chat.append(head, this.msgs, foot);
    return chat;
  }

  // ---- state machine ----
  private clearTimers(): void {
    for (const t of this.timers) clearTimeout(t);
    this.timers = [];
  }

  private setState(state: State): void {
    this.stage.className = `ek-stage s-${state}`;
    this.statusEl.textContent = STATUS[state];
    this.msgs.classList.toggle("thinking", state === "thinking");
  }

  private settleToIdle(delayMs: number): void {
    this.timers.push(window.setTimeout(() => this.setState("idle"), delayMs));
  }

  private buildConfetti(): void {
    if (this.confettiBox.childElementCount > 0) return;
    const colors = ["#f4c430", "#ffd95e", "#8fd44f", "#ffffff", "#c6f7cd", "#5bbf57"];
    for (let i = 0; i < 14; i++) {
      const s = el("div");
      const left = Math.round(Math.random() * 92) + 4;
      const size = 6 + Math.round(Math.random() * 6);
      const delay = (Math.random() * 0.9).toFixed(2);
      const dur = (1.1 + Math.random() * 0.8).toFixed(2);
      s.style.cssText =
        `position:absolute;top:0;left:${left}%;width:${size}px;height:${size}px;` +
        `background:${colors[i % colors.length]};border-radius:${i % 2 ? "50%" : "2px"};` +
        `animation:ek-confettiFall ${dur}s linear ${delay}s infinite;`;
      this.confettiBox.appendChild(s);
    }
  }

  // ---- panel open/close ----
  private open(): void {
    this.isOpen = true;
    this.panel.classList.add("open");
    this.launcher.hidden = true;
    this.input.focus();
    if (!this.greeted) {
      this.greeted = true;
      this.renderBot(GREETING);
      this.turns.push({ kind: "bot", text: GREETING });
      this.clearTimers();
      this.setState("wave");
      this.settleToIdle(1800);
    }
    this.persist();
  }

  private close(): void {
    this.isOpen = false;
    this.panel.classList.remove("open");
    this.launcher.hidden = false;
    this.clearTimers();
    this.setState("idle");
    this.launcher.focus();
    this.persist();
  }

  // ---- messages ----
  // render* build DOM only; persistence happens where a turn is recorded.
  private renderUser(text: string): void {
    const m = el("div", "msg user");
    m.textContent = text;
    this.msgs.insertBefore(m, this.typing);
    this.scrollToBottom();
  }

  private renderBot(text: string, sources?: Source[]): HTMLDivElement {
    const m = el("div", "msg bot");
    m.textContent = text;
    this.msgs.insertBefore(m, this.typing);
    if (sources && sources.length > 0) this.renderSources(m, sources);
    this.scrollToBottom();
    return m;
  }

  private renderError(text: string): void {
    const e = el("div", "msg error");
    e.textContent = text;
    this.msgs.insertBefore(e, this.typing);
    this.scrollToBottom();
  }

  private renderSources(parent: HTMLDivElement, sources: Source[]): void {
    if (sources.length === 0) return;
    const wrap = el("div", "sources");
    sources.forEach((s, i) => {
      if (!s || typeof s.uri !== "string") return;
      const a = el("a");
      if (/^https?:\/\//i.test(s.uri)) {
        a.href = s.uri;
        a.target = "_blank";
        a.rel = "noopener noreferrer";
      }
      const num = el("span", "num");
      num.textContent = `[${i + 1}]`;
      a.append(num, document.createTextNode(s.title || s.uri));
      wrap.appendChild(a);
    });
    if (wrap.childElementCount > 0) {
      parent.appendChild(wrap);
      this.scrollToBottom();
    }
  }

  private scrollToBottom(): void {
    this.msgs.scrollTop = this.msgs.scrollHeight;
  }

  // ---- send ----
  private async send(): Promise<void> {
    if (this.busy) return;
    const question = this.input.value.trim();
    if (!question) return;

    this.busy = true;
    this.sendBtn.disabled = true;
    this.input.value = "";
    this.clearTimers();
    this.renderUser(question);
    this.turns.push({ kind: "user", text: question });
    this.history.push({ role: "user", content: question });
    this.persist();
    this.setState("thinking");

    let bubble: HTMLDivElement | null = null;
    let answerText = "";
    let firstToken = true;
    const sourcesBuffer: Source[] = [];

    const ensureBubble = (): HTMLDivElement => {
      if (!bubble) {
        bubble = el("div", "msg bot");
        this.msgs.insertBefore(bubble, this.typing);
      }
      return bubble;
    };

    try {
      await sendChat(this.cfg.apiUrl, { question, history: this.history.slice(-HISTORY_LIMIT) }, {
        onToken: (text) => {
          if (firstToken) {
            firstToken = false;
            this.setState("talking");
          }
          answerText += text;
          ensureBubble().textContent = answerText; // textContent — no HTML injection
          this.scrollToBottom();
        },
        onSources: (s) => {
          sourcesBuffer.length = 0;
          sourcesBuffer.push(...s);
        },
        onDone: (answered) => {
          const b = ensureBubble();
          const finalText = answerText.length === 0 && !answered ? NOT_FOUND : answerText;
          if (answerText.length === 0 && !answered) b.textContent = NOT_FOUND;
          this.renderSources(b, sourcesBuffer);
          this.turns.push({
            kind: "bot",
            text: finalText,
            sources: sourcesBuffer.length > 0 ? [...sourcesBuffer] : undefined,
          });
          if (answered) {
            this.history.push({ role: "assistant", content: answerText });
            if (GRATITUDE.test(question)) {
              this.buildConfetti();
              this.setState("happy");
              this.settleToIdle(1900);
            } else {
              this.setState("idle");
            }
          } else {
            this.setState("confused");
            this.settleToIdle(1400);
          }
          this.persist();
        },
      });
    } catch (err) {
      const message = err instanceof ChatApiError && err.message ? err.message : GENERIC_ERROR;
      this.renderError(message);
      this.turns.push({ kind: "error", text: message });
      this.persist();
      this.setState("confused");
      this.settleToIdle(1400);
    } finally {
      this.busy = false;
      this.sendBtn.disabled = false;
      this.input.focus();
    }
  }
}

function init(): void {
  const cfg = readConfig();
  if (!cfg) return;
  const start = (): ChatWidget => new ChatWidget(cfg);
  if (document.body) {
    start();
  } else {
    document.addEventListener("DOMContentLoaded", start, { once: true });
  }
}

init();
