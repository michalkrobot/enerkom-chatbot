#!/usr/bin/env python
"""Assemble a single self-contained standalone.html that opens by double-click
(file://) with NO server and NO internet: lottie-web, the mascot animation,
and the launcher avatar are all inlined.
"""
import os, base64, json

HERE = os.path.dirname(os.path.abspath(__file__))
A = os.path.join(HERE, "assets")

lottie_js = open(os.path.join(HERE, "vendor", "lottie_svg.min.js"), encoding="utf-8").read()
data_json = open(os.path.join(A, "elektron.json"), encoding="utf-8").read()
with open(os.path.join(A, "elektron-head.png"), "rb") as f:
    avatar = "data:image/png;base64," + base64.b64encode(f.read()).decode()

HTML = """<!doctype html>
<html lang="cs">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Elektron chat widget — demo</title>
<style>
  :root{ --dark:#0F6E56; --mid:#1D9E75; --light:#5DCAA5; --userbg:#E1F5EE; --ink:#143; }
  *{ box-sizing:border-box; }
  body{ font-family:system-ui,Segoe UI,sans-serif; margin:0; min-height:100vh;
        background:linear-gradient(135deg,#f3fbf8,#e6f4ee); color:var(--ink); }
  .page{ max-width:760px; margin:0 auto; padding:40px 20px; }
  .page h1{ color:var(--dark); } .page p{ color:#456; }
  .hint{ background:#fff; border:1px solid #e2efe9; border-radius:14px; padding:14px 18px;
         box-shadow:0 6px 20px rgba(15,110,86,.08); }
  .hint b{ color:var(--dark); }

  .ek-launch{ position:fixed; right:22px; bottom:22px; z-index:9999; display:flex;
    align-items:center; gap:10px; padding:8px 16px 8px 8px; background:var(--mid); color:#fff;
    border:0; border-radius:999px; cursor:pointer; box-shadow:0 10px 30px rgba(15,110,86,.35);
    font-size:15px; font-weight:600; }
  .ek-launch:hover{ background:var(--dark); }
  .ek-launch .av{ width:48px; height:48px; border-radius:50%;
    background:#fff url(__AVATAR__) no-repeat center; background-size:contain; }

  .ek-panel{ position:fixed; right:22px; bottom:22px; z-index:10000; width:374px;
    max-width:calc(100vw - 32px); height:560px; max-height:calc(100vh - 44px); background:#fff;
    border-radius:20px; overflow:hidden; display:none; flex-direction:column;
    box-shadow:0 24px 60px rgba(15,60,46,.30); }
  .ek-panel.open{ display:flex; }
  .ek-head{ position:relative; background:linear-gradient(135deg,var(--dark),var(--mid)); color:#fff;
    padding:0 18px; display:flex; align-items:flex-end; gap:14px; min-height:178px; }
  .ek-head .mascot{ width:82px; height:178px; flex:0 0 auto; }
  .ek-head .mascot > div{ width:100%; height:100%; }
  .ek-head .meta{ align-self:center; padding-bottom:20px; }
  .ek-head .meta b{ font-size:17px; } .ek-head .meta span{ font-size:13px; opacity:.9; }
  .ek-head .x{ position:absolute; top:12px; right:14px; background:transparent; border:0; color:#fff;
    font-size:24px; cursor:pointer; opacity:.85; line-height:1; } .ek-head .x:hover{ opacity:1; }
  .ek-body{ flex:1; overflow-y:auto; padding:16px; display:flex; flex-direction:column; gap:10px;
    background:#f6fcfa; }
  .msg{ max-width:82%; padding:9px 13px; border-radius:14px; font-size:14px; line-height:1.45;
    white-space:pre-wrap; word-wrap:break-word; }
  .msg.bot{ align-self:flex-start; background:#fff; border:1px solid #e2efe9; border-bottom-left-radius:5px; }
  .msg.user{ align-self:flex-end; background:var(--userbg); color:var(--dark); border-bottom-right-radius:5px; }
  .dots span{ display:inline-block; width:6px; height:6px; margin:0 2px; border-radius:50%;
    background:var(--mid); animation:bnc 1.2s infinite; }
  .dots span:nth-child(2){ animation-delay:.15s } .dots span:nth-child(3){ animation-delay:.3s }
  @keyframes bnc{ 0%,80%,100%{ transform:translateY(0); opacity:.4 } 40%{ transform:translateY(-5px); opacity:1 } }
  .ek-foot{ display:flex; gap:8px; padding:12px; border-top:1px solid #eef3f1; background:#fff; }
  .ek-foot input{ flex:1; border:1px solid #d7e6e0; border-radius:999px; padding:10px 14px;
    font-size:14px; outline:none; } .ek-foot input:focus{ border-color:var(--mid); }
  .ek-foot button{ border:0; border-radius:50%; width:42px; height:42px; background:var(--mid);
    color:#fff; font-size:18px; cursor:pointer; } .ek-foot button:hover{ background:var(--dark); }
</style>
</head>
<body>
  <div class="page">
    <h1>Demo: chat widget s maskotem Elektronem</h1>
    <div class="hint">
      <p>Klikněte na tlačítko <b>vpravo dole</b>. Maskot reaguje na průběh konverzace:
         <b>přemýšlí</b> při hledání odpovědi, <b>mluví</b> (mrká i otevírá pusu) během psaní,
         a vrací se do <b>klidu</b>.</p>
      <p style="margin-bottom:0">Zkuste: <i>„Co je energetická komunita?"</i>, <i>„Jak se zapojit?"</i>
         — a něco mimo téma (<i>„Jaké bude počasí?"</i>) pro stav <b>nenašel</b>.</p>
    </div>
    <p style="color:#789;font-size:13px">Samostatný soubor — vše inline (Lottie + animace + obrázek).
       Mock backend, žádné volání API. Produkční napojení: <code>src/mascot.ts</code> + SSE z <code>/api/chat</code>.</p>
  </div>

  <button class="ek-launch" id="ekLaunch"><span class="av"></span> Zeptejte se Elektrona</button>

  <div class="ek-panel" id="ekPanel" role="dialog" aria-label="Chat s Elektronem">
    <div class="ek-head">
      <div class="mascot"><div id="lottieHead"></div></div>
      <div class="meta"><b>Elektron</b><br><span>Energetická komunita Horní Pomoraví</span></div>
      <button class="x" id="ekClose" aria-label="Zavřít">&times;</button>
    </div>
    <div class="ek-body" id="ekBody"></div>
    <div class="ek-foot">
      <input id="ekInput" placeholder="Napište dotaz…" autocomplete="off">
      <button id="ekSend" aria-label="Odeslat">&#10148;</button>
    </div>
  </div>

<script>__LOTTIE__</script>
<script>
var ELEKTRON_DATA = __DATA__;

class Mascot {
  constructor(container, data){
    this.seg = (data.meta && data.meta.segments) ||
      {idle:[0,90],thinking:[90,195],talking:[195,285],notfound:[285,360]};
    this.state='idle';
    this.anim = lottie.loadAnimation({container, renderer:'svg', loop:true, autoplay:false, animationData:data});
    this.anim.addEventListener('complete', ()=>{ if(this.state==='notfound') this.play('idle'); });
    this.ready = new Promise(r=>this.anim.addEventListener('DOMLoaded',r)).then(()=>this.play('idle'));
  }
  set(s){ if(s!==this.state) this.play(s); }
  play(s){ this.state=s; this.anim.loop=s!=='notfound'; this.anim.playSegments(this.seg[s], true); }
  pause(){ this.anim.pause(); }
  resume(){ this.anim.resize(); this.play(this.state); }
}

const KB = [
  {q:['komunit','sdíl','energetik'], a:'Energetická komunita znamená, že vyrobenou elektřinu (např. ze střešní fotovoltaiky) sdílíte mezi sebou — domácnostmi, obcí i firmami v okolí. Snižuje to účty a posiluje místní soběstačnost. \\uD83C\\uDF1E'},
  {q:['připoj','zapoj','člen','vstoup'], a:'Zapojit se může každý odběratel v dané lokalitě. Stačí nám napsat a my vám vysvětlíme další kroky i potřebné dokumenty.'},
  {q:['fotovolt','panel','střech','výrob'], a:'Elektřinu vyrábějí fotovoltaické panely na střechách. Přebytky se přes komunitu sdílejí tam, kde je zrovna spotřeba.'},
];
function answer(q){ const s=q.toLowerCase(); const hit=KB.find(k=>k.q.some(w=>s.includes(w)));
  return hit ? {found:true, text:hit.a}
             : {found:false, text:'To bohužel přesně nevím. Zkuste dotaz přeformulovat, nebo nás kontaktujte na info@enerkomhp.cz a rádi poradíme.'}; }

const panel=document.getElementById('ekPanel'), body=document.getElementById('ekBody'), input=document.getElementById('ekInput');
let mascot, started=false;
mascot=new Mascot(document.getElementById('lottieHead'), ELEKTRON_DATA);
window.mascot=mascot;

function addMsg(t,who){ const d=document.createElement('div'); d.className='msg '+who; d.innerHTML=t;
  body.appendChild(d); body.scrollTop=body.scrollHeight; return d; }
function open(){ panel.classList.add('open'); input.focus(); mascot.resume();
  if(!started){ started=true; addMsg('Ahoj! Jsem Elektron. Zeptejte se mě na cokoli o naší energetické komunitě.','bot'); } }
function close(){ panel.classList.remove('open'); mascot.pause(); }
document.getElementById('ekLaunch').onclick=open;
document.getElementById('ekClose').onclick=close;
async function stream(el,text){ el.textContent=''; for(let i=0;i<text.length;i++){ el.textContent+=text[i];
  body.scrollTop=body.scrollHeight; await new Promise(r=>setTimeout(r,16)); } }
async function send(){ const q=input.value.trim(); if(!q) return; input.value=''; addMsg(q,'user');
  mascot.set('thinking');
  const typing=addMsg('<span class="dots"><span></span><span></span><span></span></span>','bot');
  await new Promise(r=>setTimeout(r,2200));
  const res=answer(q); typing.remove();
  mascot.set(res.found?'talking':'notfound');
  await stream(addMsg('','bot'), res.text);
  mascot.set('idle'); }
document.getElementById('ekSend').onclick=send;
input.addEventListener('keydown',e=>{ if(e.key==='Enter') send(); if(e.key==='Escape') close(); });
</script>
</body>
</html>
"""

out = (HTML
       .replace("__AVATAR__", avatar)
       .replace("__LOTTIE__", lottie_js)
       .replace("__DATA__", data_json))
dst = os.path.join(HERE, "standalone.html")
open(dst, "w", encoding="utf-8").write(out)
print("wrote", dst, "%.0f KB" % (os.path.getsize(dst) / 1024))
