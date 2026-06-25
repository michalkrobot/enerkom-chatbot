// EnerkomChatbot chat widget — embeddable IIFE entry. Auto-initialises on load.
//
// Embed:
//   <script src=".../widget.js"
//           data-api-url="https://.../api/chat"
//           data-org-name="Enerkom HP"
//           data-primary-color="#1D9E75" defer></script>
//
// Self-contained: lottie-web, the Elektron Lottie, the launcher avatar and all
// CSS are bundled. The widget renders inside a Shadow DOM so host-page styles
// neither leak in nor out, and it never holds an API key — it only calls our API.

import { Mascot } from "./mascot";
import { buildCss } from "./styles";
import { ChatApiError, GENERIC_ERROR, sendChat } from "./api";
import type { Message, Source, WidgetConfig } from "./types";
// Inlined as a base64 data: URI at build time (no external request).
import avatarUrl from "../assets/elektron-head.png?inline";

const GREETING =
  "Dobrý den, zeptejte se mě na cokoli o naší organizaci.";
const HISTORY_LIMIT = 6;

/** Escape text for safe insertion into the DOM (we never use innerHTML with input). */
function escapeText(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function readConfig(): WidgetConfig | null {
  const script =
    (document.currentScript as HTMLScriptElement | null) ??
    findScriptByDataAttr();
  if (!script) return null;

  const apiUrl = script.getAttribute("data-api-url")?.trim();
  if (!apiUrl) {
    // eslint-disable-next-line no-console
    console.warn("[enerkom-widget] missing data-api-url — widget not initialised.");
    return null;
  }
  return {
    apiUrl,
    orgName: script.getAttribute("data-org-name")?.trim() || "naše organizace",
    primaryColor: script.getAttribute("data-primary-color")?.trim() || undefined,
  };
}

function findScriptByDataAttr(): HTMLScriptElement | null {
  const scripts = document.querySelectorAll<HTMLScriptElement>("script[data-api-url]");
  return scripts.length > 0 ? scripts[scripts.length - 1] : null;
}

class ChatWidget {
  private readonly cfg: WidgetConfig;
  private readonly host: HTMLDivElement;
  private readonly shadow: ShadowRoot;

  private launcher!: HTMLButtonElement;
  private panel!: HTMLDivElement;
  private body!: HTMLDivElement;
  private input!: HTMLInputElement;
  private sendBtn!: HTMLButtonElement;
  private mascotMount!: HTMLDivElement;

  private mascot: Mascot | null = null;
  private mascotStarted = false;
  private readonly history: Message[] = [];
  private greeted = false;
  private busy = false;

  constructor(cfg: WidgetConfig) {
    this.cfg = cfg;
    this.host = document.createElement("div");
    this.host.setAttribute("data-enerkom-widget", "");
    this.shadow = this.host.attachShadow({ mode: "open" });
    document.body.appendChild(this.host);
    this.render();
  }

  private render(): void {
    const style = document.createElement("style");
    style.textContent = buildCss(avatarUrl);
    this.shadow.appendChild(style);

    const root = document.createElement("div");
    root.className = "ek-root";

    // optional brand colour override (--mid)
    if (this.cfg.primaryColor) {
      root.style.setProperty("--mid", this.cfg.primaryColor);
    }

    // launcher
    this.launcher = document.createElement("button");
    this.launcher.className = "ek-launch";
    this.launcher.type = "button";
    this.launcher.setAttribute("aria-label", "Otevřít chat");
    const av = document.createElement("span");
    av.className = "av";
    av.setAttribute("aria-hidden", "true");
    this.launcher.appendChild(av);
    this.launcher.appendChild(document.createTextNode("Zeptejte se Elektrona"));
    this.launcher.addEventListener("click", () => this.open());

    // panel
    this.panel = document.createElement("div");
    this.panel.className = "ek-panel";
    this.panel.setAttribute("role", "dialog");
    this.panel.setAttribute("aria-modal", "false");
    this.panel.setAttribute("aria-label", `Chat – ${this.cfg.orgName}`);

    // header
    const head = document.createElement("div");
    head.className = "ek-head";
    const mascotWrap = document.createElement("div");
    mascotWrap.className = "mascot";
    this.mascotMount = document.createElement("div");
    mascotWrap.appendChild(this.mascotMount);
    const meta = document.createElement("div");
    meta.className = "meta";
    const name = document.createElement("b");
    name.textContent = "Elektron";
    const org = document.createElement("span");
    org.textContent = this.cfg.orgName;
    meta.append(name, document.createElement("br"), org);
    const close = document.createElement("button");
    close.className = "x";
    close.type = "button";
    close.setAttribute("aria-label", "Zavřít chat");
    close.textContent = "×";
    close.addEventListener("click", () => this.close());
    head.append(mascotWrap, meta, close);

    // body
    this.body = document.createElement("div");
    this.body.className = "ek-body";
    this.body.setAttribute("role", "log");
    this.body.setAttribute("aria-live", "polite");

    // footer
    const foot = document.createElement("div");
    foot.className = "ek-foot";
    this.input = document.createElement("input");
    this.input.type = "text";
    this.input.setAttribute("autocomplete", "off");
    this.input.placeholder = "Napište dotaz…";
    this.input.setAttribute("aria-label", "Napište dotaz");
    this.sendBtn = document.createElement("button");
    this.sendBtn.type = "button";
    this.sendBtn.setAttribute("aria-label", "Odeslat");
    this.sendBtn.textContent = "➤";
    this.sendBtn.addEventListener("click", () => void this.send());
    foot.append(this.input, this.sendBtn);

    this.input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        e.preventDefault();
        void this.send();
      } else if (e.key === "Escape") {
        this.close();
      }
    });
    this.panel.addEventListener("keydown", (e) => {
      if (e.key === "Escape") this.close();
    });

    this.panel.append(head, this.body, foot);
    root.append(this.launcher, this.panel);
    this.shadow.appendChild(root);
  }

  private async open(): Promise<void> {
    this.panel.classList.add("open");
    this.launcher.hidden = true;

    if (!this.mascot) {
      this.mascot = new Mascot(this.mascotMount);
      try {
        await this.mascot.ready;
      } catch {
        // mascot is decorative — chat still works without it
      }
    }
    if (this.mascot && this.mascotStarted) {
      // re-entering an open panel — settle back to idle
      this.mascot.set("idle");
    }
    this.mascotStarted = true;

    if (!this.greeted) {
      this.greeted = true;
      this.addBotMessage(GREETING);
    }
    this.input.focus();
  }

  private close(): void {
    this.panel.classList.remove("open");
    this.launcher.hidden = false;
    // Panel is display:none while closed, so the Lottie SVG is not painted —
    // no extra pause needed (Mascot's public API is set/current/destroy only).
    this.launcher.focus();
  }

  private addUserMessage(text: string): void {
    const el = document.createElement("div");
    el.className = "msg user";
    el.textContent = text;
    this.body.appendChild(el);
    this.scrollToBottom();
  }

  private addBotMessage(text: string): HTMLDivElement {
    const el = document.createElement("div");
    el.className = "msg bot";
    el.textContent = text;
    this.body.appendChild(el);
    this.scrollToBottom();
    return el;
  }

  private addTypingIndicator(): HTMLDivElement {
    const el = document.createElement("div");
    el.className = "msg bot";
    const dots = document.createElement("span");
    dots.className = "dots";
    dots.append(
      document.createElement("span"),
      document.createElement("span"),
      document.createElement("span"),
    );
    el.appendChild(dots);
    this.body.appendChild(el);
    this.scrollToBottom();
    return el;
  }

  private renderSources(parent: HTMLDivElement, sources: Source[]): void {
    if (sources.length === 0) return;
    const wrap = document.createElement("div");
    wrap.className = "sources";
    sources.forEach((s, i) => {
      if (!s || typeof s.uri !== "string") return;
      const a = document.createElement("a");
      // Only allow http(s) links to open externally; otherwise render as text.
      const safe = /^https?:\/\//i.test(s.uri);
      if (safe) {
        a.href = s.uri;
        a.target = "_blank";
        a.rel = "noopener noreferrer";
      }
      const num = document.createElement("span");
      num.className = "num";
      num.textContent = `[${i + 1}]`;
      a.appendChild(num);
      a.appendChild(document.createTextNode(s.title || s.uri));
      wrap.appendChild(a);
    });
    if (wrap.childElementCount > 0) {
      parent.appendChild(wrap);
      this.scrollToBottom();
    }
  }

  private scrollToBottom(): void {
    this.body.scrollTop = this.body.scrollHeight;
  }

  private async send(): Promise<void> {
    if (this.busy) return;
    const question = this.input.value.trim();
    if (!question) return;

    this.busy = true;
    this.sendBtn.disabled = true;
    this.input.value = "";
    this.addUserMessage(question);
    this.history.push({ role: "user", content: question });

    this.mascot?.set("thinking");
    const typing = this.addTypingIndicator();

    let bubble: HTMLDivElement | null = null;
    let answerText = "";
    let firstToken = true;
    const sourcesBuffer: Source[] = [];

    const ensureBubble = (): HTMLDivElement => {
      if (!bubble) {
        typing.remove();
        bubble = document.createElement("div");
        bubble.className = "msg bot";
        this.body.appendChild(bubble);
      }
      return bubble;
    };

    try {
      await sendChat(
        this.cfg.apiUrl,
        { question, history: this.history.slice(-HISTORY_LIMIT) },
        {
          onToken: (text) => {
            if (firstToken) {
              firstToken = false;
              this.mascot?.set("talking");
            }
            answerText += text;
            const b = ensureBubble();
            b.textContent = answerText; // textContent — no HTML injection
            this.scrollToBottom();
          },
          onSources: (s) => {
            sourcesBuffer.length = 0;
            sourcesBuffer.push(...s);
          },
          onDone: (answered) => {
            const b = ensureBubble();
            if (answerText.length === 0) {
              b.textContent = answered
                ? ""
                : "To bohužel přesně nevím. Zkuste dotaz prosím přeformulovat, nebo nás kontaktujte.";
            }
            this.renderSources(b, sourcesBuffer);
            if (answered) {
              this.history.push({ role: "assistant", content: answerText });
            }
            this.mascot?.set(answered ? "idle" : "notfound");
          },
        },
      );
    } catch (err) {
      typing.remove();
      this.mascot?.set("idle");
      const message =
        err instanceof ChatApiError && err.message ? err.message : GENERIC_ERROR;
      const el = document.createElement("div");
      el.className = "msg error";
      el.textContent = message;
      this.body.appendChild(el);
      this.scrollToBottom();
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
  const start = () => new ChatWidget(cfg);
  if (document.body) {
    start();
  } else {
    document.addEventListener("DOMContentLoaded", start, { once: true });
  }
}

init();

// escapeText is exported only to keep it available for tests / future markdown
// rendering; the widget itself uses textContent everywhere.
export { escapeText };
