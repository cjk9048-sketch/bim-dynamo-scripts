namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0901] 브라우저에 내주는 <b>지도 한 장</b>.
///
/// <para><b>지도는 VWorld</b>(국토부) 타일이다 — 이 애드인이 이미 위성영상을 거기서 받고 있어
/// 키가 하나뿐이고 약관도 정리돼 있다. <b>항공사진·일반지도·지적도</b>를 켜고 끌 수 있다.</para>
///
/// <para>★<b>지적도는 WMTS가 아니라 WMS</b>다(검토 0901). 예전에는 <c>Hybrid</c>를 켜 놓고
/// "지적도"라고 적었는데 그건 <b>지명·도로 글씨</b> 겹침이다 — 필지 경계를 보려고 켠 사람이
/// 아무것도 못 보고 눈대중으로 박스를 친다. 이제 둘을 따로 둔다.</para>
///
/// <para><b>박스는 클릭 두 번</b>으로 친다 — 끌어서 그리면 지도 이동과 부딪힌다.
/// 첫 클릭이 한 모서리, 두 번째가 반대 모서리다. 다시 찍으면 새로 시작한다.</para>
///
/// <para><b>돌려주는 것은 위경도 네 값뿐</b>이다. 타일 그림은 안 가져간다 —
/// 그건 약관 문제이기도 하고, 어차피 [배경지도]가 정식 경로로 한다.</para></summary>
internal static class MapPage
{
    /// <summary>VWorld 키 — 위성영상 타일이 쓰는 것과 <b>같은 키</b>다.
    /// <para>★<see cref="VWorldImagery"/>가 들고 있는 것을 그대로 쓴다(§50: 키를 두 곳에 적지 않는다).</para></summary>
    private static string Key => VWorldKey.Current;

    /// <param name="cm">도면 좌표계의 중앙자오선(도) — 지도를 <b>그 근처</b>에서 시작한다.</param>
    /// <param name="token">이 주소에 붙은 한 번 쓰는 표 — 되돌려 보낼 때 같이 보낸다.
    ///   도킹바(<paramref name="embedded"/>)에서는 안 쓴다.</param>
    /// <param name="embedded">★도킹바 안(WebView2)인가.
    ///   <para>맞으면 <b>서버가 아예 없다</b> — 옆방에 말 걸듯 바로 건네준다
    ///   (<c>chrome.webview.postMessage</c>). 포트도 표도 프록시도 걸릴 것이 없다.</para></param>
    internal static string Build(double cm, string token, bool embedded = false)
    {
        // 처음 보여 줄 자리 — 도면 좌표계의 원점 부근. 위도는 남한 한가운데쯤.
        string lon0 = cm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return HTML.Replace("__LON0__", lon0)
                   // ★[v109 · 검토 M7] 도킹바(embedded)는 키를 페이지 글에 안 넣는다 — 이 글은 PC 폴더에 index.html로 써지기 때문.
                   //   키는 WebView2가 문서를 만들 때 메모리에서 넣는다(window.__DHKEY · MapPanel). 브라우저 길(MapPick)은 메모리 서버라 그대로.
                   .Replace("__KEY__", embedded ? "" : Esc(Key))
                   .Replace("__TOK__", Esc(token))
                   .Replace("__EMBED__", embedded ? "1" : "0");
    }

    /// <summary>따옴표가 섞여 들어와 페이지가 깨지는 것을 막는다 — 키는 우리 것이지만 값은 값이다.</summary>
    private static string Esc(string s) =>
        (s ?? "").Replace("\\", "").Replace("'", "").Replace("\"", "").Replace("<", "");

