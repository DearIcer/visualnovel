# -*- coding: utf-8 -*-
"""《青灯引》语音生成脚本。

读取 story/main.json，为每条 say / narrate 指令调用本地 Qwen3-TTS
（gradio API，http://127.0.0.1:8000/run_instruct）生成配音，
输出到 assets/voice/{scene}_{index:03d}.wav。

已存在的音频文件会跳过，可中断后重复运行以续跑。
生成完毕后用 inject_voice.py 把 voice 指令写回剧情脚本。
"""
import json
import shutil
import sys
import time
from pathlib import Path

from gradio_client import Client

ROOT = Path(__file__).resolve().parents[2]
STORY = ROOT / "story" / "main.json"
OUT_DIR = ROOT / "assets" / "voice"
MANIFEST = Path(__file__).resolve().parent / "voice_manifest.json"

API_URL = "http://127.0.0.1:8000"
API_NAME = "/run_instruct"

# 角色音色与风格指令
SPEAKERS = {
    "a_wan": {"spk": "Vivian", "instruct": "年轻柔和的女声，略带怯意"},
    "fubo": {"spk": "Uncle Fu", "instruct": "苍老沙哑的男声，年迈仆人的口吻"},
}
NARRATOR = {"spk": "Ryan", "instruct": "低沉平稳的男声旁白，略带悬疑感"}

MAX_RETRY = 2


def collect_lines():
    data = json.loads(STORY.read_text(encoding="utf-8"))
    lines = []
    for scene_id, scene in data["scenes"].items():
        # 剔除已注入的 voice 指令再编号，保证与 inject_voice.py 的索引一致
        cmds = [c for c in scene.get("commands", []) if c.get("cmd") != "voice"]
        for idx, cmd in enumerate(cmds):
            if cmd.get("cmd") not in ("say", "narrate"):
                continue
            text = cmd.get("text", "").strip()
            if not text:
                continue
            if cmd["cmd"] == "say":
                spk = SPEAKERS.get(cmd.get("character"), NARRATOR)
            else:
                spk = NARRATOR
            lines.append({
                "id": f"{scene_id}_{idx:03d}",
                "scene": scene_id,
                "index": idx,
                "cmd": cmd["cmd"],
                "character": cmd.get("character", ""),
                "text": text,
                "speaker": spk["spk"],
                "instruct": spk["instruct"],
            })
    return lines


def main():
    lines = collect_lines()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps(lines, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"共 {len(lines)} 条台词，输出目录: {OUT_DIR}")

    client = Client(API_URL)
    done = failed = skipped = 0
    failures = []
    t0 = time.time()

    for i, line in enumerate(lines, 1):
        out = OUT_DIR / f"{line['id']}.wav"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            continue
        ok = False
        for attempt in range(1, MAX_RETRY + 2):
            try:
                audio, status = client.predict(
                    text=line["text"],
                    lang_disp="Chinese",
                    spk_disp=line["speaker"],
                    instruct=line["instruct"],
                    api_name=API_NAME,
                )
                shutil.copyfile(audio, out)
                ok = True
                break
            except Exception as e:  # noqa: BLE001 - 记录后继续下一条
                print(f"[{i}/{len(lines)}] {line['id']} 第 {attempt} 次失败: {e}", flush=True)
                time.sleep(2)
        if ok:
            done += 1
        else:
            failed += 1
            failures.append(line["id"])
        print(f"[{i}/{len(lines)}] {line['id']} {'OK' if ok else 'FAIL'} "
              f"(完成 {done} / 跳过 {skipped} / 失败 {failed}) "
              f"用时 {time.time() - t0:.0f}s", flush=True)

    print(f"结束：完成 {done}，跳过 {skipped}，失败 {failed}")
    if failures:
        print("失败列表:", ", ".join(failures))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
