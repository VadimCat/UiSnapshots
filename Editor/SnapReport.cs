using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>Writes a self-contained report.html next to the shots (images are referenced relatively).</summary>
    public static class SnapReport
    {
        public const string FileName = "report.html";

        public static string Write(string runDir, SnapManifest manifest, SnapCompareResult compare = null)
        {
            compare ??= LoadCompare(runDir);
            var data = new JObject
            {
                ["manifest"] = JObject.FromObject(manifest, JsonSerializer.Create(SnapSettings.Json)),
                ["compare"] = compare != null ? JObject.FromObject(compare, JsonSerializer.Create(SnapSettings.Json)) : null
            };
            string json = data.ToString(Formatting.None).Replace("</", "<\\/");
            string path = Path.Combine(runDir, FileName);
            File.WriteAllText(path, Template.Replace("/*DATA*/null", json));
            return path;
        }

        private static SnapCompareResult LoadCompare(string runDir)
        {
            string path = Path.Combine(runDir, SnapCompareResult.FileName);
            return File.Exists(path) ? JsonConvert.DeserializeObject<SnapCompareResult>(File.ReadAllText(path)) : null;
        }

        private const string Template = @"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>UI Snapshots</title>
<style>
:root{--bg:#f6f5f2;--card:#fff;--ink:#1d1d1b;--quiet:#6b6a66;--line:#e2e0da;--accent:#2f6fdb;--bad:#d64040;--warn:#c98a12;--ok:#3a8a4a}
@media (prefers-color-scheme:dark){:root{--bg:#181816;--card:#232320;--ink:#ecebe6;--quiet:#a3a19a;--line:#3a3934;--accent:#7aa7ff;--bad:#ff7070;--warn:#e6b450;--ok:#6cc27e}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 system-ui,-apple-system,Segoe UI,sans-serif}
header{padding:20px 24px 8px}h1{font-size:20px;margin:0 0 6px}.meta{color:var(--quiet);font-size:13px}.meta b{color:var(--ink);font-weight:600}
.bar{position:sticky;top:0;z-index:2;background:var(--bg);border-bottom:1px solid var(--line);padding:10px 24px;display:flex;gap:10px;flex-wrap:wrap;align-items:center}
select,input,button{font:inherit;color:var(--ink);background:var(--card);border:1px solid var(--line);border-radius:8px;padding:5px 9px}
button.on{border-color:var(--accent);color:var(--accent)}
.chip{display:inline-block;padding:1px 8px;border-radius:99px;font-size:12px;border:1px solid var(--line);margin-right:4px}
.chip.bad{color:var(--bad);border-color:var(--bad)}.chip.warn{color:var(--warn);border-color:var(--warn)}.chip.ok{color:var(--ok);border-color:var(--ok)}
main{padding:12px 24px 48px}.issues{margin:0 0 16px;padding:12px 16px;background:var(--card);border:1px solid var(--line);border-radius:10px}
.issues li{margin:3px 0}.group{margin:22px 0 8px;font-size:15px;font-weight:600}
.row{display:flex;gap:14px;overflow-x:auto;padding-bottom:6px}
.card{flex:0 0 auto;background:var(--card);border:1px solid var(--line);border-radius:10px;padding:8px;width:var(--w,220px)}
.card h3{font-size:12px;font-weight:600;margin:0 0 6px;display:flex;justify-content:space-between;gap:6px}
.card img{width:100%;display:block;border-radius:6px;cursor:zoom-in;background:#0001}
.pairs{display:grid;grid-template-columns:1fr 1fr;gap:6px}.pairs span{font-size:11px;color:var(--quiet)}
.slider{position:relative;overflow:hidden;border-radius:6px}.slider img{position:absolute;top:0;left:0}.slider img.base{position:relative}
.slider .top{clip-path:inset(0 0 0 var(--cut,50%))}.slider input{width:100%;margin-top:4px}
.small{font-size:11px;color:var(--quiet);margin-top:4px;word-break:break-word}
#zoom{position:fixed;inset:0;background:#000c;display:none;align-items:center;justify-content:center;z-index:5}#zoom img{max-width:96vw;max-height:96vh}
</style></head><body>
<header><h1 id=""title"">UI Snapshots</h1><div class=""meta"" id=""meta""></div></header>
<div class=""bar"">
 <select id=""fScenario""></select><select id=""fSkin""></select><select id=""fViewport""></select>
 <select id=""fStatus"" hidden></select>
 <span id=""modes""></span>
 <label class=""meta"">size <input id=""size"" type=""range"" min=""140"" max=""520"" value=""220""></label>
</div>
<main id=""main""></main>
<div id=""zoom"" onclick=""this.style.display='none'""><img></div>
<script>
const D=/*DATA*/null, M=D.manifest, C=D.compare;
const $=s=>document.querySelector(s), esc=s=>String(s??'').replace(/[&<>""]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;'}[c]));
let mode=C?'side':'shots';
$('#title').textContent='UI Snapshots · '+(M.Label||M.RunId);
const refs=M.Shots.filter(s=>s.Reference),refBad=refs.filter(s=>s.ReferenceStatus!=='same');
const errors=M.Issues.filter(i=>i.Severity==='error'),warns=M.Issues.filter(i=>i.Severity!=='error'),unsettled=M.Shots.filter(s=>!s.Settled),exp=M.Expectations.filter(e=>!e.Passed);
$('#meta').innerHTML=`<b>${esc(M.Status)}</b> · ${M.Shots.length} shots · ${esc(M.Branch)} @ <b>${esc((M.Commit||'').slice(0,10))}</b> (${esc(M.DirtyHash)}) · Unity ${esc(M.Unity)} · ${esc(M.StartedUtc)} · ${M.Seconds}s`+
 (C?`<br>compared with <b>${esc(C.BaselineBranch)} @ ${esc((C.BaselineCommit||'').slice(0,10))}</b>: ${esc(summary())}`:'')+
 `<br>${errors.length?`<span class=""chip bad"">${errors.length} errors</span>`:''}${warns.length?`<span class=""chip warn"">${warns.length} warnings</span>`:''}${unsettled.length?`<span class=""chip warn"">${unsettled.length} unsettled</span>`:''}${exp.length?`<span class=""chip warn"">${exp.length} failed expects</span>`:''}${M.UncapturedPoints.length?`<span class=""chip"">${M.UncapturedPoints.length} uncaptured markers</span>`:''}${refs.length?`<span class=""chip ${refBad.length?'warn':'ok'}"">${refs.length} vs reference, ${refBad.length} differ</span>`:''}`;
function summary(){const n=s=>C.Pairs.filter(p=>p.Status===s).length;return `${n('changed')} changed, ${n('resized')} resized, ${n('added')} added, ${n('removed')} removed, ${n('same')} same`}
function opts(id,label,values){const el=$(id);el.innerHTML=`<option value="""">${label}: all</option>`+[...new Set(values)].filter(v=>v!==undefined).map(v=>`<option>${esc(v)}</option>`).join('');el.onchange=render}
opts('#fScenario','scenario',M.Shots.map(s=>s.Scenario));opts('#fSkin','skin',M.Shots.map(s=>s.Skin||'default'));opts('#fViewport','viewport',M.Shots.map(s=>s.Viewport));
if(C){const st=$('#fStatus');st.hidden=false;st.innerHTML='<option value="""">status: all</option><option>changed</option><option>resized</option><option>added</option><option>removed</option><option>same</option>';st.value='';st.onchange=render;
 $('#modes').innerHTML=['side','slider','diff','shots'].map(m=>`<button data-m=""${m}"">${m}</button>`).join(' ');
 document.querySelectorAll('#modes button').forEach(b=>b.onclick=()=>{mode=b.dataset.m;render()})}
$('#size').oninput=e=>document.documentElement.style.setProperty('--w',e.target.value+'px');
function zoom(src){const z=$('#zoom');z.querySelector('img').src=src;z.style.display='flex'}
function parse(key){const m=key.match(/^(.*?)\/(.*)@(.*?)(?:-(.*))?$/);return m?{scenario:m[1],capture:m[2],viewport:m[3],skin:m[4]||'default'}:{}}
function pass(k){const f=parse(k);return (!$('#fScenario').value||f.scenario===$('#fScenario').value)&&(!$('#fSkin').value||f.skin===$('#fSkin').value)&&(!$('#fViewport').value||f.viewport===$('#fViewport').value)}
function refBlock(s){const cls=s.ReferenceStatus==='same'?'ok':s.ReferenceStatus==='changed'?'warn':'';
 return `<div class=""small""><span class=""chip ${cls}"">${esc(s.ReferenceKind)} reference: ${esc(s.ReferenceStatus)}${s.ReferenceStatus==='changed'?' '+s.ReferenceChangedPercent+'%':''}</span></div>`+
 `<div class=""slider"" style=""margin-top:4px""><img class=""base"" src=""${esc(s.Reference)}"" style=""width:100%""><img class=""top"" src=""${esc(s.File)}"" style=""width:100%;height:100%;object-fit:fill""></div><input type=""range"" value=""50"" oninput=""this.previousElementSibling.querySelector('.top').style.setProperty('--cut',this.value+'%')"" title=""left: reference · right: frame"">`+
 (s.ReferenceDiff?`<img loading=""lazy"" style=""margin-top:4px"" src=""${esc(s.ReferenceDiff)}"" onclick=""zoom(this.src)"" title=""diff vs reference"">`:'')}
function render(){
 document.querySelectorAll('#modes button').forEach(b=>b.classList.toggle('on',b.dataset.m===mode));
 let html='';
 const issues=[...M.Issues.map(i=>`<li><span class=""chip ${i.Severity==='error'?'bad':'warn'}"">${esc(i.Severity)}</span>${esc(i.Scenario||'')} ${esc(i.Skin||'')} ${esc(i.Step||'')}: ${esc(i.Message)}</li>`),
  ...exp.map(e=>`<li><span class=""chip warn"">expect</span>${esc(e.Scenario)} ${esc(e.Step)}: ${esc(e.What)} (actual: ${esc(e.Actual)})</li>`),
  ...M.UnreachedCaptures.map(u=>`<li><span class=""chip warn"">unreached</span>${esc(u)}</li>`),
  ...(M.UncapturedPoints.length?[`<li><span class=""chip"">markers fired but not captured</span>${esc([...new Set(M.UncapturedPoints.map(p=>p.Id))].join(', '))}</li>`]:[])];
 if(issues.length)html+=`<ul class=""issues"">${issues.join('')}</ul>`;
 if(mode==='shots'||!C){
  const groups={};M.Shots.filter(s=>pass(s.Key)).forEach(s=>{(groups[s.Scenario+' · '+s.Capture]??=[]).push(s)});
  for(const [g,shots] of Object.entries(groups)){html+=`<div class=""group"">${esc(g)}</div><div class=""row"">`+shots.map(s=>`<div class=""card""><h3><span>${esc(s.Viewport)}${s.Skin?' · '+esc(s.Skin):''}</span>${s.Settled?'':'<span class=""chip warn"">unsettled</span>'}</h3><img loading=""lazy"" src=""${esc(s.File)}"" onclick=""zoom(this.src)"">${s.FocusFile?`<img loading=""lazy"" style=""margin-top:6px"" src=""${esc(s.FocusFile)}"" onclick=""zoom(this.src)"">`:''}${s.Reference?refBlock(s):''}<div class=""small"">${s.Width}×${s.Height} · insets ${s.InsetTop}/${s.InsetBottom}${s.Point?' · '+esc(s.Point):''}${s.Warnings.length?'<br>'+esc(s.Warnings.join(' ')):''}</div></div>`).join('')+'</div>'}
 } else {
  const st=$('#fStatus').value;const order={changed:0,resized:1,added:2,removed:3,same:4};
  const pairs=C.Pairs.filter(p=>pass(p.Key)&&(!st||p.Status===st)).sort((a,b)=>order[a.Status]-order[b.Status]||b.ChangedPercent-a.ChangedPercent);
  html+=`<div class=""row"" style=""flex-wrap:wrap"">`+pairs.map(p=>{const cls=p.Status==='same'?'ok':p.Status==='changed'?'warn':'bad';let body='';
   if(mode==='slider'&&p.Before&&p.After)body=`<div class=""slider""><img class=""base"" src=""${esc(p.Before)}""><img class=""top"" src=""${esc(p.After)}""></div><input type=""range"" value=""50"" oninput=""this.previousElementSibling.querySelector('.top').style.setProperty('--cut',this.value+'%')"">`;
   else if(mode==='diff'&&p.Diff)body=`<img src=""${esc(p.Diff)}"" onclick=""zoom(this.src)"">`;
   else body=`<div class=""pairs""><div><span>before</span>${p.Before?`<img src=""${esc(p.Before)}"" onclick=""zoom(this.src)"">`:'—'}</div><div><span>after</span>${p.After?`<img src=""${esc(p.After)}"" onclick=""zoom(this.src)"">`:'—'}</div></div>`;
   return `<div class=""card"" style=""width:calc(var(--w,220px)*${mode==='side'?2:1})""><h3><span>${esc(p.Key)}</span><span class=""chip ${cls}"">${p.Status}${p.Status==='changed'?' '+p.ChangedPercent+'%':''}</span></h3>${body}</div>`}).join('')+'</div>';
 }
 $('#main').innerHTML=html;
}
render();
</script></body></html>";
    }
}
