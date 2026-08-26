# 리본 버튼 아이콘 생성 (PNG 32/16). 고해상도로 그린 뒤 축소해 선명하게.
import os

from PIL import Image, ImageDraw

OUT = r"c:\Users\user\Desktop\AI\revit-quantity-takeoff\src\DH.Takeoff.Revit\Resources"
os.makedirs(OUT, exist_ok=True)
S = 256
WHITE = (255, 255, 255, 255)
BLUE = (38, 111, 176, 255)
GREEN = (46, 139, 87, 255)
ORANGE = (224, 138, 43, 255)


def base(color):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((10, 10, S - 10, S - 10), radius=46, fill=color)
    return img, d


def make_setup():  # 매개변수 세팅 = 슬라이더(설정) 글리프
    img, d = base(BLUE)
    rows = [(92, 175), (138, 95), (184, 150)]  # (y, knob_x)
    for y, kx in rows:
        d.line((54, y, 202, y), fill=WHITE, width=14)
        d.ellipse((kx - 24, y - 24, kx + 24, y + 24), fill=WHITE)
        d.ellipse((kx - 24, y - 24, kx + 24, y + 24), outline=BLUE, width=9)
    return img


def make_export():  # 산출·내보내기 = 표 + 아래 화살표
    img, d = base(GREEN)
    d.rounded_rectangle((58, 48, 198, 150), radius=10, outline=WHITE, width=12)
    d.line((58, 99, 198, 99), fill=WHITE, width=8)
    d.line((128, 48, 128, 150), fill=WHITE, width=8)
    d.line((128, 150, 128, 210), fill=WHITE, width=16)
    d.polygon([(98, 196), (158, 196), (128, 226)], fill=WHITE)
    return img


def make_measure():  # 치수 자동입력 = 자(ruler) + 눈금
    img, d = base(ORANGE)
    d.rounded_rectangle((48, 96, 208, 160), radius=8, outline=WHITE, width=12)
    for x in (78, 108, 138, 168):  # 눈금
        d.line((x, 96, x, 124), fill=WHITE, width=8)
    return img


for name, fn in [("Setup", make_setup), ("Export", make_export), ("Measure", make_measure)]:
    full = fn()
    for sz in (32, 16):
        full.resize((sz, sz), Image.LANCZOS).save(os.path.join(OUT, f"{name}{sz}.png"))
print("WROTE icons to", OUT)
