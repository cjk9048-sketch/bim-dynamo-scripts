"""3D 매핑 만들기 시험 (실제 WebView2 · 작은 창 · 실제로 매핑을 돌림) — 1126 Terra 꼴 설정 칸.

    python viewer_test\\check_home_mapping.py 사진폴더 결과폴더 [품질=fast] [--ui-only]

화면: 사진 선택 (고른 파일만) → 칸 접고 펴기 (켜고 끄기와 따로) → 결과물 칸 켜고 끄기 (끄면 흐림 · 누를 수 없음 · 다시 켜면 값 그대로)
  → 켠 칸 수 · 예상 시간 → 장치 성능 · 실행 기록 · 설정 칸 접기 · 관심 지역 → 값 기억 (다시 읽어도) → 템플릿 저장 · 불러오기 · 일반
만들기 (--ui-only 가 아니면): 범위(가운데 절반) → [재구성 시작] → 끝나면 3D 보기 → 결과물이 고른 대로 (정사 해상도 · DEM 칸 · 등고선 기준면 ·
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
    # Terra 꼴 틀
    secs = jv('[...document.querySelectorAll("#mp [data-sec]")].map(e => e.dataset.sec)')
    ok('칸 차례 (도화 꼴)', secs == ['data', 'proc', 'at', 'ortho', 'mesh', 'gs', 'las', 'dtm', 'contour', 'dsm', 'tips'], secs)
    ok('설정 머리 · [3D 매핑] · 설정 저장 · 아래 줄', J('document.querySelector(".tshead b").textContent') == '만들기 설정'
       and J('document.getElementById("mBuild").textContent') == '3D 매핑' and J('document.getElementById("mTplSave").textContent') == '이 설정 저장'
       and J('__t.vis("#mDev") && __t.vis("#mLogBtn") && __t.vis("#mCrsBar")'), J('document.getElementById("mCrsBar").textContent'))
    ok('함께 처리 단추 (cluster.js 그대로)', J('__t.vis("#mClu")') and J('document.getElementById("mClu").textContent') in ('켜짐', '꺼짐'))
    # 장치 성능
    ok('장치 성능 읽음', wait('!!RVMap._S.sys', 20) and J('document.getElementById("mDevDot").className') in ('dot ok', 'dot warn'), jv('RVMap._S.sys'))
    J('__t.click("#mDev")')
    ok('장치 성능 자세히', J('__t.vis("#mDevPop")') and 'CPU' in J('document.getElementById("mDevPop").textContent'), J('document.getElementById("mDevPop").textContent'))
    J('__t.click("#mDev")')
    ok('장치 성능 닫기', not J('__t.vis("#mDevPop")'))
    si = api.sysinfo()
    ok('가우시안 칸 = 장치 (못 하면 흐림 + 이유)', J('document.getElementById("mGsCard").classList.contains("na")') == (not si['gs_ok'])
       and (si['gs_ok'] or J('__t.vis("#mGsWhy")')), [si['gs_ok'], si['gs_why']])

    # 접고 펴기 (켜고 끄기와 따로)
    J('__t.sw(".sw[data-o=las]", false)')
    J('__t.click(".tcard[data-sec=las] .tch")')
    ok('칸 접기 (머리줄)', J('__t.q(".tcard[data-sec=las]").classList.contains("fold")') and not J('__t.vis(".tcard[data-sec=las] .tcb")')
       and json.loads(J('localStorage.getItem("rv.map.ui")'))['fold'].get('las') is True)
    J('__t.click(".tcard[data-sec=las] .sw[data-o=las]")')
    ok('켜고 끄기는 접기와 따로 (접힌 채 켜짐)', J('__t.q(".tcard[data-sec=las]").classList.contains("fold")') and 'las' in jv('[...RVMap._S.outs]'))
    J('__t.click(".tcard[data-sec=las] .tch")')
    ok('칸 펴기', not J('__t.q(".tcard[data-sec=las]").classList.contains("fold")') and J('__t.vis(".tcard[data-sec=las] .tcb")'))
    J('__t.click(".tsec[data-sec=data] .tsh")')
    ok('큰 칸 접기 (데이터 세트)', not J('__t.vis("#mPick")'))
    J('__t.click(".tsec[data-sec=data] .tsh")')
    ok('큰 칸 펴기', J('__t.vis("#mPick")'))

    # 켜고 끄기: 끄면 흐림 · 누를 수 없음 · 값 그대로 / 켜면 바로 활성
    J('__t.sw(".sw[data-o=ortho]", true)')
    J('__t.set(".tcard[data-sec=ortho] select[data-k=orthoRes]", "mid")')
    fill0 = J('RVMap._S.v.orthoFill')
    e_on = J('document.getElementById("mEst").textContent')
    J('__t.sw(".sw[data-o=ortho]", false)')
    e_off = J('document.getElementById("mEst").textContent')
    st = jv('''(() => { const c = __t.q(".tcard[data-sec=ortho]"), b = c.querySelector(".tcb"), s = c.querySelector("select[data-k=orthoRes]"), r = s.getBoundingClientRect();
        const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
        return { off: c.classList.contains("off"), op: +getComputedStyle(b).opacity, pe: getComputedStyle(b).pointerEvents, dis: s.disabled,
                 hit: hit === s, vis: __t.vis(".tcard[data-sec=ortho] select[data-k=orthoRes]") }; })()''')
    ok('끈 칸 = 흐림 · 누를 수 없음 · 보이기는 함', st['off'] and st['op'] < 0.6 and st['pe'] == 'none' and st['dis'] and not st['hit'] and st['vis'], st)
    J('__t.click(".tcard[data-sec=ortho] .sw[data-k=orthoFill]")')
    ok('끈 칸 안 단추 눌러도 안 바뀜', J('RVMap._S.v.orthoFill') == fill0)
    ok('켠 칸 수 · 예상 시간 바로 바뀜', re.search(r'결과물 (\d+)개', e_on) and int(re.search(r'결과물 (\d+)개', e_on).group(1)) == int(re.search(r'결과물 (\d+)개', e_off).group(1)) + 1, [e_on, e_off])
    J('__t.sw(".sw[data-o=ortho]", true)')
    ok('다시 켜면 값 그대로 · 활성', J('__t.q(".tcard[data-sec=ortho] select[data-k=orthoRes]").value') == 'mid'
       and not J('__t.q(".tcard[data-sec=ortho] select[data-k=orthoRes]").disabled') and not J('__t.q(".tcard[data-sec=ortho]").classList.contains("off")'))
    J('__t.click(".tcard[data-sec=ortho] .sw[data-k=orthoFill]")')
    ok('켠 칸 안 단추 = 바뀜', J('RVMap._S.v.orthoFill') == (not fill0))

    # 관심 지역 › = 범위 그리기 · 이름 연필 · 실행 기록 · 설정 칸 접기 · 팁
    J('__t.click(".tcard[data-sec=ortho] .tlink[data-act=area]")')
    ok('관심 지역 › = 범위 그리기 켬', J('RVMap._S.area') is True and J('document.getElementById("mArea").classList.contains("on")'))
    J('__t.click("#mArea")')
    J('__t.click("#mRen")')
    ok('이름 연필 = 이름 칸', J('document.activeElement && document.activeElement.id') == 'mTitle')
    J('__t.click("#mLogBtn")')
    ok('실행 기록 펼치기', J('__t.vis("#mLogBox")'))
    J('__t.click("#mHdrLog")')
    ok('실행 기록 닫기 (윗줄 단추)', not J('__t.vis("#mLogBox")'))
    w0 = J('document.querySelector("#mp > aside").getBoundingClientRect().width')
    J('__t.click("#mFold")')
    w1 = J('document.querySelector("#mp > aside").getBoundingClientRect().width')
    ok('설정 칸 접기 [»]', J('document.getElementById("mp").classList.contains("sidecol")') and w1 < 60 and not J('__t.vis("#mp > .sideGrip")'), [w0, w1])
    J('__t.click("#mHdrSet")')
    w2 = J('document.querySelector("#mp > aside").getBoundingClientRect().width')
    ok('설정 칸 펴기 (윗줄) · 폭 손잡이 그대로', abs(w2 - w0) < 2 and J('!!document.querySelector("#mp > .sideGrip")') and J('__t.vis("#mp > .sideGrip")'), [w0, w2])
    J('''(() => { const g = document.querySelector("#mp > .sideGrip"), r = g.getBoundingClientRect(), x = r.left + r.width / 2;
          const ev = (t, x2) => g.dispatchEvent(new PointerEvent(t, { clientX: x2, clientY: 300, bubbles: true, pointerId: 7, button: 0 }));
          ev("pointerdown", x); ev("pointermove", x - 80); ev("pointerup", x - 80); })()''')
    w3 = J('document.querySelector("#mp > aside").getBoundingClientRect().width')
    J('document.querySelector("#mp > .sideGrip").dispatchEvent(new MouseEvent("dblclick", { bubbles: true }))')
    w4 = J('document.querySelector("#mp > aside").getBoundingClientRect().width')
    ok('폭 손잡이 끌기 (+80) · 두 번 누르면 처음 폭', abs(w3 - (w0 + 80)) < 3 and abs(w4 - 360) < 2, [w0, w3, w4])
    J('__t.click(".tsec[data-sec=tips] .tsh")')
    J('__t.click("#mHelp")')
    ok('도움말 = 팁 펼침', J('__t.vis("#mTips .tsb")'))

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
    J('__t.click(".tsec[data-sec=data] .tsh")')                    # 접힘도 기억하는지
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
    ok('다시 읽어도 접힘 그대로 (사진 접힘 · 도움말 펼침)', J('__t.q(".tsec[data-sec=data]").classList.contains("fold")') and not J('__t.q(".tsec[data-sec=tips]").classList.contains("fold")'))
    ok('다시 읽어도 칸 꼴 그대로 (끈 칸 흐림)', J('__t.q(".tcard[data-sec=gs]").classList.contains("off")') and not J('__t.q(".tcard[data-sec=dtm]").classList.contains("off")')
       and J('__t.q("select[data-k=demCell]").value') == '1')

    # 템플릿: 저장 → 값 바꿈 → 일반 (기본값) → 불러오기
    J('__t.click("#mTplSave")')
    ok('템플릿 이름 칸', J('__t.vis("#mTplRow")') and not J('__t.vis("#mTplSave")'))
    J('document.getElementById("mTplName").value = %s' % json.dumps(TPL))
    J('__t.click("#mTplOk")')
    ok('템플릿 저장', wait('document.getElementById("mTpl").value === %s' % json.dumps(TPL), 10) and TPL in api.templates_list()
       and api.template_load(TPL)['values'] == v1, api.templates_list())
    J('__t.set("input[data-k=ctBase]", "3")'); J('__t.sw(".sw[data-o=las]", false)')
    ok('값 바꿈', J('RVMap._S.v.ctBase') == 3 and 'las' not in jv('[...RVMap._S.outs]'))
    J('__t.set("#mTpl", "")')
    vd = jv('RVMap._test.values()')
    ok('일반 = 기본값 (3D 만 · 1.0.17 과 같은 설정)', vd['outs'] == ['mesh'] and vd['q'] == 'normal' and vd['feat'] == 'normal' and vd['demCell'] == 0.5
       and vd['orthoRes'] == 'auto' and vd['meshRatio'] == 100 and vd['meshFmt'] == [] and vd['ctBase'] == 0, vd)
    J('__t.set("#mTpl", %s)' % json.dumps(TPL))
    ok('템플릿 불러오기 = 저장한 값', wait('JSON.stringify(RVMap._test.values()) === %s' % json.dumps(json.dumps(v1, ensure_ascii=False, separators=(',', ':'))), 10),
       jv('RVMap._test.values()'))

    if UI_ONLY:
        w.destroy()
        return
    # ---------------------------------------------------------------- 재구성 (만들기)
    J('__t.click("#mArea")')
    J('''(() => { const el = document.getElementById("mMap"), b = el.getBoundingClientRect();
          const ev = (t, x, y) => el.dispatchEvent(new PointerEvent(t, { clientX: x, clientY: y, bubbles: true, pointerId: 1 }));
          ev("pointerdown", b.left + b.width * 0.2, b.top + b.height * 0.2); ev("pointermove", b.left + b.width * 0.8, b.top + b.height * 0.8);
          ev("pointerup", b.left + b.width * 0.8, b.top + b.height * 0.8); })()''')
    n_sel = J('RVMap._S.sel.size')
    ok('범위로 고름 · 관심 지역 = 그린 범위', 3 <= n_sel <= len(files) - 2 and J('__t.q(".tcard[data-sec=ortho] .roi").textContent') == '그린 범위', n_sel)
    J('RVMap._S.sel = new Set(RVMap._S.photos.map(p => p.name)); RVMap._test.draw();')     # 시험은 고른 사진 전체로 (작은 폴더)
    J('__t.click("#mBuild")')
    ok('만들기 시작', wait('!!RVMap._S.job', 15))
    ok('실행 기록 펼쳐짐', J('__t.vis("#mLogBox")'))
    t0 = time.time(); seen_pct = 0
    while time.time() - t0 < 2400:
        try:
            st = json.loads(J('window.RVMap ? JSON.stringify([document.getElementById("mBar").style.width, !!RVMap._S.job]) : "[\\"\\", false]"'))
        except Exception:
            st = ['', False]
        try:
            seen_pct = max(seen_pct, float(st[0].rstrip('%') or 0))
        except ValueError:
            pass
        if not st[1]:
            break
        time.sleep(2)
    ok('진행 막대가 움직임', seen_pct > 0, seen_pct)
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
