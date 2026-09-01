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
    private static string Key => VWorldImagery.ApiKey;

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
                   .Replace("__KEY__", Esc(Key))
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
  #bar{flex:0 0 auto;background:#1f2430;color:#eaeef7;
       padding:10px 14px;display:flex;gap:10px;align-items:center;flex-wrap:wrap;font-size:14px}
  #bar b{color:#8ab4ff}
  #map{flex:1 1 auto;min-height:0;background:#111}
  button{font:inherit;padding:6px 12px;border-radius:6px;border:1px solid #4a5268;
         background:#2b3242;color:#eaeef7;cursor:pointer}
  button.go{background:#2f6fed;border-color:#2f6fed;font-weight:bold}
  button:disabled{opacity:.45;cursor:default}
  #info{margin-left:auto;color:#c9d3e8}
  #noweb{display:none;flex:1 1 auto;background:#fff;
         padding:24px;font-size:15px;line-height:1.7}
</style></head><body>
<div id='bar'>
  <span><b>모서리 2곳</b> 클릭</span>
  <button id='clr'>다시 찍기</button>
  <label title='지도에만 표시 · 도면에는 안 들어옴'><input type='checkbox' id='cadview'> 지적 보기</label>
  <span id='cadmsg' style='color:#ffb020;display:none'>확대시 표시</span>
  <label title='같은 범위의 지적도를 도면으로 가져옴'><input type='checkbox' id='cad'> 지적도 가져오기</label>
  <label><input type='checkbox' id='lbl' checked> 지명·도로</label>
  <label><input type='radio' name='bm' value='Satellite' checked> 항공사진</label>
  <label><input type='radio' name='bm' value='Base'> 일반지도</label>
  <span id='info'>범위 없음</span>
  <button class='go' id='send' disabled>이 범위 가져오기</button>
  <button id='cancel'>그만두기</button>
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

  function dead(msg){
    document.getElementById('map').style.display='none';
    document.getElementById('noweb').style.display='block';
    send.disabled=true;
    if(msg) info.textContent=msg;
  }
  if (typeof L === 'undefined') { dead('지도 로드 실패'); return; }

  var KEY='__KEY__';
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
  function watch(l){ l.on('tileerror',function(){ bad++; if(bad>=6&&good===0) dead('타일 수신 실패'); }); }
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
  function reset(){
    if(rect){map.removeLayer(rect);rect=null;}
    if(mark){map.removeLayer(mark);mark=null;}
    p1=null;box=null;send.disabled=true;info.textContent='범위 없음';
  }
  document.getElementById('clr').addEventListener('click',reset);

  map.on('click',function(e){
    if(quit) return;
    if(p1===null){
      reset();
      p1=e.latlng;
      mark=L.circleMarker(p1,{radius:5,color:'#ffb020'}).addTo(map);
      info.textContent='반대쪽 모서리 클릭';
      return;
    }
    var b=L.latLngBounds(p1,e.latlng);
    if(rect) map.removeLayer(rect);
    rect=L.rectangle(b,{color:'#ffb020',weight:2,fillOpacity:0.08}).addTo(map);
    if(mark){map.removeLayer(mark);mark=null;}
    box={minLon:b.getWest(),minLat:b.getSouth(),maxLon:b.getEast(),maxLat:b.getNorth()};
    // 대략 크기(m) — 사람이 ""이만하면 되겠다""를 눈으로 재게.
    var mLat=111320, mLon=111320*Math.cos(b.getCenter().lat*Math.PI/180);
    var w=(b.getEast()-b.getWest())*mLon, h=(b.getNorth()-b.getSouth())*mLat;
    p1=null;
    if(w<1||h<1){ info.textContent='범위 너무 작음'; send.disabled=true; return; }
    info.textContent='약 '+Math.round(w)+'m × '+Math.round(h)+'m';
    send.disabled=false;
  });

  send.addEventListener('click',function(){
    if(!box) return;
    send.disabled=true; info.textContent='전송 중…';
    // ★[JACK 0901] 지적도 체크는 <b>지도 위 표시</b>이자 <b>같이 가져올지</b>의 뜻이다.
    box.cad = document.getElementById('cad').checked;
    tell('box',box,
      function(){ info.textContent=EMBED?'가져오는 중…':'전송 완료 · 창 닫아도 됨';
                  if(!EMBED) document.body.style.opacity=.6; },
      function(){ info.textContent='전송 실패'; send.disabled=false; });
  });
  // ★CAD가 거절하면 알려 준다 — 안 그러면 '받는 중…'에서 멈춘 것처럼 보인다.
  if(EMBED && window.chrome && window.chrome.webview){
    window.chrome.webview.addEventListener('message',function(ev){
      var m=(typeof ev.data==='string')?ev.data:'';
      if(m.indexOf('reject:')===0){ info.textContent=m.substring(7); send.disabled=!box; }
    });
  }
})();
</script></body></html>";
}
