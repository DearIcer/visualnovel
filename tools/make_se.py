# -*- coding: utf-8 -*-
# 合成剧情音效（44.1kHz 16bit 单声道 wav），输出到 assets/se/。
import os

import numpy as np

SR = 44100
DST = "assets/se"


def save(name, data):
    import struct
    import wave

    os.makedirs(DST, exist_ok=True)
    data = np.clip(data, -1.0, 1.0)
    pcm = (data * 32767).astype(np.int16)
    path = os.path.join(DST, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print("[完成]", path, f"{len(data)/SR:.2f}s")


def thump(t, at, freq=55.0, amp=1.0, decay=14.0):
    """低频心跳鼓点：衰减正弦 + 一点噪声。"""
    x = np.maximum(0.0, t - at)
    env = np.exp(-decay * x) * (x > 0)
    return amp * env * np.sin(2 * np.pi * freq * x)


def make_heartbeat():
    dur = 1.6
    t = np.arange(int(SR * dur)) / SR
    # 两次「怦-怦」
    d = (thump(t, 0.05) + 0.7 * thump(t, 0.28)
         + 0.85 * thump(t, 0.85) + 0.6 * thump(t, 1.08))
    save("se_heartbeat", d * 0.9)


def make_phone():
    dur = 1.0
    t = np.arange(int(SR * dur)) / SR
    # 手机震动：170Hz 方波，25Hz 脉冲门控
    carrier = np.sign(np.sin(2 * np.pi * 170 * t))
    gate = (np.sin(2 * np.pi * 25 * t) > 0).astype(float)
    # 前后各一段嗡鸣
    segments = ((t < 0.3) | ((t > 0.45) & (t < 0.75))).astype(float)
    d = carrier * gate * segments * 0.35
    save("se_phone", d)


def make_knock():
    dur = 1.4
    t = np.arange(int(SR * dur)) / SR
    rng = np.random.default_rng(7)
    d = np.zeros_like(t)
    for at in (0.1, 0.5, 0.9):
        x = np.maximum(0.0, t - at)
        env = np.exp(-28.0 * x) * (x > 0)
        noise = rng.standard_normal(len(t))
        d += env * (0.6 * np.sin(2 * np.pi * 190 * x) + 0.25 * noise)
    save("se_knock", d * 0.85)


def make_stinger():
    dur = 1.8
    t = np.arange(int(SR * dur)) / SR
    rng = np.random.default_rng(3)
    # 不协和金属刺响：高频簇 + 噪声
    freqs = [1244.5, 1567.98, 1661.2, 2489.0, 2637.0]
    d = np.zeros_like(t)
    for i, f in enumerate(freqs):
        env = np.exp(-(3.0 + i * 0.8) * t)
        d += env * np.sin(2 * np.pi * f * t + i * 1.3) / len(freqs)
    attack = np.minimum(1.0, t / 0.01)
    d = d * attack * 1.6
    d += np.exp(-25 * t) * rng.standard_normal(len(t)) * 0.12
    save("se_stinger", d * 0.8)


if __name__ == "__main__":
    make_heartbeat()
    make_phone()
    make_knock()
    make_stinger()
