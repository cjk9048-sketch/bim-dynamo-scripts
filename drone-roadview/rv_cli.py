"""드론 로드뷰 — 뒤에서 일하는 명령 프로그램 (설치본의 rv_cli.exe).

    rv_cli build 영상폴더 --out 결과폴더 --interval 5 ...
    rv_cli share 결과폴더
    rv_cli pipes 결과폴더 관로.shp --dia-field 관경
    rv_cli map 사진폴더 --out 결과폴더 --quality normal   (3D 매핑 · --cluster 공유폴더 = 옆 PC 와 함께 처리)
    rv_cli helper --board 공유폴더                          (함께 처리 도우미 · 앱 «도우미 모드»)
    rv_cli ai setup | ai run 매핑폴더 [--p360 360폴더] | ai status   (AI 부대공 인식 · 엔진은 처음 쓸 때 내려받기)
"""
import multiprocessing
import sys


def main():
    multiprocessing.freeze_support()          # 만들기 일꾼(ProcessPool)이 설치본에서도 뜨게
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    rest = sys.argv[2:]
    if cmd == 'build':
        from roadview.build import main as m
    elif cmd == 'share':
        from roadview.share import main as m
    elif cmd == 'pipes':
        from roadview.pipes_update import main as m
    elif cmd == 'map':
        from roadview.mapping import main as m
    elif cmd == 'helper':
        from roadview.cluster import helper_main as m
    elif cmd == 'ai':
        from roadview.ai_budae import main as m
    else:
        print(__doc__)
        return 2
    return m(rest)


if __name__ == '__main__':
    sys.exit(main())