    // ── 페이지 본문 ────────────────────────────────────────────────────────────
    //   ★Leaflet은 CDN에서 받는다. 어차피 지도 타일도 인터넷이 있어야 하므로
    //     새로 생기는 제약이 아니다. 못 받으면 아래 안내가 대신 뜬다.
    //   ★★그때도 [그만두기]는 살아 있어야 한다 — 사내망이 CDN만 막는 자리가 흔하고,
    //     단추가 죽어 있으면 CAD 쪽이 한도까지 기다린다(검토 0901).
    private const string HTML = @"<!doctype html>
<html lang='ko'><head><meta charset='utf-8'>
<title>DH 지층·정지 — 지도에서 범위 고르기</title>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'
      integrity='sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=' crossorigin='anonymous'>
<style>
  html,body{margin:0;height:100%;font-family:'맑은 고딕',Malgun Gothic,sans-serif}
  /* ★막대가 몇 줄로 접히든 지도가 그만큼 줄어든다 — 52px을 박아 두면 좁은 도킹바에서
     막대가 지도 위를 덮고, 그 자리는 클릭도 안 먹는다(검토 0901). */
  body{display:flex;flex-direction:column}
  /* ★★★[JACK 0902 — 창 크기 때문에 계속 윗줄됐다 아랫줄됐다 하는데
     그냥 깔끔하게 아랫줄로 넣어]
     종전엔 막대가 <b>한 줄짜리 flex + wrap</b>이라 도킹바 폭이 조금만 바뀌어도
     뒤쪽 셋(범위없음·가져오기·그만두기)이 <b>윗줄과 아랫줄을 오갔다</b>.
     → 막대를 <b>세로 2줄</b>로 나누고 줄마다 따로 접게 한다.
     높이는 여전히 <c>flex:0 0 auto</c>라 지도가 그만큼 줄어든다(막대가 지도를 안 덮는다). */
  #bar{flex:0 0 auto;background:#1f2430;color:#eaeef7;
       padding:9px 14px;display:flex;flex-direction:column;gap:8px;font-size:14px}
  #bar .row{display:flex;gap:10px;align-items:center;flex-wrap:wrap}
  #bar b{color:#8ab4ff}
  #map{flex:1 1 auto;min-height:0;background:#111}
  /* ★★★[JACK 0902 — 점 찍을려고 하는데 마우스가 손바닥 모양이라 좀 매칭이 안 되는데.
     보통 손바닥 모양 마우스는 이동할때쟐나]
     Leaflet 기본 커서는 <b>손바닥(grab)</b>이다 — 그건 <b>지도를 끔다</b>는 뜻이라
     모서리를 <b>찍는</b> 일과 뜻이 안 맞는다.
     → 찍는 동안에만 <b>십자</b>로 바꾸고, 두 점을 다 찍으면 손바닥으로 돌아온다. */
  #map.pick,
  #map.pick .leaflet-container,
  #map.pick .leaflet-grab,
  #map.pick .leaflet-interactive{cursor:crosshair}
  button{font:inherit;padding:6px 12px;border-radius:6px;border:1px solid #4a5268;
         background:#2b3242;color:#eaeef7;cursor:pointer}
  button.go{background:#2f6fed;border-color:#2f6fed;font-weight:bold}
  button:disabled{opacity:.45;cursor:default}
  #info{color:#c9d3e8}   /* 아랫줄 맨 앞이라 오른쪽으로 밀던 margin-left:auto 는 뺀다 */
  #slist div{padding:6px 10px;cursor:pointer;border-bottom:1px solid #3a4258}
  #slist div:hover{background:#3b4764}
  #noweb{display:none;flex:1 1 auto;background:#fff;
         padding:24px;font-size:15px;line-height:1.7}
</style></head><body>
<div id='bar'>
 <div class='row' id='srow' style='display:none;position:relative'>
  <input id='q' type='text' placeholder='주소 · 지번 · 장소 검색' style='flex:1 1 140px;min-width:0;padding:6px 8px;border-radius:6px;border:1px solid #4a5268;background:#11151d;color:#eaeef7;font:inherit'>
  <button id='sbtn'>검색</button>
  <span id='smsg' style='color:#ffb020'></span>
  <div id='slist' style='display:none;position:absolute;left:0;right:0;top:100%;z-index:1000;background:#2b3242;border:1px solid #4a5268;border-radius:6px;max-height:220px;overflow:auto'></div>
 </div>
 <div class='row'>
  <label><input type='radio' name='mode' value='box' checked> 네모</label>
  <label id='mpoly' style='display:none'><input type='radio' name='mode' value='poly'> 다각형</label>
  <span id='hint'><b>모서리 2곳</b> 클릭</span>
  <button id='clr'>다시 찍기</button>
  <button id='rt' style='display:none' title='도면의 노선(폴리선 · 선 · 호 · 선형) + 폭 → 띠'>도면 노선</button>
  <button id='cl' style='display:none' title='도면의 닫힌 폴리선'>도면 닫힌 선</button>
  <label title='지도에만 표시 · 도면에는 안 들어옴'><input type='checkbox' id='cadview'> 지적 보기</label>
  <span id='cadmsg' style='color:#ffb020;display:none'>확대시 표시</span>
  <label title='같은 범위의 지적도를 도면으로 가져옴'><input type='checkbox' id='cad'> 지적도 가져오기</label>
  <label><input type='checkbox' id='lbl' checked> 지명·도로</label>
  <label><input type='radio' name='bm' value='Satellite' checked> 항공사진</label>
  <label><input type='radio' name='bm' value='Base'> 일반지도</label>
 </div>
 <div class='row'>
  <span id='info'>범위 없음</span>
  <button class='go' id='send' disabled>이 범위 가져오기</button>
  <button id='cancel'>그만두기</button>
 </div>
</div>
<div id='map'></div>
<div id='noweb'>
  <b>지도 로드 실패</b><br>인터넷 · 사내 방화벽 확인<br><br>
  [그만두기]로 닫으세요.
</div>
<script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'
        integrity='sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=' crossorigin='anonymous'></script>
<script>
(function(){
  var BASE='/__TOK__', EMBED=(__EMBED__===1);
  // ★도킹바 안이면 서버가 없다 — 창틀에 바로 건넨다. 밖이면 예전처럼 인터폰으로 보낸다.
  function tell(kind,payload,onOk,onFail){
    if(EMBED){
      try{ window.chrome.webview.postMessage(JSON.stringify({kind:kind,box:payload||null})); onOk&&onOk(); }
      catch(e){ onFail&&onFail(); }
      return;
    }
    var u=BASE+(kind==='box'?'/box':'/cancel');
    var o=(kind==='box')?{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)}:undefined;
    fetch(u,o).then(function(){onOk&&onOk();}).catch(function(){onFail&&onFail();});
  }
  var info=document.getElementById('info'), send=document.getElementById('send');

  // ★★[그만두기]를 <b>맨 먼저</b> 단다 — Leaflet이 안 와도 이건 살아 있어야 한다.
  var quit=false;
  document.getElementById('cancel').addEventListener('click',function(){
    quit=true; send.disabled=true;
    var msg=EMBED?'취소됨':'취소됨 · 창 닫으세요';
    tell('cancel',null,function(){info.textContent=msg;},function(){info.textContent=msg;});
  });

  // ★★[튜토리얼 · JACK 1117] CAD가 'tut:{on,…}'으로 켜면 — 어둡게 덮고 할 곳만 뚫는다(클릭도 그 자리만).
  //   작은 단계: 1 검색 칸 → 2 지도(모서리 두 곳) → 3 [이 범위 가져오기] · 0 보내는 중(덮개 걷음). 바뀌면 CAD에 'sub'로 알린다.
  //   Leaflet이 안 와도 살아 있어야 한다(그래서 여기 — 아래 return 앞).
  var TS={on:false,found:false,sending:false,t:[],base:2,total:10,skip:'',quit:'',sub:-1,el:null,sig:'',min:0,small:''};
  function tutPost(ev,x){ if(!EMBED) return; var o={kind:'tut',ev:ev}; if(x) for(var k in x) o[k]=x[k];
    try{ window.chrome.webview.postMessage(JSON.stringify(o)); }catch(e){} }
  function tutDom(){
    if(TS.el) return TS.el;
    var css=document.createElement('style');
    css.textContent='.tutd{position:fixed;background:rgba(10,14,22,.62);z-index:5000}'+
      '#tutg{position:fixed;z-index:5001;pointer-events:none;border:3px solid #ffb020;border-radius:8px;'+
      'box-shadow:0 0 0 3px rgba(255,176,32,.35),0 0 18px 4px rgba(255,176,32,.8);animation:tutp 1.2s ease-in-out infinite}'+
      '@keyframes tutp{0%,100%{opacity:1}50%{opacity:.4}}'+
      '#tutb{position:fixed;z-index:5002;max-width:330px;background:#0a6269;color:#fff;border:2px solid #a8e0e3;border-radius:10px;'+
      'padding:9px 12px;font-size:14px;font-weight:600;line-height:1.45;box-shadow:0 6px 18px rgba(0,0,0,.45)}'+
      '#tutb .n{display:inline-block;padding:0 8px;border-radius:10px;background:#ffb020;color:#1f2430;font-weight:bold;margin-right:6px}'+
      '#tutb .r{margin-top:8px;display:flex;gap:6px;justify-content:flex-end}'+
      '#tutb button{padding:3px 10px;font-size:12px;background:#11474c;border-color:#5fb3b8}';
    document.head.appendChild(css);
    var d=[0,1,2,3].map(function(){ var x=document.createElement('div'); x.className='tutd'; document.body.appendChild(x); return x; });
    var g=document.createElement('div'); g.id='tutg'; document.body.appendChild(g);
    var b=document.createElement('div'); b.id='tutb'; document.body.appendChild(b);
    TS.el={d:d,g:g,b:b}; return TS.el;
  }
  function tutHide(){ if(!TS.el) return; TS.el.d.forEach(function(x){x.style.display='none';}); TS.el.g.style.display='none'; TS.el.b.style.display='none'; }
  function tutBalloon(idx,note){
    var el=tutDom(), b=el.b, sig=idx+'|'+(note||'')+'|'+TS.t.join('|');
    if(TS.sig===sig) return; TS.sig=sig;
    b.innerHTML='';
    var top=document.createElement('div'), n=document.createElement('span'); n.className='n';
    n.textContent=(TS.base+idx-1)+' / '+TS.total; top.appendChild(n);
    top.appendChild(document.createTextNode(TS.t[idx-1]||'')); b.appendChild(top);
    if(note){ var nt=document.createElement('div'); nt.style.cssText='color:#ffb020;margin-top:5px;font-size:13px'; nt.textContent=note; b.appendChild(nt); }
    var r=document.createElement('div'); r.className='r';
    if(idx===1&&TS.skip){ var s=document.createElement('button'); s.textContent=TS.skip;
      s.addEventListener('click',function(){ TS.found=true; tutTick(); }); r.appendChild(s); }
    var q=document.createElement('button'); q.textContent=TS.quit||'그만두기';
    q.addEventListener('click',function(){ tutPost('quit'); }); r.appendChild(q);
    b.appendChild(r);
  }
  function tutTick(){
    if(!TS.on){ tutHide(); TS.sub=-1; return; }
    // ★[JACK 1117 «너무 작게 가져와서 에러»] 고른 범위가 TS.min(m)보다 작으면 3으로 안 넘어간다 — 말풍선에 «범위 작음»
    var small=false;
    if(box&&TS.min>0){ var kLon=111320*Math.cos((box.minLat+box.maxLat)/2*Math.PI/180);
      small=((box.maxLon-box.minLon)*kLon<TS.min)||((box.maxLat-box.minLat)*111320<TS.min); }
    var idx=TS.sending?0:((box&&!small)?3:((TS.found||p1||box)?2:1));
    if(idx!==TS.sub){ TS.sub=idx; tutPost('sub',{n:idx}); }
    if(idx===0){ tutHide(); return; }
    var ids=idx===1?['srow','slist']:(idx===2?['map']:['send']);
    var L0=1e9,T0=1e9,R0=-1e9,B0=-1e9;
    ids.forEach(function(id){ var e=document.getElementById(id); if(!e||e.style.display==='none') return;
      var r=e.getBoundingClientRect(); if(r.width<1||r.height<1) return;
      L0=Math.min(L0,r.left);T0=Math.min(T0,r.top);R0=Math.max(R0,r.right);B0=Math.max(B0,r.bottom); });
    if(R0<L0){ tutHide(); return; }
    var p=4; L0-=p;T0-=p;R0+=p;B0+=p;
    var W=window.innerWidth,H=window.innerHeight,el=tutDom(),d=el.d;
    function put(x,l,t,w,h){ x.style.display='block'; x.style.left=l+'px'; x.style.top=t+'px'; x.style.width=Math.max(0,w)+'px'; x.style.height=Math.max(0,h)+'px'; }
    put(d[0],0,0,W,T0); put(d[1],0,B0,W,H-B0); put(d[2],0,T0,L0,B0-T0); put(d[3],R0,T0,W-R0,B0-T0);
    put(el.g,L0,T0,R0-L0,B0-T0);
    tutBalloon(idx,(idx===2&&small)?TS.small:'');
    var b=el.b; b.style.display='block';
    var bw=b.offsetWidth,bh=b.offsetHeight,bl,bt;
    if(idx===2){ bl=L0+12; bt=T0+12; }
    else { bl=Math.max(8,Math.min(L0,W-bw-8)); bt=(B0+10+bh<H)?B0+10:Math.max(8,T0-bh-10); }
    b.style.left=bl+'px'; b.style.top=bt+'px';
  }
  if(EMBED && window.chrome && window.chrome.webview){
    window.chrome.webview.addEventListener('message',function(ev){
      var m=(typeof ev.data==='string')?ev.data:'';
      if(m.indexOf('tut:')!==0) return;
      var o=null; try{ o=JSON.parse(m.substring(4)); }catch(e){}
      if(!o) return;
      TS.on=!!o.on; if(o.t) TS.t=o.t; if(o.base) TS.base=o.base; if(o.total) TS.total=o.total;
      if(o.skip) TS.skip=o.skip; if(o.quit) TS.quit=o.quit; if(o.min) TS.min=o.min; if(o.small) TS.small=o.small; TS.sig=''; TS.sub=-1;
      tutTick(); tutPost('ack');
    });
    document.addEventListener('keydown',function(e){ if(TS.on&&e.key==='Escape'&&!p1) tutPost('quit'); });   // 첫 점 뒤 Esc = 찍기 취소(튜토리얼은 그대로)
    setInterval(tutTick,250);
    tutPost('ready');
  }

  function dead(msg){
    document.getElementById('map').style.display='none';
    document.getElementById('noweb').style.display='block';
    send.disabled=true;
    if(msg) info.textContent=msg;
    if(TS.on){ TS.on=false; tutHide(); }
    tutPost('dead',{msg:msg||''});   // ★[튜토리얼] 덮개를 걷고 CAD 말풍선이 까닭을 말한다
  }
  if (typeof L === 'undefined') { dead('지도 로드 실패'); return; }

  var KEY=(window.__DHKEY||'__KEY__');
  if (!KEY) { dead('VWorld 키가 없습니다 — [공통설정]에서 키를 넣으세요'); return; }
  var map=L.map('map',{center:[36.5,__LON0__],zoom:8});
  function tiles(layer,ext){
    return L.tileLayer('https://api.vworld.kr/req/wmts/1.0.0/'+KEY+'/'+layer+'/{z}/{y}/{x}.'+ext,
      {maxZoom:19,attribution:'© VWorld (국토교통부)'});
  }
  // ★★★[JACK 0901 ""지도가 안 나와 검은 화면이야""]
  //   <b>원인: 도킹바 안에서는 크기가 0일 때 페이지가 먼저 뜬다.</b> Leaflet은 만들어질 때의
  //   창 크기로 깔 타일을 정하는데 0×0이면 <b>한 장도 안 깐다</b>. 나중에 창이 커져도
  //   스스로 다시 세지 않아 배경색(#111)만 남는다 — 브라우저에서는 처음부터 크기가 있어 안 났다.
  //   → 크기가 바뀔 때마다 <b>다시 재라고</b> 알려 준다.
  function fit(){ try{ map.invalidateSize(false); }catch(e){} }
  window.addEventListener('resize',fit);
  try{ if(window.ResizeObserver) new ResizeObserver(fit).observe(document.getElementById('map')); }catch(e){}
  setTimeout(fit,100); setTimeout(fit,500); setTimeout(fit,1500);

  var base={Satellite:tiles('Satellite','jpeg'),Base:tiles('Base','png')};
  var cur=base.Satellite.addTo(map);

  // 지명·도로 글씨 겹침(Hybrid) — 이건 지적도가 아니다.
  var labels=tiles('Hybrid','png').addTo(map);

  // ★진짜 연속지적도 — WMTS에는 없고 WMS로만 나온다.
  // ★★[재측정 0901 — JACK ""확대시 표시가 너무 빨리 없어져""]
  //   <b>앞의 측정이 틀렸다.</b> 14~16단계에서 오는 1,784바이트를 '지적선'으로 읽었는데,
  //   네 단계가 <b>바이트까지 똑같은 그림</b>이었다 — 자리와 무관한 안내 그림이지 지적선이 아니다.
  //   지문(sha256)을 찍어 보고서야 갈렸다. <b>크기만 보고 '있다'고 하면 안 된다.</b>
  //
  //   그리고 켜고 있던 것은 <b>부번 필지뿐</b>이었다 — 본번을 같이 켜면 필지가 더 나오고(z18에서
  //   7.7KB → 10.5KB), 시작 단계도 18 → <b>17</b>로 한 칸 내려간다.
  var CADZ=17;
  var CADL='lp_pa_cbnd_bonbun,lp_pa_cbnd_bubun';   // 본번 + 부번
  var cadastral=L.tileLayer.wms('https://api.vworld.kr/req/wms?',{
    layers:CADL, styles:CADL,
    format:'image/png', transparent:true, version:'1.3.0',
    key:KEY, domain:location.origin, minZoom:CADZ, maxZoom:19,
    attribution:'지적 © VWorld'
  });
  // 켰는데 아직 안 보이는 구간이면 <b>왜 안 보이는지</b> 한마디로 알린다.
  function cadMsg(){
    var on=document.getElementById('cadview').checked;
    document.getElementById('cadmsg').style.display=(on&&map.getZoom()<CADZ)?'inline':'none';
  }

  // ★★타일이 전부 막히면 <b>까만 네모</b>가 남는데, 그걸 밤바다로 알고 눈대중으로 찍는다.
  //   몇 장 연달아 실패하면 대놓고 말한다(검토 0901).
  var bad=0, good=0, told=false;
  function say(m){ if(EMBED){ try{ window.chrome.webview.postMessage(JSON.stringify({kind:'diag',text:m})); }catch(e){} } }
  function watch(l){ l.on('tileerror',function(){ bad++; if(bad>=6&&good===0) dead('타일 수신 실패 — 인터넷 또는 VWorld 키([공통설정] → 키 시험) 확인'); }); }
  watch(base.Satellite); watch(base.Base); watch(labels);
  function ok(l){ l.on('tileload',function(){ good++; bad=0; }); }
  ok(base.Satellite); ok(base.Base); ok(labels);
  // ★4초 뒤에도 한 장도 안 깔렸으면 <b>왜인지 적어서</b> CAD 로그로 보낸다 — 검은 화면만 남기지 않는다.
  setTimeout(function(){
    if(told) return; told=true;
    var sz=document.getElementById('map');
    say('타일 성공 '+good+' 실패 '+bad+' · 지도칸 '+sz.clientWidth+'×'+sz.clientHeight+'px · 확대 '+map.getZoom());
    if(good===0&&bad===0&&sz.clientHeight<40){ fit(); say('지도칸 작음 — 재측정'); }
  },4000);

  function restack(){                       // 밑그림을 바꾸면 겹침을 다시 위로 올린다
    if(map.hasLayer(labels)){map.removeLayer(labels);labels.addTo(map);}
    if(map.hasLayer(cadastral)){map.removeLayer(cadastral);cadastral.addTo(map);}
  }
  document.querySelectorAll(""input[name='bm']"").forEach(function(r){
    r.addEventListener('change',function(){
      map.removeLayer(cur); cur=base[this.value]; cur.addTo(map); restack();
    });
  });
  // ★보기는 지도만, 가져오기는 도면만 — 하나가 두 일을 하면 '보려고 켰는데 2만 필지가 들어온다'.
  document.getElementById('cadview').addEventListener('change',function(){
    if(this.checked) cadastral.addTo(map); else map.removeLayer(cadastral);
    cadMsg();
  });
  map.on('zoomend',cadMsg);
  // 가져오기를 켜면 어떤 자리인지 보이도록 보기도 같이 켜 준다(끄는 것은 따로).
  document.getElementById('cad').addEventListener('change',function(){
    if(this.checked && !document.getElementById('cadview').checked){
      document.getElementById('cadview').checked=true; cadastral.addTo(map);
    }
  });
  document.getElementById('lbl').addEventListener('change',function(){
    if(this.checked) labels.addTo(map); else map.removeLayer(labels);
  });

  var p1=null,rect=null,mark=null,box=null;
  var mapDiv=document.getElementById('map');
  function pickOn(v){ if(mapDiv) mapDiv.classList.toggle('pick', !!v); }
  pickOn(true);
  // ★[JACK 1117] 첫 점 뒤엔 박스가 마우스를 따라다닌다 — 가로·세로·면적이 같이 바뀐다.
  var lblW=null,lblH=null,lblA=null;
  var lcss=document.createElement('style');
  lcss.textContent='.bxl{background:rgba(31,36,48,.88);color:#fff;border:1px solid #ffb020;border-radius:4px;padding:1px 6px;font-size:12px;font-weight:bold;white-space:nowrap;width:auto!important;height:auto!important;line-height:1.4;pointer-events:none}';
  document.head.appendChild(lcss);
  function lblSet(m,ll,txt){
    if(!m) m=L.marker(ll,{interactive:false,keyboard:false,icon:L.divIcon({className:'bxl',html:txt,iconSize:null})}).addTo(map);
    else { m.setLatLng(ll); m.getElement().innerHTML=txt; }
    return m;
  }
  function lblClear(){
    [lblW,lblH,lblA].forEach(function(m){ if(m) map.removeLayer(m); });
    lblW=lblH=lblA=null;
  }
  function fmtArea(a){ return a>=10000 ? (a/10000).toFixed(2)+' ha' : Math.round(a).toLocaleString()+' ㎡'; }
  function drawLive(b){
    var nw=b.getNorthWest(), ne=b.getNorthEast(), se=b.getSouthEast();
    var w=map.distance(nw,ne), h=map.distance(ne,se);
    if(rect) rect.setBounds(b);
    else rect=L.rectangle(b,{color:'#ffb020',weight:2,fillOpacity:0.08,interactive:false}).addTo(map);
    lblW=lblSet(lblW,L.latLng(b.getNorth(),b.getCenter().lng),Math.round(w).toLocaleString()+' m');
    lblH=lblSet(lblH,L.latLng(b.getCenter().lat,b.getEast()),Math.round(h).toLocaleString()+' m');
    lblA=lblSet(lblA,b.getCenter(),fmtArea(w*h));
    return {w:w,h:h};
  }
  map.on('mousemove',function(e){
    if(!quit&&mode==='poly'&&!poly&&pv.length){ polyDraw(e.latlng); return; }
    if(quit||p1===null) return;
    var s=drawLive(L.latLngBounds(p1,e.latlng));
    info.textContent=Math.round(s.w)+'m × '+Math.round(s.h)+'m · 클릭하면 확정';
  });
  function cancelFirst(){ if(p1!==null){ reset(); info.textContent='범위 없음'; } }
  map.on('contextmenu',cancelFirst);
  document.addEventListener('keydown',function(e){ if(e.key==='Escape') cancelFirst(); });
  function reset(){
    if(rect){map.removeLayer(rect);rect=null;}
    if(mark){map.removeLayer(mark);mark=null;}
    lblClear();
    p1=null;box=null;send.disabled=true;info.textContent='범위 없음';
    pickOn(true);
    polyReset();
  }
  // ★[JACK 1126 «다각형으로 가져오기»] 다각형 모드 — 클릭 = 꼭짓점 · 첫 점 클릭/더블클릭 = 닫기 · Backspace = 되돌리기 · Esc/오른쪽 = 다시
  var mode='box', pv=[], pline=null, prub=null, ppoly=null, poly=null, pdots=[];
  // ★[검토 10] 도면에서 고르는 동안 지도 단추 잠금 — 또 누르면 'DHCONTOURPICK '이 고르기 프롬프트 입력으로 들어감 · CAD가 'unlock:'으로 풂(취소 · 실패 · 예외 길도)
  var picking=false;
  function pickLock(v){ picking=v; ['rt','cl'].forEach(function(id){ var b=document.getElementById(id); if(b) b.disabled=v; }); send.disabled=v||!(box||poly); }
  function polyReset(){
    [pline,prub,ppoly].forEach(function(l){ if(l) map.removeLayer(l); });
    pdots.forEach(function(d){ map.removeLayer(d); });
    pline=prub=ppoly=null; pdots=[]; pv=[]; poly=null;
  }
  function polyArea(a){
    if(a.length<3) return 0;
    var lat0=a[0].lat, k=111320*Math.cos(lat0*Math.PI/180), s2=0;
    for(var i=0,j=a.length-1;i<a.length;j=i++){ s2+=(a[j].lng*k)*(a[i].lat*111320)-(a[i].lng*k)*(a[j].lat*111320); }
    return Math.abs(s2/2);
  }
  function segX(a,b,c,d){
    function o(p,q,r){ var v=(q.lng-p.lng)*(r.lat-p.lat)-(q.lat-p.lat)*(r.lng-p.lng); return v>0?1:(v<0?-1:0); }
    return o(a,b,c)*o(a,b,d)<0 && o(c,d,a)*o(c,d,b)<0;
  }
  function polyTwisted(a){
    var n=a.length;
    for(var i=0;i<n;i++) for(var j=i+1;j<n;j++){
      if(j===i+1||(i===0&&j===n-1)) continue;
      if(segX(a[i],a[(i+1)%n],a[j],a[(j+1)%n])) return true;
    }
    return false;
  }
  function polyDraw(mouse){
    var pts=pv.slice(); if(mouse) pts.push(mouse);
    if(pline) pline.setLatLngs(pv); else if(pv.length) pline=L.polyline(pv,{color:'#ffb020',weight:2,interactive:false}).addTo(map);
    if(mouse&&pv.length){ var rb=[pv[pv.length-1],mouse]; if(pv.length>=2) rb.push(pv[0]);
      if(prub) prub.setLatLngs(rb); else prub=L.polyline(rb,{color:'#ffb020',weight:1,dashArray:'4 4',interactive:false}).addTo(map); }
    info.textContent='꼭짓점 '+pv.length+(pts.length>=3?' · '+fmtArea(polyArea(pts)):'')+' · 첫 점/더블클릭 = 닫기 · Backspace = 되돌리기';
  }
  function polyDot(ll){ pdots.push(L.circleMarker(ll,{radius:4,color:'#ffb020',weight:2,fillOpacity:0.6,interactive:false}).addTo(map)); }
  function polyClose(){
    if(pv.length<3){ info.textContent='꼭짓점 3개 이상'; return; }
    if(polyTwisted(pv)){ info.textContent='다각형 꼬임 → 다시 그리기'; return; }
    poly=pv.slice();
    if(prub){ map.removeLayer(prub); prub=null; }
    if(pline){ map.removeLayer(pline); pline=null; }
    ppoly=L.polygon(poly,{color:'#ffb020',weight:2,fillOpacity:0.08,interactive:false}).addTo(map);
    info.textContent='꼭짓점 '+poly.length+' · '+fmtArea(polyArea(poly));
    send.disabled=picking;
  }
  function polyClick(e){
    if(poly){ reset(); }
    if(pv.length>=3){
      var a=map.latLngToContainerPoint(pv[0]), b=map.latLngToContainerPoint(e.latlng);
      if(a.distanceTo(b)<10){ polyClose(); return; }
    }
    pv.push(e.latlng); polyDot(e.latlng); polyDraw(null);
  }
  function setMode(m){
    mode=m; reset();
    if(m==='poly'){ map.doubleClickZoom.disable(); document.getElementById('hint').textContent='꼭짓점 클릭'; }
    else { map.doubleClickZoom.enable(); document.getElementById('hint').innerHTML='<b>모서리 2곳</b> 클릭'; }
  }
  document.querySelectorAll(""input[name='mode']"").forEach(function(r){ r.addEventListener('change',function(){ setMode(this.value); }); });
  map.on('dblclick',function(e){
    if(mode!=='poly'||poly) return;
    // 더블클릭은 클릭 두 번을 먼저 보낸다 — 거의 같은 자리 꼭짓점 하나를 걷고 닫음
    if(pv.length>=2){ var a=map.latLngToContainerPoint(pv[pv.length-1]), b=map.latLngToContainerPoint(pv[pv.length-2]);
      if(a.distanceTo(b)<6){ pv.pop(); var d=pdots.pop(); if(d) map.removeLayer(d); } }
    polyClose();
  });
  document.addEventListener('keydown',function(e){
    if(mode!=='poly'||poly) return;
    // ★[검토 7] 검색칸 등 글 칸에 포커스가 있으면 그 칸의 Backspace · Esc(꼭짓점을 지우지 않음)
    var t=e.target; if(t&&(t.tagName==='TEXTAREA'||t.isContentEditable||(t.tagName==='INPUT'&&!/^(radio|checkbox|button)$/i.test(t.type||'')))) return;
    if(e.key==='Backspace'&&pv.length){ e.preventDefault(); pv.pop(); var d=pdots.pop(); if(d) map.removeLayer(d); if(!pv.length) polyReset(); polyDraw(null); }
    if(e.key==='Escape'){ reset(); }
  });
  document.getElementById('clr').addEventListener('click',reset);

  map.on('click',function(e){
    if(quit) return;
    if(mode==='poly'){ polyClick(e); return; }
    if(p1===null){
      reset();
      p1=e.latlng;
      mark=L.circleMarker(p1,{radius:5,color:'#ffb020'}).addTo(map);
      info.textContent='마우스를 움직여 크기 보기 · 클릭 확정 · Esc 취소';
      return;
    }
    var b=L.latLngBounds(p1,e.latlng);
    drawLive(b);                  // 확정 — 마지막 마우스 위치 그대로, 길이 표시는 남긴다
    if(mark){map.removeLayer(mark);mark=null;}
    box={minLon:b.getWest(),minLat:b.getSouth(),maxLon:b.getEast(),maxLat:b.getNorth()};
    // 대략 크기(m) — 사람이 ""이만하면 되겠다""를 눈으로 재게.
    var mLat=111320, mLon=111320*Math.cos(b.getCenter().lat*Math.PI/180);
    var w=(b.getEast()-b.getWest())*mLon, h=(b.getNorth()-b.getSouth())*mLat;
    p1=null;
    if(w<1||h<1){ info.textContent='범위 너무 작음'; send.disabled=true; return; }
    info.textContent='약 '+Math.round(w)+'m × '+Math.round(h)+'m';
    send.disabled=picking;
    // ★[검토 0902] 여기서 손바닥으로 바꾸면 안 된다 — 바로 위에서 p1=null 로 되돌려
    //   <b>다음 클릭이 곷바로 새 찍기</b>다. 커서만 ‘끌기’라고 말하면 거짓이 된다.
  });

  send.addEventListener('click',function(){
    if(picking) return;
    if(mode==='poly'&&poly){
      send.disabled=true; info.textContent='전송 중…';
      tell('poly',{pts:poly.map(function(q){ return [q.lng,q.lat]; }),cad:document.getElementById('cad').checked},
        function(){ info.textContent='가져오는 중…'; },
        function(){ info.textContent='전송 실패'; send.disabled=false; });
      return;
    }
    if(!box) return;
    send.disabled=true; info.textContent='전송 중…'; TS.sending=true;
    // ★[JACK 0901] 지적도 체크는 <b>지도 위 표시</b>이자 <b>같이 가져올지</b>의 뜻이다.
    box.cad = document.getElementById('cad').checked;
    tell('box',box,
      function(){ info.textContent=EMBED?'가져오는 중…':'전송 완료 · 창 닫아도 됨';
                  if(!EMBED) document.body.style.opacity=.6; },
      function(){ info.textContent='전송 실패'; send.disabled=false; TS.sending=false; });
  });
  // ★[주소 검색] 도킹바(EMBED)에서만 — 통신은 C#이 한다(키 · 로그). 결과는 'geo:[{t,c,x,y}]' 로 온다.
  var smk=null, sq=document.getElementById('q'), slist=document.getElementById('slist'), smsg=document.getElementById('smsg');
  function goHit(h){
    slist.style.display='none'; smsg.textContent=''; TS.found=true;
    var ll=L.latLng(h.y,h.x);
    map.setView(ll,17);                       // 한가운데 · 17단계(지적도 시작 단계)
    if(smk) map.removeLayer(smk);
    smk=L.circleMarker(ll,{radius:7,color:'#ff3b30',weight:3,fillOpacity:0.2}).addTo(map);
    fit();
  }
  function doSearch(){
    var q=sq.value.trim(); if(!q||quit) return;
    smsg.textContent='검색 중…'; slist.style.display='none';
    try{ window.chrome.webview.postMessage(JSON.stringify({kind:'search',q:q})); }catch(e){ smsg.textContent='검색 실패 — 인터넷 · 키 확인'; }
  }
  if(EMBED){
    document.getElementById('mpoly').style.display='inline';
    ['rt','cl'].forEach(function(id){
      var b=document.getElementById(id); b.style.display='inline-block';
      b.addEventListener('click',function(){
        if(quit||picking) return;
        pickLock(true);
        info.textContent='도면에서 고르기…';
        tell('cadpick',{how:(id==='rt'?'R':'P'),cad:document.getElementById('cad').checked},null,function(){ info.textContent='전송 실패'; pickLock(false); });
      });
    });
    document.getElementById('srow').style.display='flex';
    document.getElementById('sbtn').addEventListener('click',doSearch);
    sq.addEventListener('keydown',function(e){ if(e.key==='Enter') doSearch(); });
    map.on('click',function(){ slist.style.display='none'; if(smk){map.removeLayer(smk);smk=null;} });
  }
  function onGeo(j){
    var a=null; try{ a=JSON.parse(j); }catch(e){}
    if(!a){ smsg.textContent='검색 실패 — 인터넷 · 키 확인'; return; }
    if(!a.length){ smsg.textContent='주소 못 찾음 → 다시 입력'; return; }
    if(a.length===1){ goHit(a[0]); return; }
    smsg.textContent=''; slist.innerHTML='';
    a.forEach(function(h){
      var d=document.createElement('div'); d.textContent=h.t+'  ('+h.c+')';
      d.addEventListener('click',function(){ goHit(h); });
      slist.appendChild(d);
    });
    slist.style.display='block';
  }
  // ★CAD가 거절하면 알려 준다 — 안 그러면 '받는 중…'에서 멈춘 것처럼 보인다.
  if(EMBED && window.chrome && window.chrome.webview){
    window.chrome.webview.addEventListener('message',function(ev){
      var m=(typeof ev.data==='string')?ev.data:'';
      if(m.indexOf('geo:')===0){ onGeo(m.substring(4)); return; }
      if(m.indexOf('unlock:')===0){ pickLock(false); return; }
      if(m.indexOf('reject:')===0){ info.textContent=m.substring(7); if(!picking) send.disabled=!(box||poly); TS.sending=false; }
    });
  }
})();
</script></body></html>";
}
