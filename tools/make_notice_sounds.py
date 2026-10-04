#!/usr/bin/env python3
"""通知パッケージの、入室・退室の音を作る（計算で作った自作の音。素材は使っていない）。

    python tools/make_notice_sounds.py

出来る物:
  Packages/com.nagonago.notice/Runtime/Audio/notice-join.wav    2 音が上がる（ソ → ド）
  Packages/com.nagonago.notice/Runtime/Audio/notice-leave.wav   2 音が下がる（ミ → ド）

形式は、ほかの通知音と同じ（モノラル・16 bit・44.1 kHz）。大きさも、ほかの音に合わせてある。
"""
import wave
from pathlib import Path

import numpy as np

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "Packages" / "com.nagonago.notice" / "Runtime" / "Audio"
PEAK = 0.33


def note(freq: float, length: float) -> np.ndarray:
    """やわらかい 1 音。立ち上がりは 8 ms、あとは自然に消える"""
    t = np.arange(int(RATE * length)) / RATE
    tone = np.sin(2 * np.pi * freq * t) + 0.25 * np.sin(2 * np.pi * freq * 2 * t) + 0.08 * np.sin(2 * np.pi * freq * 3 * t)
    attack = np.minimum(1.0, t / 0.008)
    decay = np.exp(-t / (length * 0.28))
    release = np.minimum(1.0, (length - t) / 0.02)
    return tone * attack * decay * release


def chime(freqs, gap: float, length: float) -> np.ndarray:
    total = int(RATE * (gap * (len(freqs) - 1) + length))
    out = np.zeros(total)
    for i, f in enumerate(freqs):
        n = note(f, length)
        start = int(RATE * gap * i)
        out[start:start + len(n)] += n
    return out * (PEAK / np.abs(out).max())


def save(name: str, samples: np.ndarray) -> None:
    path = OUT / name
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((np.clip(samples, -1, 1) * 32767).astype(np.int16).tobytes())
    print(f"{name}: {len(samples) / RATE:.2f} s")


def main() -> None:
    save("notice-join.wav", chime([783.99, 1046.50], 0.11, 0.30))    # G5 → C6
    save("notice-leave.wav", chime([659.26, 523.25], 0.11, 0.30))    # E5 → C5


if __name__ == "__main__":
    main()
