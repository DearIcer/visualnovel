#!/usr/bin/env bash
# 交叉编译 Android 版 libgodot_wry.so（arm64 + arm32），并拷贝到 addons/godot_wry/bin/。
#
# 用法（Git Bash）：
#   ./build-android.sh [--debug]
#
# 环境要求：
#   - Rust 工具链（rustup）及 target：aarch64-linux-android、armv7-linux-androideabi
#   - Android NDK：通过环境变量 ANDROID_NDK_HOME 指定，
#     否则自动探测 %LOCALAPPDATA%/Android/Sdk/ndk 下的最新版本。
set -euo pipefail

cd "$(dirname "$0")"
PROFILE=release
[[ "${1:-}" == "--debug" ]] && PROFILE=debug

# ---------- 定位 NDK ----------
if [[ -z "${ANDROID_NDK_HOME:-}" ]]; then
    NDK_ROOT="${LOCALAPPDATA}/Android/Sdk/ndk"
    [[ -d "$NDK_ROOT" ]] || { echo "错误：未找到 Android NDK，请设置 ANDROID_NDK_HOME"; exit 1; }
    # 取版本号最大的已安装 NDK
    ANDROID_NDK_HOME=$(ls -d "$NDK_ROOT"/*/ | sort -V | tail -1)
    ANDROID_NDK_HOME="${ANDROID_NDK_HOME%/}"
fi
echo "使用 NDK: $ANDROID_NDK_HOME"

NDK_BIN=$(cygpath -w "$ANDROID_NDK_HOME" | sed 's|\\|/|g')/toolchains/llvm/prebuilt/windows-x86_64/bin
API=24

# ---------- 生成 cargo 配置（NDK 链接器） ----------
mkdir -p .cargo
cat > .cargo/config.toml <<EOF
[target.aarch64-linux-android]
linker = "$NDK_BIN/aarch64-linux-android$API-clang.cmd"

[target.armv7-linux-androideabi]
linker = "$NDK_BIN/armv7a-linux-androideabi$API-clang.cmd"
EOF

# ---------- github -> gitcode 镜像（仅本次进程生效） ----------
export GIT_CONFIG_COUNT=1
export GIT_CONFIG_KEY_0="url.https://gitcode.com/gh_mirrors/gd/gdext.git.insteadOf"
export GIT_CONFIG_VALUE_0="https://github.com/godot-rust/gdext"
export CARGO_NET_GIT_FETCH_WITH_CLI=true

export PATH="$USERPROFILE/.cargo/bin:$PATH"

# ---------- 编译 ----------
for TARGET in aarch64-linux-android armv7-linux-androideabi; do
    echo "==> 构建 $TARGET ($PROFILE)"
    if [[ "$PROFILE" == "release" ]]; then
        cargo build --target "$TARGET" --release
    else
        cargo build --target "$TARGET"
    fi
done

# ---------- 拷贝到 Godot 插件目录 ----------
ADDONS_BIN="../../../addons/godot_wry/bin"
mkdir -p "$ADDONS_BIN/aarch64-linux-android" "$ADDONS_BIN/armv7-linux-androideabi"
cp "target/aarch64-linux-android/$PROFILE/libgodot_wry.so" "$ADDONS_BIN/aarch64-linux-android/"
cp "target/armv7-linux-androideabi/$PROFILE/libgodot_wry.so" "$ADDONS_BIN/armv7-linux-androideabi/"
echo "完成：已拷贝 libgodot_wry.so 到 addons/godot_wry/bin/{aarch64-linux-android,armv7-linux-androideabi}/"
