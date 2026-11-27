"""3D 매핑 만들기 시험 (실제 WebView2 · 작은 창 · 실제로 매핑을 돌림) — 1127 «단계 카드형» 화면.

    python viewer_test\\check_home_mapping.py 사진폴더 결과폴더 [처리 수준=fast] [--ui-only]

화면: 사진 선택 (고른 파일만) → 단계 띠 (① 사진 · ② 작업 영역 · ③ 받을 결과 · ④ 만들기 · 체크 · 누르면 깜박) → 바뀐 이름 · 상용 프로그램 낱말 없음
  → 결과 카드 켜고 끄기 (카드 누르기) · ⋯ 세부 옵션 칸 (끈 카드 = 흐림 · 누를 수 없음 · 다시 켜면 값 그대로) → 처리 수준 ▾ · 작업 설정 칸
  → 장치 성능 · 실행 기록 · 카드 줄 접기 · 도움말 → 값 기억 (다시 읽어도) → 이 설정 저장 · 불러오기 · 기본 → 단계 시간표 꼴 (가짜 작업)
만들기 (--ui-only 가 아니면): 작업 영역(가운데) → [3D 매핑] → 단계 시간표 · 남은 시간 → 끝나면 3D 보기 → 결과물이 고른 대로 (정사 선명도 · DEM 칸 · 등고선 기준면 ·
  주석 · 정리 · LAS 밀도 · 분류 · TIN 샘플 거리 · 3D PLY/OBJ · args.json · 정보.txt) → 같은 결과로 다듬기 (--reuse-only) 옵션만 바꿔 다시.
결과 폴더는 지우고 새로 만든다. 시험 템플릿은 끝에 지운다.
"""
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))
import webview  # noqa: E402
from roadview import app as A  # noqa: E402

ARGS = [x for x in sys.argv[1:] if not x.startswith('--')]
UI_ONLY = '--ui-only' in sys.argv
PHOTOS, OUT = os.path.abspath(ARGS[0]), os.path.abspath(ARGS[1])
Q = ARGS[2] if len(ARGS) > 2 else 'fast'
TPL = '시험틀_자동'
FAIL = []


def ok(name, cond, val=None):
    if not cond:
        FAIL.append(name)
    print(('통과 ' if cond else '실패 ') + name + ('' if val is None else ' · ' + json.dumps(val, ensure_ascii=False, default=str)[:260]))
    sys.stdout.flush()


# 시험에서 쓰는 값 (만들기에도 이대로)
SET = {'q': Q, 'outs': ['mesh', 'ortho', 'dsm', 'dtm', 'las', 'contour'], 'feat': 'high', 'orthoRes': 'low', 'orthoFill': False,
       'meshRatio': 50, 'meshFmt': ['ply', 'obj'], 'lasDensity': 25, 'lasClassify': True, 'demCell': 1, 'ctBase': 0.5, 'ct': 1,
       'ctLabel': 20, 'ctClean': True, 'tinStep': 200}


