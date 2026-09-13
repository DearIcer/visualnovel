# -*- coding: utf-8 -*-
# 绿幕抠图：将 assets_raw/sprites 下的立绘去背、去绿边、裁剪后输出到 assets/characters/。
import colorsys
import os
import sys

from PIL import Image, ImageFilter

SRC = "assets_raw/sprites"
DST = "assets/characters"


def chroma_key(img: Image.Image) -> Image.Image:
    """numpy 向量化绿幕去除：按 hue/饱和度/亮度判定绿色，去绿溢色并羽化。"""
    import numpy as np

    arr = np.asarray(img.convert("RGBA")).astype(np.float32)
    r, g, b, a = arr[..., 0] / 255.0, arr[..., 1] / 255.0, arr[..., 2] / 255.0, arr[..., 3]

    mx = np.maximum(np.maximum(r, g), b)
    mn = np.minimum(np.minimum(r, g), b)
    diff = mx - mn
    # hue（度）
    hue = np.zeros_like(mx)
    nz = diff > 1e-6
    idx = nz & (mx == r)
    hue[idx] = (60 * ((g - b) / np.maximum(diff, 1e-6)) % 360)[idx]
    idx = nz & (mx == g)
    hue[idx] = (60 * ((b - r) / np.maximum(diff, 1e-6)) + 120)[idx]
    idx = nz & (mx == b)
    hue[idx] = (60 * ((r - g) / np.maximum(diff, 1e-6)) + 240)[idx]
    sat = np.where(mx > 1e-6, diff / np.maximum(mx, 1e-6), 0)
    val = mx

    is_green = (hue >= 70) & (hue <= 165) & (sat > 0.25) & (val > 0.15)
    greenness = np.where(is_green, np.minimum(1.0, sat * 1.6), 0.0)

    a2 = a * (1.0 - greenness)
    # 去绿溢色：把 g 压到 r/b 均值附近
    g2 = np.minimum(arr[..., 1], ((arr[..., 0] + arr[..., 2]) / 2 * 1.15))

    out = np.stack([arr[..., 0], g2, arr[..., 2], a2], axis=-1)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def process(name: str):
    src = os.path.join(SRC, name + ".png")
    if not os.path.exists(src):
        print(f"[缺失] {src}")
        return
    img = Image.open(src)
    img = chroma_key(img)

    # 裁剪透明边缘（留少量内边距）
    alpha = img.getchannel("A")
    bbox = alpha.getbbox()
    if bbox:
        pad = 8
        l, t, r, b = bbox
        l = max(0, l - pad); t = max(0, t - pad)
        r = min(img.width, r + pad); b = min(img.height, b + pad)
        img = img.crop((l, t, r, b))

    # 轻微羽化边缘
    a = img.getchannel("A").filter(ImageFilter.GaussianBlur(0.6))
    img.putalpha(a)

    # 解析 角色_表情
    char, emotion = name.rsplit("_", 1)
    out_dir = os.path.join(DST, char)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, emotion + ".png")
    img.save(out)
    print(f"[完成] {out} ({img.width}x{img.height})")


def main():
    names = sys.argv[1:]
    if not names:
        names = [os.path.splitext(f)[0] for f in os.listdir(SRC) if f.endswith(".png")]
    for n in names:
        process(n)


if __name__ == "__main__":
    main()
