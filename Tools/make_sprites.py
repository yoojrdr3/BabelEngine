"""WebPrototype/ 의 캐릭터 일러스트에서 배경을 지워 투명 PNG 스프라이트를 만든다.

사용: python -X utf8 Tools/make_sprites.py   (프로젝트 루트에서)
출력: Assets/BabelEngine/Textures/Characters/*.png  (Unity 가 스프라이트로 가져온다)

배경은 가장자리에서 시작하는 색 범위 채우기(flood fill)로 지운다. 배경이 거의 균일한 일러스트 전용이다.
"""
import os
from collections import deque

import numpy as np
from PIL import Image, ImageFilter

SRC = "WebPrototype"
DST = "Assets/BabelEngine/Textures/Characters"
HEIGHT = 512  # 출력 높이(px)
ABS = 48      # 가장자리 배경색과의 최대 차이

# 이름 → (원본 파일, 잘라낼 영역(비율 x0,y0,x1,y1) 또는 None, 이웃 색 허용 오차, 아래쪽 남길 비율(바닥 그림자 제거))
SPRITES = {
    "Metaphos": ("메타포스 - 측면.png", None, 7, 0.945),
    "Tanarc": ("타나르크.png", (0.03, 0.0, 0.36, 1.0), 7, 0.955),
    "Demon_Commander": ("혼돈 악마.png", None, 8, 0.945),
    "Demon_Leader": ("파괴 악마 - 전신.png", None, 8, 1.0),
    "Demon_Member": ("종말 악마 - 전신.png", None, 8, 0.93),
}


def flood_background(rgb: np.ndarray, tol: float, absolute: float) -> np.ndarray:
    """가장자리에서 시작해 이웃과 색이 tol 이내로 이어지고, 가장자리 중앙값 색과 absolute 이내인 영역(= 배경) 마스크."""
    h, w, _ = rgb.shape
    img = rgb.astype(np.int16)
    border = np.concatenate([img[0], img[-1], img[:, 0], img[:, -1]])
    ref = np.median(border, axis=0)
    bg = np.zeros((h, w), bool)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            bg[y, x] = True
            q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if not bg[y, x]:
                bg[y, x] = True
                q.append((y, x))
    while q:
        y, x = q.popleft()
        c = img[y, x]
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not bg[ny, nx]:
                if np.abs(img[ny, nx] - c).max() <= tol and np.abs(img[ny, nx] - ref).max() <= absolute:
                    bg[ny, nx] = True
                    q.append((ny, nx))
    return bg


def main():
    os.makedirs(DST, exist_ok=True)
    for name, (fn, crop, tol, keep) in SPRITES.items():
        path = os.path.join(SRC, fn)
        if not os.path.exists(path):
            print("없음:", path)
            continue
        im = Image.open(path).convert("RGB")
        if crop:
            w, h = im.size
            im = im.crop((int(w * crop[0]), int(h * crop[1]), int(w * crop[2]), int(h * crop[3])))
        # 큰 그림은 먼저 줄여서 채우기 속도를 확보
        scale = 900 / max(im.size)
        if scale < 1:
            im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
        rgb = np.array(im)
        bg = flood_background(rgb, tol, ABS)
        alpha = Image.fromarray(np.where(bg, 0, 255).astype(np.uint8))
        # 구멍 메우기·가장자리 정리: 살짝 침식 후 번짐
        alpha = alpha.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1.2))
        out = im.convert("RGBA")
        out.putalpha(alpha)
        bbox = alpha.point(lambda v: 255 if v > 20 else 0).getbbox()
        if bbox:
            out = out.crop(bbox)
        if keep < 1.0:
            out = out.crop((0, 0, out.width, int(out.height * keep)))
            bb = out.getchannel('A').point(lambda v: 255 if v > 20 else 0).getbbox()
            if bb:
                out = out.crop(bb)
        r = HEIGHT / out.height
        out = out.resize((max(1, int(out.width * r)), HEIGHT), Image.LANCZOS)
        out.save(os.path.join(DST, name + ".png"))
        print(name, out.size)


if __name__ == "__main__":
    main()