def run(w, api):
    J = w.evaluate_js

    def wait(expr, t=60, step=0.5):
        t0 = time.time()
        while time.time() - t0 < t:
            try:
                if J(expr):
                    return True
            except Exception:
                pass
            time.sleep(step)
        return False
    jv = lambda e: json.loads(J('JSON.stringify(%s)' % e))      # noqa: E731
    # 화면 조작 (사람처럼: 단추 누르기 · 고르기 바꾸기)
    J('''window.__t = {
      q: (s) => document.querySelector(s),
      click: (s) => { const e = document.querySelector(s); e.click(); return true; },
      set: (s, v) => { const e = document.querySelector(s); e.value = v; e.dispatchEvent(new Event('change')); return e.value; },
      sw: (s, on) => { const e = document.querySelector(s); if (e.classList.contains('on') !== on) e.click(); return e.classList.contains('on'); },
      vis: (s) => { const e = document.querySelector(s); return !!e && !!e.offsetParent && getComputedStyle(e).display !== 'none'; },
    };''')

    def setup(photo_r):
        J('RVMap._test.take(%s); RVMap._S.outTouched = true; document.getElementById("mOut").value = %s;' % (json.dumps(photo_r), json.dumps(OUT)))
        J('RVMap.open()')
        return wait('!document.getElementById("mp").classList.contains("hide")', 10)

    time.sleep(3)
    r_all = api.scan_mapping(PHOTOS)
    files = [os.path.join(PHOTOS, p['name']) for p in r_all['photos']]
    r_pick = api.scan_mapping_files(files[:-2])
    ok('사진 선택 = 고른 파일만', r_pick['ok'] and len(r_pick['photos']) == len(files) - 2 and r_pick['folder'] == PHOTOS,
       [len(r_pick['photos']), len(files)])
    ok('매핑 화면', setup(r_pick))
    ok('고른 사진 글', '고른 사진 %d장' % (len(files) - 2) in J('document.getElementById("mInfo").textContent'), J('document.getElementById("mInfo").textContent'))
    # 단계 카드형 틀 (JACK 1127 «배치 · 이름을 우리 꼴로 → 대폭»)
    secs = jv('[...document.querySelectorAll("#mp [data-sec]")].map(e => e.dataset.sec)')
    ok('결과 카드 차례 (단계 카드형)', secs == ['mesh', 'ortho', 'dtm', 'las', 'contour', 'dsm', 'gs'], secs)
    steps = jv('[...document.querySelectorAll("#mSteps .mstep")].map(e => [e.dataset.step, e.querySelector("b").textContent, e.classList.contains("done")])')
    ok('단계 띠 = ① 사진 · ② 작업 영역 · ③ 받을 결과 · ④ 만들기', [s[1] for s in steps] == ['사진', '작업 영역', '받을 결과', '만들기'] and J('__t.vis("#mSteps")'), steps)
    ok('단계 띠: 사진 들어옴 → ① 체크 · ② «모든 사진»', steps[0][2] and steps[1][2] and '모든 사진' in J('__t.q(".mstep[data-step=area] em").textContent')
       and J('__t.q(".mlink[data-after=photo]").classList.contains("done")'), J('__t.q(".mstep[data-step=area] em").textContent'))
    ok('[3D 매핑] · [정지] · 이 설정 저장 · 상태 줄', J('document.getElementById("mBuild").textContent') == '3D 매핑' and J('document.getElementById("mStop").textContent') == '정지'
       and J('document.getElementById("mTplSave").textContent') == '이 설정 저장' and J('__t.vis("#mDev") && __t.vis("#mLogBtn") && __t.vis("#mCrsBar") && __t.vis("#mBuild") && __t.vis("#mStop")'),
       J('document.getElementById("mCrsBar").textContent'))
    ok('이름 칸 «프로젝트 이름» (머리 오른쪽)', J('document.getElementById("mTitle").placeholder') == '프로젝트 이름' and J('__t.vis("#mTitle") && __t.vis("#mRen") && __t.vis("#mHdrLog") && __t.vis("#mHelp")'))
    TIPS = ['3D 매핑은 컴퓨터 성능에 따라 많은 재구성 시간이 소요됨', '매핑 중에 사진 폴더의 사진을 옮기거나 지우지 말 것', '재기 · 주석 = 결과 3D 화면 오른쪽 메뉴 «재기»',
            '처리 속도 NVIDIA GPU > CPU', '결과물 추가 · 자르기 = 끝난 뒤 결과 3D 화면 «결과물 다듬기»']
    ok('도움말 다섯 줄 그대로', jv('[...document.querySelectorAll("#mTips li")].map(e => e.textContent)') == TIPS, jv('[...document.querySelectorAll("#mTips li")].map(e => e.textContent)'))
    # 바뀐 이름 (화면 글만 · 안쪽 열쇠 그대로)
    names = jv('''({ all: document.getElementById("mAll").textContent, area: document.getElementById("mArea").textContent, none: document.getElementById("mNone").textContent,
        pick: document.getElementById("mPick").textContent, files: document.getElementById("mPickFiles").textContent,
        labels: [...document.querySelectorAll("#mp .mside .ml")].map(e => e.textContent.trim()),
        cards: [...document.querySelectorAll("#mCards .tcard .cn")].map(e => e.textContent),
        q: [...document.querySelectorAll("#mQ button b")].map(e => e.textContent) })''')
    ok('바뀐 이름 (모든 사진 · 작업 영역 지정 · 영역 지우기 · 사진 폴더 열기 · 저장 위치 · 처리 수준 · 결과 카드)',
       names['all'] == '모든 사진' and names['area'] == '작업 영역 지정' and names['none'] == '영역 지우기' and names['pick'] == '사진 폴더 열기' and names['files'] == '사진 파일 고르기'
       and names['labels'][:3] == ['처리 수준', '저장 위치', '병렬 처리 참여i'] and names['q'] == ['빠른 확인', '표준', '정밀']
       and names['cards'] == ['실감 3D 모델', '정사 지도(GeoTIFF)', '맨땅 높이(DTM)', '점구름(LAS)', '등고선(DXF)', '지표 높이(DSM) · LandXML', '사진 같은 3D(가우시안)'], names)
    words = jv('''(() => { const m = document.getElementById("mp"), first = m.querySelector("#mTips li");
        let t = m.textContent.replace(first.textContent, "");
        m.querySelectorAll("[title]").forEach(e => { t += " " + e.title; }); m.querySelectorAll("[data-tip]").forEach(e => { t += " " + e.dataset.tip; });
        m.querySelectorAll("[placeholder]").forEach(e => { t += " " + e.placeholder; });
        return ["재구성", "해상도", "시나리오", "고급 설정", "시작", "품질", "단순", "범위 그리기", "비우기", "결과 폴더", "정사영상"].filter(w => t.indexOf(w) >= 0); })()''')
    ok('상용 프로그램 낱말 없음 (재구성 · 해상도 · 시나리오 · 고급 설정 · 시작 …) · 도움말 1번은 JACK 글 그대로', words == [], words)
    ok('병렬 처리 참여 단추 (cluster.js 그대로)', J('__t.vis("#mClu")') and J('document.getElementById("mClu").textContent') in ('켜짐', '꺼짐'))
    # 장치 성능
    ok('장치 성능 읽음', wait('!!RVMap._S.sys', 20) and J('document.getElementById("mDevDot").className') in ('dot ok', 'dot warn'), jv('RVMap._S.sys'))
    si = api.sysinfo()
    ok('상태 줄 장치 이름 (그래픽카드)', (si['gpu'].split()[-1] in J('document.getElementById("mDevName").textContent')) if si.get('gpu_nvidia') else 'CPU' in J('document.getElementById("mDevName").textContent'),
       J('document.getElementById("mDevName").textContent'))
    J('__t.click("#mDev")')
    ok('장치 성능 자세히', J('__t.vis("#mDevPop")') and 'CPU' in J('document.getElementById("mDevPop").textContent'), J('document.getElementById("mDevPop").textContent'))
    J('__t.click("#mDev")')
    ok('장치 성능 닫기', not J('__t.vis("#mDevPop")'))
    ok('가우시안 카드 = 장치 (못 하면 흐림 + 이유)', J('document.getElementById("mGsCard").classList.contains("na")') == (not si['gs_ok'])
       and (si['gs_ok'] or J('__t.vis("#mGsWhy")')), [si['gs_ok'], si['gs_why']])

    # 카드 켜고 끄기 (카드 아무 데나 · 스위치) · ⋯ = 세부 옵션 칸 (켜고 끄기와 따로)
    J('__t.sw(".sw[data-o=las]", false)')
    J('__t.click(".tcard[data-sec=las] .cn")')
    ok('카드 누르면 켜짐', 'las' in jv('[...RVMap._S.outs]') and not J('__t.q(".tcard[data-sec=las]").classList.contains("off")') and J('__t.q(".sw[data-o=las]").getAttribute("aria-checked")') == 'true')
    J('__t.click(".tcard[data-sec=las] .cd")')
    ok('카드 다시 누르면 꺼짐', 'las' not in jv('[...RVMap._S.outs]') and J('__t.q(".tcard[data-sec=las]").classList.contains("off")'))
    J('__t.click(".tcard[data-sec=las] .cmore")')
    pr = jv('''(() => { const p = document.querySelector(".tcard[data-sec=las] .mpop"), c = document.querySelector(".tcard[data-sec=las]").getBoundingClientRect(), r = p.getBoundingClientRect();
        return { open: p.classList.contains("open"), top: r.top, bottom: r.bottom, left: r.left, right: r.right, ctop: c.top, W: innerWidth, H: innerHeight }; })()''')
    ok('⋯ = 세부 옵션 칸 (켜고 끄기는 그대로)', pr['open'] and J('__t.vis(".tcard[data-sec=las] select[data-k=lasDensity]")') and 'las' not in jv('[...RVMap._S.outs]')
       and '꺼짐' in J('__t.q(".tcard[data-sec=las] .mpst").textContent'), pr)
    ok('세부 옵션 칸 = 카드 위 · 화면 안', pr['bottom'] <= pr['ctop'] + 1 and pr['left'] >= 0 and pr['right'] <= pr['W'] and pr['top'] >= 0, pr)
    J('document.getElementById("mMap").dispatchEvent(new PointerEvent("pointerdown", { bubbles: true, clientX: 50, clientY: 50, pointerId: 1 })); document.getElementById("mMap").dispatchEvent(new PointerEvent("pointerup", { bubbles: true, clientX: 50, clientY: 50, pointerId: 1 }))')
    ok('바깥 누르면 닫힘', not J('__t.q(".tcard[data-sec=las] .mpop").classList.contains("open")'))
    J('__t.click(".tcard[data-sec=las] .cmore")')
    J('document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }))')
    ok('Esc = 닫힘', not J('__t.q(".tcard[data-sec=las] .mpop").classList.contains("open")'))

    # 끈 카드 옵션: 흐림 · 누를 수 없음 · 값 그대로 / 켜면 바로 활성
    J('__t.sw(".sw[data-o=ortho]", true)')
    J('__t.set(".tcard[data-sec=ortho] select[data-k=orthoRes]", "mid")')
    fill0 = J('RVMap._S.v.orthoFill')
    e_on = J('document.getElementById("mEst").textContent')
    J('__t.sw(".sw[data-o=ortho]", false)')
    e_off = J('document.getElementById("mEst").textContent')
    J('__t.click(".tcard[data-sec=ortho] .cmore")')
    st = jv('''(() => { const c = __t.q(".tcard[data-sec=ortho]"), b = c.querySelector(".tcb"), s = c.querySelector("select[data-k=orthoRes]"), r = s.getBoundingClientRect();
        const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
        return { off: c.classList.contains("off"), op: +getComputedStyle(b).opacity, pe: getComputedStyle(b).pointerEvents, dis: s.disabled,
                 hit: hit === s, vis: __t.vis(".tcard[data-sec=ortho] select[data-k=orthoRes]") }; })()''')
    ok('끈 카드 옵션 = 흐림 · 누를 수 없음 · 보이기는 함', st['off'] and st['op'] < 0.6 and st['pe'] == 'none' and st['dis'] and not st['hit'] and st['vis'], st)
    J('__t.click(".tcard[data-sec=ortho] .sw[data-k=orthoFill]")')
    ok('끈 카드 옵션 단추 눌러도 안 바뀜', J('RVMap._S.v.orthoFill') == fill0)
    ok('켠 카드 수 · 예상 시간 바로 바뀜', re.search(r'결과물 (\d+)개', e_on) and int(re.search(r'결과물 (\d+)개', e_on).group(1)) == int(re.search(r'결과물 (\d+)개', e_off).group(1)) + 1, [e_on, e_off])
    J('__t.sw(".sw[data-o=ortho]", true)')
    ok('다시 켜면 값 그대로 · 활성', J('__t.q(".tcard[data-sec=ortho] select[data-k=orthoRes]").value') == 'mid'
       and not J('__t.q(".tcard[data-sec=ortho] select[data-k=orthoRes]").disabled') and not J('__t.q(".tcard[data-sec=ortho]").classList.contains("off")')
       and '켜짐' in J('__t.q(".tcard[data-sec=ortho] .mpst").textContent'))
    J('__t.click(".tcard[data-sec=ortho] .sw[data-k=orthoFill]")')
    ok('켠 카드 옵션 단추 = 바뀜', J('RVMap._S.v.orthoFill') == (not fill0))

    # 세부 옵션 «작업 영역 ›» = 작업 영역 지정 · 처리 수준 · 작업 설정 · 단계 띠 누르기
    J('__t.click(".tcard[data-sec=ortho] .tlink[data-act=area]")')
    ok('세부 옵션 «작업 영역 ›» = 작업 영역 지정 켬 · 칸 닫힘', J('RVMap._S.area') is True and J('document.getElementById("mArea").classList.contains("on")')
       and not J('__t.q(".tcard[data-sec=ortho] .mpop").classList.contains("open")'))
    J('__t.click("#mArea")')
    ok('작업 영역 지정 끔', J('RVMap._S.area') is False)
    J('__t.click("#mQBtn")')
    ok('처리 수준 ▾ = 빠른 확인 · 표준 · 정밀 칸', J('__t.q("#mQPop").classList.contains("open")') and J('__t.vis("#mQ button[data-q=fine]")'))
    J('__t.click("#mQ button[data-q=fine]")')
    ok('처리 수준 고르면 이름 · 예상 시간 바뀜 · 칸 닫힘', J('RVMap._S.q') == 'fine' and J('document.getElementById("mQName").textContent') == '정밀'
       and '처리 수준 정밀' in J('document.getElementById("mEst").textContent') and not J('__t.q("#mQPop").classList.contains("open")'), J('document.getElementById("mEst").textContent'))
    J('__t.click("#mSetBtn")')
    ok('⚙ 작업 설정 칸 (저장한 설정 · 특징점 밀도 · 좌표계)', J('__t.vis("#mTpl") && __t.vis("select[data-k=feat]") && __t.vis("#mCrs")'))
    J('__t.click("#mSetBtn")')
    ok('작업 설정 다시 누르면 닫힘', not J('__t.vis("#mTpl")'))
    for k, tgt in (('outs', 'mCards'), ('area', 'mTools'), ('go', 'mBuild'), ('photo', 'mPhotoBox')):
        J('__t.click(".mstep[data-step=%s]")' % k)
        ok('단계 띠 «%s» 누르면 그 자리 깜박' % k, J('document.getElementById("%s").classList.contains("mflash")' % tgt))
    o0 = jv('[...RVMap._S.outs]')
    for o in o0:
        J('__t.sw(".sw[data-o=%s]", false)' % o)
    ok('결과 카드 다 끄면 ③ 안 끝남 · 지금 단계 = ③', not J('__t.q(".mstep[data-step=outs]").classList.contains("done")') and J('__t.q(".mstep[data-step=outs]").classList.contains("cur")')
       and '받을 결과 카드 켜기' in J('document.getElementById("mEst").textContent'))
    for o in o0:
        J('__t.sw(".sw[data-o=%s]", true)' % o)
    ok('다시 켜면 ③ 체크 · ④ = 예상 시간', J('__t.q(".mstep[data-step=outs]").classList.contains("done")') and J('__t.q(".mstep[data-step=go]").classList.contains("cur")')
       and '분' in J('__t.q(".mstep[data-step=go] em").textContent'), J('__t.q(".mstep[data-step=go] em").textContent'))

    # 이름 연필 · 실행 기록 · 카드 줄 접기 · 도움말
    J('__t.click("#mRen")')
    ok('이름 연필 = 이름 칸', J('document.activeElement && document.activeElement.id') == 'mTitle')
    J('__t.click("#mLogBtn")')
    ok('실행 기록 펼치기', J('__t.vis("#mLogBox")'))
    J('__t.click("#mHdrLog")')
    ok('실행 기록 닫기 (머리 단추)', not J('__t.vis("#mLogBox")'))
    h0 = J('document.getElementById("mMap").getBoundingClientRect().height')
    J('__t.click("#mFold")')
    h1 = J('document.getElementById("mMap").getBoundingClientRect().height')
    ok('카드 줄 접기 = 지도 커짐 · 기억', J('document.getElementById("mp").classList.contains("cardsMin")') and not J('__t.vis("#mCards")') and h1 > h0 + 60
       and json.loads(J('localStorage.getItem("rv.map.ui")'))['fold'].get('__cards') is True, [h0, h1])
    J('__t.click("#mHdrSet")')
    h2 = J('document.getElementById("mMap").getBoundingClientRect().height')
    ok('카드 줄 펴기 (머리 단추)', J('__t.vis("#mCards")') and abs(h2 - h0) < 2, [h0, h2])
    J('__t.click("#mHelp")')
    ok('도움말 = 떠 있는 칸 (다섯 줄)', J('__t.vis("#mTips ol")'))
    J('__t.click("#mHelp")')
    ok('도움말 닫기', not J('__t.vis("#mTips ol")'))

    # 고른 값 = 시험 값 (사람처럼 칸에서 바꿈)
    J('__t.click("#mQ button[data-q=%s]")' % SET['q'])
    for o in ('mesh', 'ortho', 'dsm', 'dtm', 'las', 'contour', 'gs'):
        J('__t.sw(".sw[data-o=%s]", %s)' % (o, 'true' if o in SET['outs'] else 'false'))
    for k in ('feat', 'orthoRes', 'lasDensity', 'demCell'):
        J('__t.set("select[data-k=%s]", %s)' % (k, json.dumps(str(SET[k]))))
    for k in ('meshRatio', 'ctBase', 'ctLabel', 'tinStep'):
        J('__t.set("input[data-k=%s]", %s)' % (k, json.dumps(str(SET[k]))))
    J('__t.set("#mCt", "%s")' % SET['ct'])
    for k in ('orthoFill', 'lasClassify', 'ctClean'):
        J('__t.sw(".sw[data-k=%s]", %s)' % (k, 'true' if SET[k] else 'false'))
    for f in ('ply', 'obj'):
        J('__t.sw("#mFmt button[data-f=%s]", %s)' % (f, 'true' if f in SET['meshFmt'] else 'false'))
    J('__t.click("#mFold")')                                        # 접힘도 기억하는지
    v1 = jv('RVMap._test.values()')
    want = dict(SET, outs=sorted(SET['outs']))
    got = {k: (sorted(v1[k]) if k in ('outs', 'meshFmt') else v1[k]) for k in want}
    ok('칸에서 고른 값', got == dict(want, meshFmt=sorted(SET['meshFmt'])), got)
    e3 = J('document.getElementById("mEst").textContent')
    ok('예상 시간 글 (결과물 6개 · 분)', '결과물 6개' in e3 and '분' in e3, e3)

    # 값 기억: 화면을 다시 읽어도 (localStorage)
    J('location.reload()')
    time.sleep(2)
    ok('다시 읽음', wait('!!window.RVMap && !!document.querySelector("#mTpl option") && !!RVMap._S.sys', 30))
    J('''window.__t = { q: (s) => document.querySelector(s), click: (s) => { document.querySelector(s).click(); return true; },
      set: (s, v) => { const e = document.querySelector(s); e.value = v; e.dispatchEvent(new Event('change')); return e.value; },
      sw: (s, on) => { const e = document.querySelector(s); if (e.classList.contains('on') !== on) e.click(); return e.classList.contains('on'); },
      vis: (s) => { const e = document.querySelector(s); return !!e && !!e.offsetParent && getComputedStyle(e).display !== 'none'; } };''')
    setup(r_pick)
    v2 = jv('RVMap._test.values()')
    ok('다시 읽어도 값 그대로', v2 == v1, {k: v2[k] for k in v2 if v2[k] != v1.get(k)})
    ok('다시 읽어도 카드 줄 접힘 그대로', J('document.getElementById("mp").classList.contains("cardsMin")') and '처리 수준 ' in J('document.getElementById("mOutsSum").textContent'),
       J('document.getElementById("mOutsSum").textContent'))
    J('__t.click("#mFold")')
    ok('다시 읽어도 카드 꼴 그대로 (끈 카드 흐림)', J('__t.q(".tcard[data-sec=gs]").classList.contains("off")') and not J('__t.q(".tcard[data-sec=dtm]").classList.contains("off")')
       and J('__t.q("select[data-k=demCell]").value') == '1' and J('__t.vis("#mCards")'))

    # 저장한 설정: 저장 → 값 바꿈 → 기본 (기본값) → 불러오기
    J('__t.click("#mTplSave")')
    ok('설정 이름 칸', J('__t.vis("#mTplRow")') and not J('__t.vis("#mTplSave")'))
    J('document.getElementById("mTplName").value = %s' % json.dumps(TPL))
    J('__t.click("#mTplOk")')
    ok('이 설정 저장', wait('document.getElementById("mTpl").value === %s' % json.dumps(TPL), 10) and TPL in api.templates_list()
       and api.template_load(TPL)['values'] == v1 and J('__t.vis("#mTplSave")'), api.templates_list())
    J('__t.set("input[data-k=ctBase]", "3")'); J('__t.sw(".sw[data-o=las]", false)')
    ok('값 바꿈', J('RVMap._S.v.ctBase') == 3 and 'las' not in jv('[...RVMap._S.outs]'))
    J('__t.set("#mTpl", "")')
    vd = jv('RVMap._test.values()')
    ok('기본 = 기본값 (3D 만 · 1.0.17 과 같은 설정)', vd['outs'] == ['mesh'] and vd['q'] == 'normal' and vd['feat'] == 'normal' and vd['demCell'] == 0.5
       and vd['orthoRes'] == 'auto' and vd['meshRatio'] == 100 and vd['meshFmt'] == [] and vd['ctBase'] == 0 and J('document.getElementById("mQName").textContent') == '표준', vd)
    J('__t.set("#mTpl", %s)' % json.dumps(TPL))
    ok('저장한 설정 불러오기 = 저장한 값', wait('JSON.stringify(RVMap._test.values()) === %s' % json.dumps(json.dumps(v1, ensure_ascii=False, separators=(',', ':'))), 10),
       jv('RVMap._test.values()'))

    # 만드는 동안 단계 시간표 (가짜 작업 · 화면만) — 실제 만들기 전에 꼴 확인
    J('''(() => { RVMap._S.job = "시험"; RVMap._test.runStart(); RVMap._test.lockUi(true); })()''')
    tl = jv('''({ names: [...document.querySelectorAll("#mTime .ts b")].map(e => e.textContent), cards: __t.vis("#mCards"), time: __t.vis("#mTime"),
        locked: document.getElementById("mAll").disabled && document.getElementById("mQBtn").disabled && document.getElementById("mClu").disabled,
        go: document.querySelector(".mstep[data-step=go]").classList.contains("run"), stage: ["특징점", "카메라 풀기", "모양 계산 (점구름)", "측량용 모양 계산", "점구름 · 함께 처리 (도우미 1대) · 조각 1/4",
          "3D 면 만들기", "질감 입히기", "결과물 만들기", "가우시안 학습 1 / 9걸음", "끝"].map(l => RVMap._test.stageOf(l)) })''')
    ok('단계 시간표 꼴 (사진 맞추기 → 점구름 → 면 만들기 → 질감 입히기 → 정사 · 지형 → 끝)', tl['names'] == ['사진 맞추기', '점구름', '면 만들기', '질감 입히기', '정사 · 지형', '끝']
       and tl['time'] and not tl['cards'] and tl['locked'] and tl['go'], tl)
    ok('엔진 진행 글 → 단계', tl['stage'] == [0, 0, 1, 1, 1, 2, 3, 4, -1, 5], tl['stage'])
    J('RVMap._S.job = null; RVMap._test.lockUi(false); RVMap._S.run = null;')
    ok('잠금 풀면 카드 줄 다시', J('__t.vis("#mCards")') and not J('__t.vis("#mTime")') and not J('document.getElementById("mAll").disabled'))

    if UI_ONLY:
        w.destroy()
        return
    # ---------------------------------------------------------------- 만들기 (실제)
    J('__t.click("#mArea")')
    J('''(() => { const el = document.getElementById("mMap"), b = el.getBoundingClientRect();
          const ev = (t, x, y) => el.dispatchEvent(new PointerEvent(t, { clientX: x, clientY: y, bubbles: true, pointerId: 1 }));
          ev("pointerdown", b.left + b.width * 0.2, b.top + b.height * 0.2); ev("pointermove", b.left + b.width * 0.8, b.top + b.height * 0.8);
          ev("pointerup", b.left + b.width * 0.8, b.top + b.height * 0.8); })()''')
    n_sel = J('RVMap._S.sel.size')
    ok('작업 영역으로 고름 · «작업 영역 › 지정 영역»', 3 <= n_sel <= len(files) - 2 and J('__t.q(".tcard[data-sec=ortho] .roi").textContent') == '지정 영역'
       and '지정 영역' in J('__t.q(".mstep[data-step=area] em").textContent'), n_sel)
    J('RVMap._S.sel = new Set(RVMap._S.photos.map(p => p.name)); RVMap._test.draw();')     # 시험은 고른 사진 전체로 (작은 폴더)
    J('__t.click("#mBuild")')
    ok('만들기 시작', wait('!!RVMap._S.job', 15))
    ok('단계 시간표 보임 · 카드 줄 숨김 · 실행 기록 접힘', wait('__t.vis("#mTime .ts")', 5) and not J('__t.vis("#mCards")') and not J('__t.vis("#mLogBox")'))
    t0 = time.time(); seen_pct = 0; seen_idx = -1; seen_left = set(); seen_eta = ''; seen_gone = ''; seen_done = 0
    while time.time() - t0 < 2400:
        try:
            st = json.loads(J('window.RVMap ? JSON.stringify([document.getElementById("mBar").style.width, !!RVMap._S.job, RVMap._S.run ? RVMap._S.run.idx : -1, '
                              'document.getElementById("mLeft").textContent, document.getElementById("mEta").textContent, document.getElementById("mGone").textContent, '
                              'document.querySelectorAll("#mTime .ts.done").length]) : "[\\"\\", false]"'))
        except Exception:
            st = ['', False]
        try:
            seen_pct = max(seen_pct, float(st[0].rstrip('%') or 0))
        except ValueError:
            pass
        if len(st) > 2 and st[1]:
            seen_idx = max(seen_idx, st[2]); seen_left.add(st[3]); seen_done = max(seen_done, st[6])
            if re.match(r'^\d\d:\d\d$', st[4] or ''):
                seen_eta = st[4]
            if st[5] and st[5] != '–':
                seen_gone = st[5]
        if not st[1]:
            break
        time.sleep(2)
    ok('진행 막대가 움직임', seen_pct > 0, seen_pct)
    ok('단계 시간표가 앞으로 감 (끝난 단계 체크)', seen_idx >= 2 and seen_done >= 2, [seen_idx, seen_done])
    ok('남은 시간 · 끝 예상 · 지난 시간 글', any(('분' in x or x == '1분 안') for x in seen_left) and seen_eta and seen_gone, [sorted(seen_left)[:6], seen_eta, seen_gone])
    ok('끝나고 3D 보기로 열림', wait('location.href.indexOf("index.html") >= 0 && !!window.__map3d', 60), J('location.href'))
    ok('3D 그려짐', wait('window.__drawn > 0 || !!window.__err', 120), J('JSON.stringify([window.__mesh, window.__err || null])'))
    check_outputs(first=True)
    w.destroy()


