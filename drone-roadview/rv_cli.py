"""드론 로드뷰 — 뒤에서 일하는 명령 프로그램 (설치본의 rv_cli.exe).

    rv_cli build 영상폴더 --out 결과폴더 --interval 5 ...
    rv_cli share 결과폴더
    rv_cli pipes 결과폴더 관로.shp --dia-field 관경
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
    else:
        print(__doc__)
        return 2
    return m(rest)


if __name__ == '__main__':
    sys.exit(main())
