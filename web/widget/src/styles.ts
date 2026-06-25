// All widget CSS, scoped inside the Shadow DOM. Adapted from demo.html.
// `__AVATAR__` is replaced with the inlined launcher-head data URI at runtime.

export const AVATAR_PLACEHOLDER = "__AVATAR__";

export function buildCss(avatarDataUri: string): string {
  return CSS.replace(AVATAR_PLACEHOLDER, avatarDataUri);
}

const CSS = `
:host {
  --dark: #0F6E56;
  --mid: #1D9E75;
  --light: #5DCAA5;
  --userbg: #E1F5EE;
  --sun: #EF9F27;
  --ink: #1a3b32;
  --panel: #fff;
  all: initial;
}
*, *::before, *::after { box-sizing: border-box; }

.ek-root {
  font-family: system-ui, "Segoe UI", sans-serif;
  font-size: 16px;
  line-height: 1.4;
  color: var(--ink);
}

/* launcher */
.ek-launch {
  position: fixed; right: 22px; bottom: 22px; z-index: 2147483000;
  display: flex; align-items: center; gap: 10px; padding: 8px 16px 8px 8px;
  background: var(--mid); color: #fff; border: 0; border-radius: 999px; cursor: pointer;
  box-shadow: 0 10px 30px rgba(15, 110, 86, .35); font-size: 15px; font-weight: 600;
  font-family: inherit;
}
.ek-launch:hover { background: var(--dark); }
.ek-launch:focus-visible { outline: 3px solid var(--sun); outline-offset: 2px; }
.ek-launch .av {
  width: 48px; height: 48px; border-radius: 50%; flex: 0 0 auto;
  background: #fff url("${AVATAR_PLACEHOLDER}") no-repeat center;
  background-size: contain;
}
.ek-launch[hidden] { display: none; }

/* panel */
.ek-panel {
  position: fixed; right: 22px; bottom: 22px; z-index: 2147483001; width: 374px;
  max-width: calc(100vw - 32px); height: 560px; max-height: calc(100vh - 44px);
  background: var(--panel); border-radius: 20px; overflow: hidden; display: none;
  flex-direction: column; box-shadow: 0 24px 60px rgba(15, 60, 46, .30);
}
.ek-panel.open { display: flex; }

.ek-head {
  position: relative; background: linear-gradient(135deg, var(--dark), var(--mid));
  color: #fff; padding: 0 18px; display: flex; align-items: flex-end; gap: 14px;
  min-height: 178px;
}
.ek-head .mascot { width: 82px; height: 178px; flex: 0 0 auto; }
.ek-head .mascot > div { width: 100%; height: 100%; }
.ek-head .meta { align-self: center; padding-bottom: 20px; }
.ek-head .meta b { font-size: 17px; }
.ek-head .meta span { font-size: 13px; opacity: .9; }
.ek-head .x {
  position: absolute; top: 12px; right: 14px; background: transparent; border: 0;
  color: #fff; font-size: 24px; cursor: pointer; opacity: .85; line-height: 1;
  font-family: inherit;
}
.ek-head .x:hover { opacity: 1; }
.ek-head .x:focus-visible { outline: 2px solid #fff; outline-offset: 2px; border-radius: 4px; }

.ek-body {
  flex: 1; overflow-y: auto; padding: 16px; display: flex; flex-direction: column;
  gap: 10px; background: #f6fcfa;
}
.msg {
  max-width: 82%; padding: 9px 13px; border-radius: 14px; font-size: 14px;
  line-height: 1.45; white-space: pre-wrap; overflow-wrap: anywhere;
}
.msg.bot {
  align-self: flex-start; background: #fff; border: 1px solid #e2efe9;
  border-bottom-left-radius: 5px;
}
.msg.user {
  align-self: flex-end; background: var(--userbg); color: var(--dark);
  border-bottom-right-radius: 5px;
}
.msg.error {
  align-self: flex-start; background: #fff4e6; border: 1px solid #f3d6a8;
  color: #8a5300; border-bottom-left-radius: 5px;
}

.sources { margin-top: 8px; display: flex; flex-direction: column; gap: 4px; }
.sources a {
  font-size: 12.5px; color: var(--dark); text-decoration: none;
  overflow-wrap: anywhere;
}
.sources a:hover { text-decoration: underline; }
.sources a .num { font-weight: 700; color: var(--mid); margin-right: 4px; }

.dots span {
  display: inline-block; width: 6px; height: 6px; margin: 0 2px; border-radius: 50%;
  background: var(--mid); animation: ek-bnc 1.2s infinite;
}
.dots span:nth-child(2) { animation-delay: .15s; }
.dots span:nth-child(3) { animation-delay: .3s; }
@keyframes ek-bnc {
  0%, 80%, 100% { transform: translateY(0); opacity: .4; }
  40% { transform: translateY(-5px); opacity: 1; }
}

.ek-foot {
  display: flex; gap: 8px; padding: 12px; border-top: 1px solid #eef3f1; background: #fff;
}
.ek-foot input {
  flex: 1; border: 1px solid #d7e6e0; border-radius: 999px; padding: 10px 14px;
  font-size: 14px; outline: none; font-family: inherit; color: var(--ink);
}
.ek-foot input:focus { border-color: var(--mid); }
.ek-foot button {
  border: 0; border-radius: 50%; width: 42px; height: 42px; background: var(--mid);
  color: #fff; font-size: 18px; cursor: pointer; flex: 0 0 auto; font-family: inherit;
}
.ek-foot button:hover { background: var(--dark); }
.ek-foot button:focus-visible { outline: 3px solid var(--sun); outline-offset: 2px; }
.ek-foot button:disabled { opacity: .5; cursor: default; }

/* mobile: full-width panel */
@media (max-width: 480px) {
  .ek-panel {
    right: 0; bottom: 0; width: 100vw; max-width: 100vw; height: 100dvh;
    max-height: 100dvh; border-radius: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .dots span { animation: none; }
}
`;