def las_head(p):
    b = open(p, 'rb').read(227 + 4)
    return struct.unpack('<I', b[107:111])[0], b[24:26]


def log_text():
    return open(os.path.join(OUT, '매핑_기록.txt'), encoding='utf-8').read()


def check_outputs(first):
    R_ = os.path.join(OUT, '결과물')
    ex = lambda f: os.path.exists(os.path.join(R_, f))      # noqa: E731
    lg = log_text()
    if first:
        n_pick = len(open(os.path.join(OUT, 'work', 'pick.txt'), encoding='utf-8').read().split())
        ok('고른 사진만 씀 (pick.txt)', n_pick == len([f for f in os.listdir(PHOTOS) if f.lower().endswith('.jpg')]) - 2, n_pick)
        A_ = json.load(open(os.path.join(OUT, 'work', 'args.json'), encoding='utf-8'))
        want = {'feat': 'high', 'ortho_res': 'low', 'ortho_fill': 0, 'mesh_ratio': 50.0, 'mesh_formats': ['ply', 'obj'], 'las_density': 25,
                'las_classify': '1', 'dem_cell': 1.0, 'contour_base': 0.5, 'contour_label': 20.0, 'contour_clean': 1, 'tin_step': 2.0}
        ok('args.json = 고른 세부 설정', all(A_.get(k) == v for k, v in want.items()), {k: A_.get(k) for k in want})
        ok('결과물 파일 (켠 칸만 · 가우시안 없음)', all(ex(f) for f in ('정사영상.tif', 'DSM.tif', '지표면_DSM.xml', 'DTM.tif', '지표면_DTM.xml', '등고선.dxf',
                                                           '점구름.las', '3D_model.ply', '3D_model.obj', '3D_model.mtl'))
           and not any('가우시안' in f or f.lower().endswith('.splat') for f in os.listdir(R_)), sorted(os.listdir(R_)))
        m = re.search(r'정사영상 원본 사진 재투영 · 칸 ([\d.]+) cm · 사진 GSD ([\d.]+) cm', lg)
        ok('정사 해상도 낮음 = 칸 ≈ GSD × 4', m and abs(float(m.group(1)) / float(m.group(2)) - 4) < 0.15, m and m.groups())
        ok('지능형 홀 채우기 끔 (정보.txt)', '지능형 홀 채우기 끔' in open(os.path.join(R_, '정보.txt'), encoding='utf-8').read())
        ok('특징점 높음 = 사진 크기 × 1.5 (정보.txt)', '특징점 사진 3000 px (특징 포인트 밀도 높음)' in open(os.path.join(R_, '정보.txt'), encoding='utf-8').read()
           if Q == 'fast' else '특징 포인트 밀도 높음' in open(os.path.join(R_, '정보.txt'), encoding='utf-8').read())
        from roadview.mapping import TARGET_FACES
        ok('3D 축소 비율 50 % → 세모 한도 절반 (정보.txt)', '3D 세모 한도 %d (모델 축소 비율 50 %%)' % (TARGET_FACES[Q] // 2)
           in open(os.path.join(R_, '정보.txt'), encoding='utf-8').read())
        w = [float(x) for x in open(os.path.join(R_, 'DTM.tfw')).read().split()]
        ok('DEM 크기 1:2500 = 1 m 칸', abs(w[0] - 1.0) < 1e-6, w[0])
        import numpy as np
        P = np.array([[float(v) for v in s.split()] for s in re.findall(r'<P id="\d+">([^<]+)</P>', open(os.path.join(R_, '지표면_DSM.xml'), encoding='utf-8').read())])
        de = np.diff(np.unique(np.round(P[:, 1], 3)))
        ok('TIN 샘플 거리 200 cm = 점 간격 2 m', len(P) and abs(np.median(de) - 2.0) < 0.01, [len(P), float(np.median(de))])
        dx = open(os.path.join(R_, '등고선.dxf'), encoding='cp949').read().split('\n')
        zs = [float(dx[i + 1]) for i in range(len(dx) - 1) if dx[i].strip() == '30' and i > 2 and 'POLYLINE' in ''.join(dx[max(0, i - 12):i])]
        ok('등고선 기준면 0.5 m', zs and all(abs((z - 0.5) - round(z - 0.5)) < 1e-6 for z in zs), sorted(set(zs))[:6])
        ok('등고선 주석 20 m · 정리 (기록)', re.search(r'등고선 표고 글 \d+개 \(20 m 마다\)', lg) and '등고선 정리: 짧은 조각' in lg,
           re.findall(r'등고선[^\n]*', lg)[-4:])
        n_las, ver = las_head(os.path.join(R_, '점구름.las'))
        m2 = re.search(r'점구름 LAS (\d+)점 · 밀도 25%( · 땅 분류 (\d+)%)?', lg)
        ok('LAS 밀도 25 % · 지면 분류', m2 and int(m2.group(1)) == n_las and m2.group(2), [n_las, m2 and m2.group(0)])
        hd = open(os.path.join(R_, '3D_model.ply'), 'rb').read(4000).split(b'end_header')[0].decode('latin1')
        nv = int(re.search(r'element vertex (\d+)', hd).group(1)); nf = int(re.search(r'element face (\d+)', hd).group(1))
        texs = re.findall(r'comment TextureFile (\S+)', hd)
        ok('3D PLY 꼴 (double · 질감 파일 있음)', 'property double x' in hd and texs and all(os.path.exists(os.path.join(R_, t)) for t in texs), [nv, nf, len(texs)])
        tfw = [float(x) for x in open(os.path.join(R_, 'DSM.tfw')).read().split()]
        objv = [ln for ln in open(os.path.join(R_, '3D_model.obj'), encoding='ascii') if ln.startswith(('v ', 'f '))]
        vx = [float(ln.split()[1]) for ln in objv[:50] if ln.startswith('v ')]
        ok('3D OBJ 꼴 (꼭짓점 · 세모 수 = PLY · 도면 좌표 = DSM 자리)', sum(ln.startswith('v ') for ln in objv) == nv and sum(ln.startswith('f ') for ln in objv) == nf
           and vx and abs(vx[0] - tfw[4]) < 2000, [nv, nf, vx[:1], tfw[4]])
        info = open(os.path.join(R_, '정보.txt'), encoding='utf-8').read()
        ok('정보.txt 새 설정', all(s in info for s in ('3D_model.ply', '3D_model.obj', '밀도 25 %', '(1:2500)', '고도 기준면 0.5 m', '20 m 마다', 'DSM 2 m 솎음')),
           [s for s in ('3D_model.ply', '3D_model.obj', '밀도 25 %', '(1:2500)', '고도 기준면 0.5 m', '20 m 마다', 'DSM 2 m 솎음') if s not in info])


def rerun():
    """결과물 다듬기 길 (app._rerun 과 같은 옵션 · --reuse-only): DEM 2 m · 주석 없음 · LAS 100 % · 3D 내보내기 없음 · 정사 자동."""
    api = A.Api(); api._project = OUT
    D = api._map_args()
    ok('다듬기 설정 = args.json 세부 설정', D.get('mesh_formats') == ['ply', 'obj'] and D.get('dem_cell') == 1.0 and D.get('las_density') == 25, D)
    n0 = las_head(os.path.join(OUT, '결과물', '점구름.las'))[0]
    D.update(dem_cell=2, contour_label=0, las_density=100, mesh_formats=[], ortho_res='auto', ortho_fill=1)
    args = [D['photos'], '--out', OUT, '--quality', D['quality'], '--title', D['title'], '--crs', D['crs'], '--outputs', ','.join(D['outputs']),
            '--contour', str(D['contour']), '--reuse-only', '--names', D['names']] + A.Api._opt_args(D)
    cmd, cwd = A._cli('map', args)
    t0 = time.time()
    r = subprocess.run(cmd, cwd=cwd, capture_output=True, env=dict(os.environ, PYTHONIOENCODING='utf-8'))
    tail = r.stdout.decode('utf-8', 'replace').strip().splitlines()[-3:]
    ok('다듬기 (앞 단계 다시 씀 · 처음부터 안 함)', r.returncode == 0 and '지난번 것 씀' in log_text() and time.time() - t0 < 600, [r.returncode, round(time.time() - t0), tail])
    R_ = os.path.join(OUT, '결과물')
    ok('다듬기: DEM 2 m', abs(float(open(os.path.join(R_, 'DTM.tfw')).read().split()[0]) - 2.0) < 1e-6)
    dx = open(os.path.join(R_, '등고선.dxf'), encoding='cp949').read()
    ok('다듬기: 주석 간격 0 = 표고 글 없음', '\nTEXT\n' not in dx)
    n1 = las_head(os.path.join(R_, '점구름.las'))[0]
    ok('다듬기: LAS 100 % ≈ 25 % × 4', abs(n1 - 4 * n0) <= 4, [n0, n1])
    ok('다듬기: 3D 내보내기 안 고름 → 지움', not any(f.startswith('3D_model') for f in os.listdir(R_)), sorted(os.listdir(R_)))
    m = re.findall(r'정사영상 원본 사진 재투영 · 칸 ([\d.]+) cm · 사진 GSD ([\d.]+) cm', log_text())
    ok('다듬기: 정사 자동 (칸 < 낮음)', len(m) >= 2 and float(m[-1][0]) < float(m[-2][0]), m[-2:])


def main():
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    api = A.Api()
    w = webview.create_window('시험', A.HOME_HTML.resolve().as_uri(), js_api=api, width=1500, height=920, minimized=True)
    api._window = w
    try:
        webview.start(run, (w, api), gui='edgechromium', private_mode=True)
    finally:
        f = A.TPL_DIR / (TPL + '.json')
        if f.exists():
            f.unlink()
    if not UI_ONLY and os.path.exists(os.path.join(OUT, '결과물', '3D_model.ply')):
        rerun()
    print('== %d 실패' % len(FAIL), FAIL)
    return 1 if FAIL else 0


if __name__ == '__main__':
    sys.exit(main())
