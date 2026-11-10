# 리본 단추 그림(PNG 32 · 16) — 크게 그린 뒤 줄여 선명하게. 수량산출 애드인 그림과 같은 꼴(둥근 네모 + 흰 기호).
# 기호: 좌표 십자(두 점) + 점 둘을 잇는 선 = «두 점 맞춤».
import os
import sys

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "DH.CivilCoord.Revit", "Resources")
os.makedirs(OUT, exist_ok=True)
S = 256
WHITE = (255, 255, 255, 255)
TEAL = (18, 120, 132, 255)


def make():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((10, 10, S - 10, S - 10), radius=46, fill=TEAL)
    # 좌표 축(왼쪽 아래 원점)
    d.line((58, 200, 58, 52), fill=WHITE, width=12)
    d.line((58, 200, 206, 200), fill=WHITE, width=12)
    d.polygon([(40, 66), (76, 66), (58, 38)], fill=WHITE)
    d.polygon([(192, 182), (192, 218), (220, 200)], fill=WHITE)
    # 두 점과 잇는 선
    a, b = (104, 150), (176, 86)
    d.line((a[0], a[1], b[0], b[1]), fill=WHITE, width=10)
    for (x, y) in (a, b):
        d.ellipse((x - 20, y - 20, x + 20, y + 20), fill=WHITE)
        d.ellipse((x - 9, y - 9, x + 9, y + 9), fill=TEAL)
    return img


img = make()
for size in (32, 16):
    img.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, f"Coord{size}.png"))
print("ok", OUT, file=sys.stderr)
