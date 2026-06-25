// All widget CSS, scoped inside the Shadow DOM. Variant 2: mascot in its own green
// stage beside the chat. Fonts (Baloo 2, Nunito) are loaded via the FontFace API in
// widget.ts (reliable inside Shadow DOM) — here we only reference the families.

export function buildCss(): string {
  return CSS;
}

const CSS = `
:host { all: initial; }
*, *::before, *::after { box-sizing: border-box; }

.ek-root {
  font-family: 'Nunito', system-ui, "Segoe UI", sans-serif;
  font-size: 16px; line-height: 1.4; color: #234;
}

/* ===== mascot animations ===== */
@keyframes ek-breathe { 0%,100%{transform:translateY(0) scale(1)} 50%{transform:translateY(-5px) scale(1.012)} }
@keyframes ek-thinkTilt { 0%,100%{transform:rotate(-5deg) translateY(0)} 50%{transform:rotate(2.5deg) translateY(-2px)} }
@keyframes ek-talkBob { 0%,100%{transform:translateY(0) scaleY(1)} 50%{transform:translateY(3px) scaleY(.985)} }
@keyframes ek-happyBounce { 0%,100%{transform:translateY(0) scale(1,1)} 18%{transform:translateY(-24px) scale(.97,1.06)} 42%{transform:translateY(0) scale(1.07,.93)} 62%{transform:translateY(-9px) scale(1,1)} }
@keyframes ek-waveGreet { 0%,100%{transform:rotate(0deg)} 20%{transform:rotate(7deg)} 45%{transform:rotate(-5deg)} 70%{transform:rotate(6deg)} }
@keyframes ek-confusedWobble { 0%,100%{transform:rotate(-3.5deg) translateY(0)} 50%{transform:rotate(6deg) translateY(-3px)} }
@keyframes ek-blink { 0%,90%,96.5%,100%{transform:scaleY(0)} 93.3%{transform:scaleY(1)} }
@keyframes ek-dotJump { 0%,72%,100%{transform:translateY(0);opacity:.45} 36%{transform:translateY(-7px);opacity:1} }
@keyframes ek-confettiFall { 0%{transform:translateY(-12px) rotate(0);opacity:0} 12%{opacity:1} 100%{transform:translateY(330px) rotate(420deg);opacity:0} }
@keyframes ek-pulseDot { 0%,100%{transform:scale(1);opacity:1} 50%{transform:scale(.6);opacity:.55} }
@keyframes ek-popIn { 0%{transform:scale(.7) translateY(14px);opacity:0} 100%{transform:scale(1) translateY(0);opacity:1} }

/* ===== launcher ===== */
.ek-launch {
  position: fixed; right: 24px; bottom: 24px; z-index: 2147483000; display: flex;
  align-items: center; gap: 11px; padding: 8px 18px 8px 8px; border: 0; cursor: pointer;
  background: linear-gradient(160deg,#41a85d,#2e8b4f); color: #fff; border-radius: 999px;
  font-family: 'Baloo 2', 'Nunito', cursive; font-weight: 800; font-size: 15px;
  box-shadow: 0 12px 30px -6px rgba(31,107,58,.55); transition: transform .15s, box-shadow .15s;
}
.ek-launch:hover { transform: translateY(-2px); box-shadow: 0 16px 36px -6px rgba(31,107,58,.6); }
.ek-launch:focus-visible { outline: 3px solid #ffd95e; outline-offset: 2px; }
.ek-launch .av {
  position: relative; width: 46px; height: 46px; border-radius: 50%; flex: none; overflow: hidden;
  background: #eaf6ec; box-shadow: inset 0 0 0 2px rgba(255,255,255,.6);
}
.ek-launch .av img { position: absolute; left: 50%; top: 0; width: 70%; height: auto; transform: translate(-50%,-3%); display: block; }
.ek-launch[hidden] { display: none; }

/* ===== panel ===== */
.ek-panel {
  position: fixed; right: 24px; bottom: 24px; z-index: 2147483001;
  width: 580px; max-width: calc(100vw - 32px); height: 520px; max-height: calc(100vh - 48px);
  display: none; background: #fff; border-radius: 24px; overflow: hidden;
  box-shadow: 0 28px 64px -22px rgba(31,107,58,.5), 0 6px 18px rgba(31,107,58,.14);
  transform-origin: bottom right;
}
.ek-panel.open { display: flex; animation: ek-popIn .26s cubic-bezier(.2,.9,.3,1.2); }

/* ===== mascot stage (left) ===== */
.ek-stage {
  position: relative; width: 212px; flex: none; overflow: hidden;
  background: linear-gradient(165deg,#41a85d 0%,#2e8b4f 55%,#22703e 100%);
  display: flex; flex-direction: column; align-items: center; justify-content: flex-end;
}
.ek-stage .blob { position: absolute; border-radius: 50%; background: rgba(255,255,255,.07); }
.ek-stage .blob.a { top: -40px; left: -40px; width: 140px; height: 140px; }
.ek-stage .blob.b { bottom: -30px; right: -30px; width: 120px; height: 120px; background: rgba(255,255,255,.06); }
.ek-pill { position: absolute; top: 16px; left: 0; right: 0; display: flex; justify-content: center; z-index: 7; }
.ek-pill > div { display: flex; align-items: center; gap: 7px; background: rgba(255,255,255,.2); color: #fff; font-weight: 800; font-size: 12px; padding: 6px 13px; border-radius: 999px; }
.ek-pill .dot { width: 8px; height: 8px; border-radius: 50%; background: #c6f7cd; animation: ek-pulseDot 1.8s ease-in-out infinite; }

.ek-confetti { position: absolute; inset: 0; pointer-events: none; overflow: hidden; z-index: 8; display: none; }
.ek-stage.s-happy .ek-confetti { display: block; }

.ek-mascot { position: relative; width: 132px; aspect-ratio: 330/710; margin-top: 26px; }
.ek-bubble { position: absolute; z-index: 6; display: none; }
.ek-stage.s-thinking .ek-bubble.think { display: block; }
.ek-stage.s-wave .ek-bubble.wave { display: block; }
.ek-stage.s-confused .ek-bubble.conf { display: block; }
.ek-bubble.think { top: 26px; left: 96px; }
.ek-bubble.think .box { display: flex; gap: 5px; background: #fff; padding: 10px 13px; border-radius: 16px; box-shadow: 0 8px 18px rgba(0,0,0,.2); }
.ek-bubble.think .tail { width: 9px; height: 9px; border-radius: 50%; background: #fff; margin: 3px 0 0 4px; }
.ek-bubble.think span { width: 7px; height: 7px; border-radius: 50%; background: #7a8f80; animation: ek-dotJump 1.1s ease-in-out infinite; }
.ek-bubble.think span:nth-child(2) { animation-delay: .18s; }
.ek-bubble.think span:nth-child(3) { animation-delay: .36s; }
.ek-bubble.wave { top: 20px; left: 92px; }
.ek-bubble.wave .box { position: relative; background: #fff; color: #1f6b3a; font-family: 'Baloo 2','Nunito',cursive; font-weight: 800; font-size: 15px; padding: 8px 14px; border-radius: 16px; box-shadow: 0 8px 18px rgba(0,0,0,.2); white-space: nowrap; }
.ek-bubble.conf { top: 14px; left: 98px; }
.ek-bubble.conf .box { position: relative; background: #fff; color: #e0a000; font-family: 'Baloo 2','Nunito',cursive; font-weight: 800; font-size: 22px; line-height: 1; padding: 6px 13px; border-radius: 16px; box-shadow: 0 8px 18px rgba(0,0,0,.2); }
.ek-bubble .nib { position: absolute; bottom: 7px; left: -5px; width: 12px; height: 12px; background: #fff; transform: rotate(45deg); border-radius: 2px; }

.ek-shadow { position: absolute; bottom: 2px; left: 50%; transform: translateX(-50%); width: 112px; height: 18px; background: rgba(0,0,0,.2); filter: blur(7px); border-radius: 50%; z-index: 0; }
.ek-body3d { position: absolute; inset: 0; transform-origin: 50% 100%; z-index: 1; animation: ek-breathe 4s ease-in-out infinite; }
.ek-stage.s-thinking .ek-body3d { animation: ek-thinkTilt 2.6s ease-in-out infinite; }
.ek-stage.s-talking .ek-body3d { animation: ek-talkBob .42s ease-in-out infinite; }
.ek-stage.s-happy .ek-body3d { animation: ek-happyBounce .9s ease-in-out infinite; }
.ek-stage.s-wave .ek-body3d { animation: ek-waveGreet 1.1s ease-in-out infinite; }
.ek-stage.s-confused .ek-body3d { animation: ek-confusedWobble 2.2s ease-in-out infinite; }
.ek-body3d img { width: 100%; height: 100%; object-fit: contain; object-position: bottom; display: block; }
.ek-eye { position: absolute; width: 16.6%; height: 6.6%; background: #f7cfb6; border-radius: 50%; transform-origin: top center; transform: scaleY(0); box-shadow: inset 0 -3px 4px rgba(150,90,60,.28); animation: ek-blink 4.3s infinite; }
.ek-eye.l { left: 26.1%; top: 45.4%; }
.ek-eye.r { left: 63.4%; top: 44.9%; }

/* ===== chat (right) ===== */
.ek-chat { flex: 1; display: flex; flex-direction: column; min-width: 0; }
.ek-head { display: flex; align-items: center; gap: 11px; padding: 14px 16px; border-bottom: 1px solid #eef2ee; }
.ek-head .logo { width: 38px; height: 38px; border-radius: 11px; flex: none; display: flex; align-items: center; justify-content: center; background: linear-gradient(160deg,#41a85d,#2e8b4f); }
.ek-head .logo span { color: #ffd95e; font-size: 20px; line-height: 1; }
.ek-head .meta { min-width: 0; flex: 1; }
.ek-head .meta b { font-family: 'Baloo 2','Nunito',cursive; font-weight: 800; color: #1f6b3a; font-size: 16px; }
.ek-head .meta small { display: block; font-size: 12px; color: #7aa384; font-weight: 700; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ek-head .x { background: transparent; border: 0; color: #9fb6a6; font-size: 24px; line-height: 1; cursor: pointer; padding: 0 4px; font-family: inherit; }
.ek-head .x:hover { color: #5f7d68; }
.ek-head .x:focus-visible { outline: 2px solid #41a85d; outline-offset: 2px; border-radius: 4px; }

.ek-msgs { flex: 1; padding: 18px; display: flex; flex-direction: column; gap: 11px; overflow-y: auto; background: #f8fbf8; }
.msg { max-width: 82%; padding: 9px 13px; font-size: 14px; line-height: 1.45; font-weight: 600; white-space: pre-wrap; overflow-wrap: anywhere; }
.msg.bot { align-self: flex-start; background: #fff; color: #244a33; border: 1px solid #e6efe6; border-radius: 14px 14px 14px 4px; box-shadow: 0 2px 6px rgba(0,0,0,.04); }
.msg.user { align-self: flex-end; background: #2e8b4f; color: #fff; border-radius: 14px 14px 4px 14px; }
.msg.error { align-self: flex-start; background: #fff4e6; color: #8a5300; border: 1px solid #f3d6a8; border-radius: 14px 14px 14px 4px; }

.sources { margin-top: 8px; display: flex; flex-direction: column; gap: 4px; }
.sources a { font-size: 12.5px; color: #1f6b3a; text-decoration: none; font-weight: 700; overflow-wrap: anywhere; }
.sources a:hover { text-decoration: underline; }
.sources a .num { color: #2e8b4f; margin-right: 4px; }

.typing { align-self: flex-start; display: none; gap: 5px; background: #fff; border: 1px solid #e6efe6; padding: 11px 14px; border-radius: 14px 14px 14px 4px; }
.ek-msgs.thinking .typing { display: flex; }
.typing span { width: 7px; height: 7px; border-radius: 50%; background: #9fb0a4; animation: ek-dotJump 1.1s ease-in-out infinite; }
.typing span:nth-child(2) { animation-delay: .18s; }
.typing span:nth-child(3) { animation-delay: .36s; }

.ek-foot { display: flex; gap: 9px; padding: 13px 14px; border-top: 1px solid #eef2ee; background: #fff; }
.ek-foot input { flex: 1; min-width: 0; border: 1.5px solid #e2ece3; background: #f8fbf8; border-radius: 999px; padding: 10px 16px; font-family: 'Nunito',sans-serif; font-size: 14px; font-weight: 600; color: #234; outline: none; }
.ek-foot input:focus { border-color: #41a85d; }
.ek-foot button { flex: none; border: 0; cursor: pointer; width: 42px; height: 42px; border-radius: 50%; background: #2e8b4f; color: #fff; font-size: 18px; display: flex; align-items: center; justify-content: center; box-shadow: 0 6px 14px -4px rgba(46,139,79,.7); font-family: inherit; }
.ek-foot button:hover { background: #22703e; }
.ek-foot button:focus-visible { outline: 3px solid #ffd95e; outline-offset: 2px; }
.ek-foot button:disabled { opacity: .5; cursor: default; }

/* ===== responsive ===== */
@media (max-width: 620px) {
  .ek-panel { width: calc(100vw - 24px); height: calc(100dvh - 96px); right: 12px; bottom: 12px; }
  .ek-stage { width: 150px; }
  .ek-mascot { width: 104px; }
}
@media (max-width: 430px) {
  .ek-stage { display: none; }
}

@media (prefers-reduced-motion: reduce) {
  .ek-body3d, .ek-eye, .ek-pill .dot, .ek-bubble.think span, .typing span, .ek-panel.open { animation: none !important; }
}
`;
