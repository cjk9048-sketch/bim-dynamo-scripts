@echo off
rem 드론 360 로드뷰 만들기 창
rem   (명령줄로 돌리려면: python -m roadview.build 영상폴더 --out 결과폴더 --interval 5)
pushd "%~dp0"
start "" pythonw -m roadview.gui
popd
