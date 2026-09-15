# godot-wry-android

为 `addons/godot_wry`（WebView GDExtension）补齐 **Android 平台支持** 的构建工程与集成材料。

godot_wry 上游仅支持桌面（Windows/macOS/Linux）。本目录基于其 Rust 源码 fork 出 Android 端实现：
wry 的 Android 后端通过 JNI 调用系统 `android.webkit.WebView`，需要一套 Kotlin 粘合类
（`RustWebView` / `RustWebViewClient` / `RustWebChromeClient` / `Ipc` 等，取自 wry 0.50.4 官方模板），
以及一个继承 `GodotApp` 的 `WryActivity` 作为应用主 Activity。

## 重要：包名是编译期常量

wry 的 JNI 符号名包含应用包名。当前固定为 **`com.example.interactivetext`**，三处必须保持一致：

1. `rust/src/lib.rs` 中 `android_binding!(com_example, interactivetext)` 与
   `Java_com_example_interactivetext_WryActivity_nativeWrySetup`
2. `android/java/com/example/interactivetext/` 下所有 Kotlin 文件的 `package` 声明
3. 导出预设的 `package/unique_name`（`export_presets.cfg`）

如需更换包名，请同步修改以上三处后重新编译 Rust 库。

## 目录

```
rust/       # godot_wry Rust 源码 fork（新增 Android 支持），cargo 交叉编译
android/    # Kotlin 粘合层，供 Godot 自定义 Gradle 构建使用
```

## 一、编译 Rust 库（生成 libgodot_wry.so）

环境要求：Rust（rustup）、Android NDK（已随 Android SDK 安装）。

```bash
# 一次性：安装 Android 交叉编译目标
rustup target add aarch64-linux-android armv7-linux-androideabi

# 编译 arm64 + arm32 并自动拷贝到 addons/godot_wry/bin/
cd tools/godot-wry-android/rust
./build-android.sh            # release；调试版用 ./build-android.sh --debug
```

脚本说明：
- NDK 默认自动探测 `%LOCALAPPDATA%/Android/Sdk/ndk` 下最新版本，也可用 `ANDROID_NDK_HOME` 指定；
- 因网络原因，脚本通过进程内环境变量将 gdext 的 GitHub 依赖重定向到 gitcode 镜像，
  不影响全局 git 配置。

## 二、导出 Android（Godot 编辑器侧）

**关键前提：mono 版 Android 导出模板**。本项目是 C# 工程，而当前
`%APPDATA%/Godot/export_templates/4.7.2.rc/` 与引擎 `bin/` 下的 Android 模板均为
**非 mono 构建**（`libgodot_android.so` 未编入 mono 模块，C# 无法运行）。需要先用
`module_mono_enabled=yes` 重新构建 Android 模板：

```bash
# 在引擎源码目录（需 Android SDK/NDK 环境变量就绪）
scons platform=android target=template_debug module_mono_enabled=yes generate_apk=yes
scons platform=android target=template_release module_mono_enabled=yes generate_apk=yes
```

构建产物（`bin/android_debug.apk`、`bin/android_release.apk`、`bin/android_source.zip`）
复制到 `%APPDATA%/Godot/export_templates/4.7.2.rc/` 覆盖同名文件，然后**重新安装构建模板**
（项目 → 安装 Android 构建模板）并重跑下面的集成脚本。

其余前置：编辑器设置 → 导出 → Android 中配置好 JDK 17 与 Android SDK 路径。

1. 编辑器菜单：**项目 → 安装 Android 构建模板**（生成 `android/build/`）。
2. 运行一键集成脚本（复制 Kotlin 粘合层、改写主 Activity、追加 webkit 依赖与
   manifest 构建期改写任务）：
   ```bash
   ./tools/godot-wry-android/integrate.sh
   ```
3. 使用导出预设 **Android**（已启用 Gradle 构建、arm64-v8a + armeabi-v7a）导出 APK。

> 注意：Godot 导出器每次导出都会重新生成 `android/build/src/<debug|release>/AndroidManifest.xml`
> 并把主 Activity 硬编码为 `.GodotApp`，且其优先级高于 `src/main/AndroidManifest.xml`。
> 因此集成脚本在 `build.gradle` 中注入了一个构建期任务，在 manifest 合并前自动把生成的
> manifest 改写为 `WryActivity`——请勿删除 `build.gradle` 中标记为
> `godot_wry Android 支持` 的段落。

## 工作原理

- Godot 加载 GDExtension 后，`WebView` 节点类在 Android 上由 `libgodot_wry.so` 注册，API 与桌面一致。
- `WryActivity.onCreate` 在 UI 主线程调用 `nativeWrySetup`（Rust），
  完成 `wry::android_setup`：在 Main Looper 上注册消息管道，用于跨线程创建/操作 WebView。
- wry 创建 WebView 时调用 `activity.setContentView(webview)`，`WryActivity` 拦截该调用，
  将 WebView **叠加**到 Godot 渲染视图之上（而非替换），`transparent=true` 时舞台内容正常透出。
- JS ↔ C# 的 IPC（`window.ipc.postMessage` / `post_message`）与自定义 `res://` 协议在 Android 上同样可用。

## 已知限制

- `resize`/`set_bounds`、`open_devtools` 等在 Android 为空操作（wry 后端限制），
  WebView 恒为全屏叠加，与本项目 `full_window_size` 用法一致。
- 网页内文件选择器（`<input type="file">` 的相机捕获）如需使用，
  需在 Manifest 中额外注册 `{package}.fileprovider`（本项目 UI 未用到）。
- 触摸输入由 WebView 原生处理；`forward_input_events` 的键鼠转发是为桌面设计的，Android 保持 `false`。

## 排障记录（本游戏实际踩过的坑）

1. **C# 程序集名必须只用 ASCII 字符**。Godot 4.7 在 Android 上用 MonoVM 模拟 CoreCLR
   宿主 API（`coreclr_create_delegate`），其垫片会把非 ASCII 程序集名按 ANSI 解码，
   导致 `GodotPlugins.Game.Main` 委托创建失败（日志：`Failed to get GodotPlugins
   initialization function pointer` → `Failed to load hostfxr` → 进程 abort）。
   本项目因此将 `dotnet/project/assembly_name` 与 csproj/sln 都改为 `InteractiveText`
   （**命名空间不受影响，代码中仍可用中文**）。
2. **导出时 dotnet publish 需要 `<程序集名>.sln`，且 sln 必须包含
   `ExportDebug`/`ExportRelease` 解决方案配置**，否则 publish 被跳过/失败，
   APK 中不会有任何 .NET 程序集（启动时引擎初始化即 SIGSEGV）。
   编辑器重新打开项目时也会自动维护该 sln。
3. Android 导出要求开启 `rendering/textures/vram_compression/import_etc2_astc`
   （已在 project.godot 中配置）。
