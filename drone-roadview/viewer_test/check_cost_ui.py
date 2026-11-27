"""3D 매핑 보기 · 개략공사비 화면 시험 (JACK 1127 · viewer/cost.js) — check_map3d_ui 의 «$» 단락만 따로 (관 1개 자료로도 됨).
결과 폴더를 작은 HTTP 서버로 열고 화면 없는 Edge (cdp.py) 를 진짜 마우스로 몬다. 그림은 --shots 폴더에 남김.

    python -X utf8 -I check_cost_ui.py --out <3D 매핑 결과 폴더 (튜토리얼 복사본 등)> --shots <그림 폴더>

  $  가시설 소계 = 손셈 · 전체 = 관로 본공사 + 부대 공종 = 합계 (손셈) · 억 · 만 글 · (i) 설명 · [단가표] 창 · 단가 고치면 금액 바뀜 ·
     보정 계수 · 포장 · [기본값] · × 닫기 · 모든 쪽 JS 오류 0
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402
import check_map3d_ug as ug  # noqa: E402
from check_map3d_ug import errs, report, serve, setup_page  # noqa: E402
from check_map3d_ctx import copy_site  # noqa: E402
import check_map3d_ui as cu  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD = os.path.abspath(a.out), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    copy_site(out)
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), cu.WIN, gl=False).__enter__()
    inp = ce.Inp(ed)
    ui = lambda eid: ug.ui_click(ed, inp, eid)        # noqa: E731
    try:
        print('== 열기 (프로그램 흉내 · 기억 지움)')
        report('열림', setup_page(ed, base + 'index_host.html'))
        ed.js('localStorage.clear(); sessionStorage.clear()')
        report('다시 열림', setup_page(ed, base + 'index_host.html'))
        errs(ed, '열기')
        print('== $ 개략공사비')
        ed.js("window.RV_PIPES.lines[0].shore = [{ a: 5, b: 14, t: 'hpile' }, { a: 20, b: 23.5, t: 'sheet' }]; __map3d.rebuild()")
        ui('qkShore')
        cu.cost_check(ed, ui, SD)
        ui('qkShore')
        report('$ 같은 단추 → 닫힘', not cu.vis(ed, 'qtyBox'))
        errs(ed, '끝')
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()
    for where, e in ug.R['errs']:
        print('  오류', where, e)
    print('== %s · 실패 %d %s' % ('통과' if ug.R['ok'] else '실패', len(ug.R['fails']), ug.R['fails']))
    return 0 if ug.R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
