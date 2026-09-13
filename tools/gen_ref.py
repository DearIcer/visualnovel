# -*- coding: utf-8 -*-
# 带参考图的生图工具（绕过 gen_image.sh 在 Windows 上 base64 命令行过长的问题）。
# 用法: python tools/gen_ref.py <输出路径> <尺寸> <分辨率> <提示词> [参考图1 参考图2 ...]
import base64
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
import uuid

API_BASE = "https://api.apimart.ai/v1"


def get_key():
    key = os.environ.get("APIMART_API_KEY", "").strip()
    if not key:
        with open(os.path.expanduser("~/.kimi-code/skills/apimart-image/api_key"), encoding="utf-8") as f:
            key = f.read().strip()
    return key


def get_proxy():
    """读取 Windows 系统代理（仅当启用时）。"""
    try:
        out = subprocess.run(
            ["reg", "query", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "/v", "ProxyEnable"],
            capture_output=True, text=True).stdout
        if "0x1" not in out:
            return None
        out = subprocess.run(
            ["reg", "query", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "/v", "ProxyServer"],
            capture_output=True, text=True).stdout
        m = re.search(r"ProxyServer\s+REG_SZ\s+(\S+)", out)
        return "http://" + m.group(1) if m else None
    except Exception:
        return None


def make_opener():
    proxy = os.environ.get("HTTPS_PROXY") or get_proxy()
    if proxy:
        return urllib.request.build_opener(urllib.request.ProxyHandler({"http": proxy, "https": proxy}))
    return urllib.request.build_opener()


def main():
    out_path, size, res, prompt = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
    refs = sys.argv[5:]
    key = get_key()
    opener = make_opener()

    body = {
        "model": "gpt-image-2.5-ext",
        "version": "flare",
        "prompt": prompt,
        "n": 1,
        "size": size,
        "resolution": res,
    }
    if refs:
        urls = []
        for r in refs:
            ext = os.path.splitext(r)[1].lower().lstrip(".")
            mime = {"jpg": "image/jpeg", "jpeg": "image/jpeg", "webp": "image/webp"}.get(ext, "image/png")
            with open(r, "rb") as f:
                urls.append(f"data:{mime};base64," + base64.b64encode(f.read()).decode())
        body["image_urls"] = urls

    req = urllib.request.Request(
        API_BASE + "/images/generations",
        data=json.dumps(body).encode(),
        headers={
            "Authorization": "Bearer " + key,
            "Content-Type": "application/json",
            "X-APIMart-Response-Version": "2026-07-27",
            "Idempotency-Key": str(uuid.uuid4()),
        },
        method="POST",
    )
    with opener.open(req, timeout=120) as resp:
        data = json.loads(resp.read())
    task_id = data["data"]["id"] if isinstance(data.get("data"), dict) else data["data"][0]["task_id"]
    print("任务已提交:", task_id, flush=True)

    img_urls = None
    for i in range(120):
        time.sleep(5)
        req = urllib.request.Request(API_BASE + "/tasks/" + task_id, headers={"Authorization": "Bearer " + key})
        try:
            with opener.open(req, timeout=60) as resp:
                st = json.loads(resp.read())
        except Exception as e:
            print(f"  轮询异常，重试 ({i}/120): {e}", flush=True)
            continue
        d = st.get("data", {})
        status = d.get("status")
        if status == "completed":
            result = d.get("result", {})
            images = result.get("images", [])
            img_urls = []
            for img in images:
                u = img.get("url")
                img_urls.extend(u if isinstance(u, list) else [u])
            break
        if status == "failed":
            print("任务失败:", json.dumps(st, ensure_ascii=False)[:500])
            sys.exit(1)
    if not img_urls:
        print("等待超时")
        sys.exit(1)

    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    req = urllib.request.Request(img_urls[0])
    with opener.open(req, timeout=300) as resp, open(out_path, "wb") as f:
        f.write(resp.read())
    print("已保存:", out_path)


if __name__ == "__main__":
    main()
