"""드론 로드뷰 — 창 프로그램 입구 (설치본의 드론로드뷰.exe)."""
import multiprocessing
import sys

if __name__ == '__main__':
    multiprocessing.freeze_support()
    try:
        from ctypes import windll
        windll.shcore.SetProcessDpiAwareness(2)      # 글자 흐림 막기
    except Exception:
        pass
    from roadview.app import main
    sys.exit(main())
