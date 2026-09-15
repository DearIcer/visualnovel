#!/usr/bin/env bash
# 将 godot_wry Android 支持集成到 Godot 自定义 Gradle 构建模板（android/build）中。
#
# 前置：已在 Godot 编辑器执行「项目 → 安装 Android 构建模板」（生成 android/build）。
# 用法（项目根目录的 Git Bash 中）：
#   ./tools/godot-wry-android/integrate.sh
#
# 重复执行是安全的（幂等）。
set -euo pipefail

PACKAGE="com.example.interactivetext"
ACTIVITY="${PACKAGE}.WryActivity"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD_DIR="$ROOT/android/build"
[[ -f "$BUILD_DIR/build.gradle" ]] || { echo "错误：未找到 android/build，请先在编辑器中安装 Android 构建模板"; exit 1; }

# 1. 复制 Kotlin 粘合层
mkdir -p "$BUILD_DIR/src/main/java"
cp -r "$ROOT/tools/godot-wry-android/android/java/com" "$BUILD_DIR/src/main/java/"
echo "已复制 Kotlin 粘合层到 android/build/src/main/java/"

# 2. 改写主 manifest 的主 Activity（幂等）
MANIFEST="$BUILD_DIR/src/main/AndroidManifest.xml"
if grep -q 'android:name=".GodotApp"' "$MANIFEST"; then
    sed -i "s|android:name=\".GodotApp\"|android:name=\"$ACTIVITY\"|;
            s|android:targetActivity=\".GodotApp\"|android:targetActivity=\"$ACTIVITY\"|" "$MANIFEST"
    echo "已将 src/main/AndroidManifest.xml 主 Activity 改为 $ACTIVITY"
fi

# 3. build.gradle：追加 androidx.webkit 依赖与 manifest 改写任务（幂等）
GRADLE="$BUILD_DIR/build.gradle"
if ! grep -q "androidx.webkit" "$GRADLE"; then
    sed -i 's|implementation "androidx.documentfile:documentfile:$versions.documentfileVersion"|implementation "androidx.documentfile:documentfile:$versions.documentfileVersion"\n    implementation "androidx.webkit:webkit:1.12.1"|' "$GRADLE"
    echo "已添加 androidx.webkit 依赖"
fi
if ! grep -q "godot_wry Android 支持" "$GRADLE"; then
    cat >> "$GRADLE" << EOF

// ===== godot_wry Android 支持 =====
// Godot 导出器会在每次导出时重新生成 src/<debug|release>/AndroidManifest.xml，
// 其中主 Activity 硬编码为 .GodotApp。本任务在 manifest 合并前将其改写为
// $ACTIVITY（继承 GodotApp，叠加 wry 的 WebView）。
tasks.configureEach { task ->
    def m = (task.name =~ /process\w*(Debug|Release)MainManifest/)
    if (m.find()) {
        task.doFirst {
            def manifestFile = file("src/\${m.group(1).toLowerCase()}/AndroidManifest.xml")
            if (manifestFile.exists()) {
                def text = manifestFile.getText('UTF-8')
                text = text.replace('android:name=".GodotApp"',
                                    'android:name="$ACTIVITY"')
                           .replace('android:targetActivity=".GodotApp"',
                                    'android:targetActivity="$ACTIVITY"')
                manifestFile.setText(text, 'UTF-8')
                println("[godot_wry] 已改写 \${manifestFile} 的主 Activity 为 WryActivity")
            }
        }
    }
}
// ===== godot_wry Android 支持结束 =====
EOF
    echo "已在 build.gradle 追加 manifest 改写任务"
fi

# 4. 阻止编辑器扫描 android/ 目录（避免生成 .import 文件导致资源合并失败）
touch "$ROOT/android/.gdignore" "$BUILD_DIR/.gdignore"

# 5. 构建模板版本标记（编辑器安装模板时会自动生成；手动安装时补写）
if [[ ! -f "$ROOT/android/.build_version" ]]; then
    printf '4.7.2.rc\n' > "$ROOT/android/.build_version"
    echo "已写入 android/.build_version（如引擎版本变化需同步更新）"
fi

echo "集成完成。"
