# -*- coding: utf-8 -*-
"""把 voice 指令注入 story/main.json。

在每条已有对应音频（assets/voice/{scene}_{index:03d}.wav）的
say / narrate 指令前插入：
    { "cmd": "voice", "path": "res://assets/voice/{scene}_{index:03d}.wav" }

采用文本级插入以保留原文件格式；注入前自动备份为 main.json.bak。
重复运行会先清除旧 voice 指令再重新注入，幂等安全。
"""
import json
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STORY = ROOT / "story" / "main.json"
BACKUP = STORY.with_suffix(".json.bak")
VOICE_DIR = ROOT / "assets" / "voice"

CMD_LINE = re.compile(r'^(?P<indent>\s*)\{ "cmd": "(?P<cmd>[^"]+)"')
CMD_ANY = re.compile(r'^\s*"cmd": "[^"]+"')
SCENE_HEADER = re.compile(r'^\t"[^"]+": \{\s*$')
VOICE_LINE = re.compile(r'^\s*\{ "cmd": "voice", "path": "res://assets/voice/[^"]+\.wav" \},?\s*$')


def main():
    text = STORY.read_text(encoding="utf-8")
    data = json.loads(text)

    # 解析层：按场景顺序记录需要注入的 (scene, index) -> voice 文件名。
    # 先剔除已注入的 voice 指令再编号，保证重复运行时索引一致。
    wanted = {}  # (scene_id, cmd_index) -> voice_id
    for scene_id, scene in data["scenes"].items():
        cmds = [c for c in scene.get("commands", []) if c.get("cmd") != "voice"]
        for idx, cmd in enumerate(cmds):
            if cmd.get("cmd") in ("say", "narrate"):
                voice_id = f"{scene_id}_{idx:03d}"
                if (VOICE_DIR / f"{voice_id}.wav").exists():
                    wanted[(scene_id, idx)] = voice_id

    if not wanted:
        print("assets/voice 中没有可用音频，未做任何修改。")
        sys.exit(1)

    lines = text.splitlines(keepends=True)

    # 第一步：移除已存在的旧 voice 指令行（仅脚本生成的那种单行格式）
    lines = [l for l in lines if not VOICE_LINE.match(l.rstrip("\n"))]

    # 第二步：重新定位每个场景的指令序列并插入 voice 行
    out = []
    in_scenes = False
    current_scene = None
    cmd_idx = -1
    inserted = 0
    for line in lines:
        stripped = line.rstrip("\n")
        if re.match(r'^  "scenes": \{\s*$', stripped):
            in_scenes = True
        elif in_scenes and SCENE_HEADER.match(stripped):
            current_scene = stripped.strip().split('"')[1]
            cmd_idx = -1
        elif in_scenes and current_scene is not None:
            m = CMD_LINE.match(stripped)
            if m:
                cmd_idx += 1
                key = (current_scene, cmd_idx)
                if key in wanted:
                    voice_id = wanted[key]
                    indent = m.group("indent")
                    out.append(f'{indent}{{ "cmd": "voice", "path": "res://assets/voice/{voice_id}.wav" }},\n')
                    inserted += 1
            elif CMD_ANY.match(stripped):
                # 多行指令（如 choice）只计数不插入
                cmd_idx += 1
        out.append(line)

    new_text = "".join(out)
    # 校验：注入后 JSON 必须仍然合法
    new_data = json.loads(new_text)
    voice_count = sum(
        1 for s in new_data["scenes"].values()
        for c in s.get("commands", []) if c.get("cmd") == "voice"
    )
    assert voice_count == inserted, f"注入数量不一致: 文本插入 {inserted}, JSON 内 {voice_count}"

    if not BACKUP.exists():
        shutil.copyfile(STORY, BACKUP)
        print(f"已备份原文件: {BACKUP}")
    STORY.write_text(new_text, encoding="utf-8")
    print(f"已注入 {inserted} 条 voice 指令（共需 {len(wanted)} 条，缺失音频的已跳过）。")


if __name__ == "__main__":
    main()
