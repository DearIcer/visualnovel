#!/usr/bin/env bash
# 按清单批量调用 apimart 生图脚本，输出重命名为清单指定文件名。
# 用法: bash tools/gen_assets.sh tools/manifest_bg.txt assets_raw
set -uo pipefail

MANIFEST="$1"
OUTDIR="$2"
GEN="$HOME/.kimi-code/skills/apimart-image/scripts/gen_image.sh"
MODEL="gpt-image-2.5-ext"
RES="2k"

mkdir -p "$OUTDIR"

while IFS='|' read -r name size prompt ref; do
  # 跳过空行与注释
  case "$name" in ''|\#*) continue ;; esac
  name="$(printf '%s' "$name" | tr -d '[:space:]')"
  size="$(printf '%s' "$size" | tr -d '[:space:]')"
  target="$OUTDIR/$name.png"

  # 参考图占位符替换
  refpath=""
  if [ -n "${ref:-}" ]; then
    case "$ref" in
      REF_ANZHI) refpath="$OUTDIR/anzhi_normal.png" ;;
      *) refpath="$ref" ;;
    esac
  fi

  if [ -f "$target" ]; then
    echo "[跳过] $target 已存在"
    continue
  fi
  # 需要参考图但参考图还没生成时，跳过（第二轮再补）
  if [ -n "$refpath" ] && [ ! -f "$refpath" ]; then
    echo "[等待] $name 依赖参考图 $refpath，本轮跳过"
    continue
  fi

  echo "=== 生成 $name ($size) ==="
  before="$(mktemp)"
  ls "$OUTDIR"/*.png 2>/dev/null > "$before" || true

  args=(--model "$MODEL" --n 1 "$prompt" "$size" "$RES" "$OUTDIR")
  if [ -n "$refpath" ]; then args+=("$refpath"); fi

  ok=0
  for attempt in 1 2; do
    if bash "$GEN" "${args[@]}" > /tmp/gen_last.log 2>&1; then
      ok=1; break
    fi
    echo "  [重试 $attempt] $name 失败，日志: $(tail -2 /tmp/gen_last.log)"
    sleep 5
  done

  # 找出生成的新文件并重命名（Windows 下可能有文件锁，重试若干次，mv 失败则退回 cp+rm）
  newest="$(ls -t "$OUTDIR"/*.png 2>/dev/null | head -1 || true)"
  if [ "$ok" = "1" ] && [ -n "$newest" ] && ! grep -qF "$newest" "$before"; then
    renamed=0
    for i in 1 2 3 4 5; do
      if mv "$newest" "$target" 2>/dev/null || { cp "$newest" "$target" 2>/dev/null && rm -f "$newest"; }; then
        renamed=1; break
      fi
      sleep 2
    done
    if [ "$renamed" = "1" ]; then
      echo "[完成] $target"
    else
      echo "[失败] $name 重命名失败，原文件: $newest"
    fi
  else
    echo "[失败] $name"
    cat /tmp/gen_last.log | tail -5
  fi
done < "$MANIFEST"

echo "=== 全部任务结束 ==="
