# -*- coding: utf-8 -*-
"""生成心海海的应用图标 app.ico(Q版:青绿发+红发带+红瞳)。"""
from PIL import Image, ImageDraw

S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

HAIR = (95, 200, 180, 255)
HAIR_DK = (62, 164, 146, 255)
SKIN = (255, 239, 226, 255)
RED = (229, 72, 77, 255)
EYE = (200, 60, 75, 255)
LINE = (74, 38, 48, 255)

# 后发(大圆)
d.ellipse((14, 14, 242, 242), fill=HAIR, outline=HAIR_DK, width=6)
# 两侧长发
d.polygon([(30, 120), (18, 232), (66, 200), (56, 120)], fill=HAIR, outline=None)
d.polygon([(226, 120), (238, 232), (190, 200), (200, 120)], fill=HAIR)
# 脸
d.ellipse((48, 66, 208, 218), fill=SKIN)
# 刘海(上半圆)
d.pieslice((40, 40, 216, 176), 180, 360, fill=HAIR)
# 刘海锯齿
for i, x in enumerate(range(56, 201, 29)):
    d.polygon([(x, 104), (x + 15, 128 if i % 2 == 0 else 118), (x + 29, 104)], fill=HAIR)
# 呆毛
d.line((128, 42, 118, 8), fill=HAIR_DK, width=10)
d.line((118, 10, 146, 4), fill=HAIR_DK, width=10)
# 眼睛
d.ellipse((88, 138, 118, 178), fill=EYE, outline=(110, 24, 34, 255), width=3)
d.ellipse((150, 138, 180, 178), fill=EYE, outline=(110, 24, 34, 255), width=3)
d.ellipse((94, 144, 106, 158), fill=(255, 255, 255, 240))
d.ellipse((156, 144, 168, 158), fill=(255, 255, 255, 240))
# 嘴(微笑)
d.arc((118, 176, 150, 200), 15, 165, fill=(190, 90, 100, 255), width=5)
# 腮红
d.ellipse((70, 172, 96, 186), fill=(255, 169, 180, 150))
d.ellipse((160, 172, 186, 186), fill=(255, 169, 180, 150))
# 红色蝴蝶结(右上)
d.polygon([(178, 44), (146, 22), (150, 58)], fill=RED)
d.polygon([(178, 44), (214, 20), (212, 58)], fill=RED)
d.ellipse((168, 34, 190, 56), fill=RED, outline=(167, 46, 56, 255), width=3)

img.save(r"D:\1\XinHaiHai\app.ico", sizes=[(256, 256), (64, 64), (48, 48), (32, 32), (16, 16)])
img.save(r"D:\1\XinHaiHai\icon_preview.png")
print("icon ok")
