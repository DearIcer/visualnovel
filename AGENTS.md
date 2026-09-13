# AGENTS.md

> 面向 AI 编码助手的项目说明。请在进行代码修改前通读本文件。

## 项目概览

**交互式文本** 是一个基于 **Godot 4.7（.NET / C# 版，Godot.NET.Sdk）** 开发的视觉小说（Visual Novel）引擎项目。目标框架为 **.NET 8**，渲染使用 **GL Compatibility**，Windows 下渲染设备驱动为 **d3d12**。

游戏剧情通过 **JSON 脚本**（`story/main.json`）驱动，引擎解析并逐条执行指令，实现对话、立绘、背景、BGM/SE/语音、选择支、变量、存档/读档、历史回顾、设置与鉴赏（CG/音乐/语音解锁）等完整功能。

## 目录结构

```
交互式文本.csproj      # C# 项目文件（Godot.NET.Sdk/4.7.0，net8.0，RootNamespace 为 "交互式文本"）
project.godot          # Godot 工程配置（主场景、Autoload、渲染设置）
scenes/                # 场景文件
  Main.tscn            # 主场景（入口）：VNCore + Stage（背景/角色）+ UI（WebView + 桥接节点）
webui/                 # Web UI 前端（纯 HTML/CSS/JS，无构建步骤）
  index.html           # UI 骨架：顶部按钮栏、对话框、选择支、全屏面板、右键菜单
  style.css            # 深色毛玻璃主题样式（CSS 变量 --accent 等可调配比色）
  ui.js                # 打字机状态机、面板渲染、输入捕获、IPC 收发
scripts/               # C# 源码（全部位于 交互式文本.* 命名空间下）
  Engine/              # 核心引擎
    VNCore.cs          # 主控制器：挂在主场景根节点，驱动剧情流程、输入、状态管理
    CommandRunner.cs   # 指令执行器：将 CommandData 映射为演出与状态变更
    ScriptParser.cs    # JSON 剧情解析器（SceneData / CommandData / CharacterData）
    GameState.cs       # 游戏状态：变量、已读标记、历史、舞台/音频快照、解锁数据
    SaveLoadManager.cs # 存档序列化（System.Text.Json，存于 user://saves/save_N.json）
  Data/                # 数据结构与序列化
    GameConfig.cs      # 全局设置（音量、自动/快进延迟、全屏等，存于 user://settings.json）
    SaveData.cs        # 存档数据结构
    VariantConverter.cs# Godot Variant 的 System.Text.Json 转换器
  Audio/               # 音频系统
    AudioManager.cs    # Autoload 单例，统一管理 BGM / SE / Voice 三条总线
    MusicChannel.cs    # BGM 通道（淡入淡出、交叉淡化）
    SoundPool.cs       # 音效池
    VoiceChannel.cs    # 语音通道
  UI/                  # 界面层（Web UI 桥接；实际渲染在 webui/ 前端）
    WebUI.cs           # 核心桥：C#↔JS JSON 消息收发、面板数据供给（历史/存读档/设置/鉴赏）
    UIManager.cs       # 面板管理桥：保留 Instance/IsAnyUIOpen/Show*/CloseAll，转发 WebUI
    DialogueBox.cs     # 对话框桥：保留 ShowDialogue/ShowChoices 等签名，转发 WebUI
story/main.json        # 剧情脚本（characters + scenes/commands）
assets/                # 资源
  backgrounds/         # 背景图（PNG）
  characters/<角色id>/ # 立绘，按 角色id/表情名.png 组织（如 shiori/smile.png）
  bgm/ se/ voice/ cg/  # 对应音频与 CG 资源（指令按文件名引用）
addons/godot_wry/      # Godot WRY WebView GDExtension（驱动整个 Web UI 层）
tools/story-editor/    # 剧情编辑器（纯前端 HTML/CSS/JS，Chrome/Edge 打开 index.html 即用；
                       # 通过 File System Access API 读写 story/*.json，不参与游戏运行与导出，
                       # 不被 Godot 工程引用；smoke-test.js 为 Node 冒烟测试：node smoke-test.js）
.godot/                # Godot 编辑器缓存（勿提交，勿手改）
```

## 构建与运行

- **构建**：`dotnet build`（等价于 Godot 编辑器中的 Build；需要 .NET 8 SDK 与 Godot .NET 版）。
- **运行**：用 Godot 4.7 .NET 版编辑器打开项目并运行（主场景 `scenes/Main.tscn`），或 `godot --path .`（需自行确保命令行 Godot 版本为 .NET 版）。
- **导出/部署**：项目未配置自定义导出流程，使用 Godot 编辑器标准的「项目 → 导出」流程。
- **测试**：项目目前**没有自动化测试框架或测试用例**。验证方式为构建通过 + 在编辑器中运行游戏手动验证剧情流程。

## 剧情脚本格式（story/*.json）

顶层结构：`characters`（角色定义：`displayName` / `color` / `defaultPosition`）与 `scenes`（场景 id → `commands` 数组）。每条指令为 `{ "cmd": "...", ...参数 }`，支持的指令（见 `CommandRunner.Execute`）：

| 指令 | 作用 | 主要参数 |
|---|---|---|
| `bg` | 切换背景 | `asset`（assets/backgrounds 下文件名）、`color`、`fade` |
| `bgm` | 背景音乐 | `action`(play/crossfade/stop)、`track`、`fade`、`loop` |
| `se` | 音效 | `sound`（assets/se 下 .wav 文件名）、`volume`、`pitch` |
| `voice` | 语音 | `path` |
| `show` / `hide` / `move` | 立绘显示/隐藏/移动 | `character`、`position`(left/center/right)、`emotion`、`animation`、`duration` |

> `show` 省略 `position` 时：已登场角色保持当前位置（仅切换表情），新登场角色落到 `defaultPosition`。切换场景/段落时旧角色不会自动退场，需要显式 `hide`，否则立绘会残留叠在新场景上。
| `say` / `narrate` | 角色对话 / 旁白 | `character`、`text` |
| `choice` | 选择支 | `options`：[{`text`, `target`}] |
| `jump` | 跳转场景 | `target` |
| `set` | 设置变量 | `name`、`value` |
| `if` | 条件判断（MVP：仅简单比较，无完整块语法） | `name`、`op`、`value` |
| `cg` / `music` / `vo_unlock` | 解锁鉴赏内容 | `id` |

资源路径约定：背景 `res://assets/backgrounds/{asset}.png`；立绘 `res://assets/characters/{character}/{emotion}.png`；音效 `res://assets/se/{sound}.wav`。新指令应在 `CommandRunner.Execute` 的 switch 中注册，并同步在 `tools/story-editor/editor.js` 的 `CMD_DEFS` 中登记，保证剧情编辑器能编辑与校验新指令。

## 架构要点

- **UI 为 WebView 渲染**：全部界面（对话框/选择支/历史/存读档/设置/鉴赏/右键菜单）由 `webui/` 前端的 HTML/CSS/JS 实现，经 godot_wry 的 `WebView` 节点（`transparent=true`、`forward_input_events=false`）透明叠加在原生 Stage（背景/立绘）之上。WebView 永远渲染在 Godot 内容之上，Godot 原生 UI 无法盖在其上。
- **C# ↔ JS 通信**：C# → JS 用 `WebView.Call("post_message", json)`，JS 侧 `document.addEventListener('message', e => JSON.parse(e.detail))`；JS → C# 用 `window.ipc.postMessage(json)`，C# 侧连接 `ipc_message` 信号（`WebUI.OnIpcMessage` 统一分发，消息均带 `type` 字段）。新增 UI 功能时在 `WebUI.cs` 的 switch 与 `webui/ui.js` 的 message switch 中成对注册。
- **打字机在 JS 端**：`webui/ui.js` 逐字渲染，打完发 `typing_complete` → `VNCore.OnDialogueComplete()`；打字中点击本地补全，完成后点击才发 `advance`。
- **输入由 Web 端捕获**：`forward_input_events=false`，点击/空格/滚轮/H/A/Ctrl/Esc/右键均由 `ui.js` 捕获并经 IPC 通知引擎；`VNCore._UnhandledInput` 的输入处理仅作兜底。面板打开时 JS 不再发送 `advance`，C# 侧 `UIManager.IsAnyUIOpen`（由 JS 的 `panel_opened/panel_closed` 维护）用于引擎兜底判断。
- **运行时入口**：`VNCore._Ready()` 注册输入动作 → 加载剧情 JSON → 从 `StartSceneId` 开始 `RunNextCommand()` 循环执行指令。非阻塞指令自动推进，`say`/`narrate`/`choice` 等阻塞指令等待输入（`IsWaitingForInput` / `IsTyping` / `IsTransitioning` 标志控制）。
- **输入动作**在代码中动态注册（不在 InputMap 中配置）：`vn_advance`（空格/回车/左键/滚轮下）、`vn_history`(H)、`vn_auto`(A)、`vn_skip`(Ctrl)、`vn_menu`(Esc)。`UIManager` 面板打开时会阻断剧情推进输入。
- **存档**：`GameState` 所有字段必须可序列化（`System.Text.Json`，`Variant` 通过 `VariantConverter` 处理）。新增状态字段时需同步考虑 `SaveData`、`VNCore.ApplySaveData` 与读档恢复逻辑（舞台/音频快照用于读档复现演出）。
- **音频**：`AudioManager` 为 Autoload 单例（`AudioManager.Instance`），运行时自动创建 BGM/SE/Voice 三条总线；设置界面的音量通过 `ApplyVolumes()` 应用（设置数据由 WebUI 在 `apply_settings` 消息中应用）。

## 代码风格约定

- 代码、注释与文档字符串统一使用**简体中文**（XML 文档注释 `/// <summary>` 标注每个公共类型）。
- C# 类与命名空间均使用中文根命名空间 `交互式文本.<子系统>`；文件与类同名。
- Godot 类型继承约定：节点类用 `partial class X : Node/Control`，纯逻辑类用 `partial class X : RefCounted`。
- 注意缩进不统一：部分文件用 **Tab**（如 `Audio/`、`UI/UIManager.cs`），部分文件用 **4 空格**（如 `Engine/`、`Data/`）。修改文件时请**跟随该文件现有缩进风格**。
- 每个 `.cs` 文件旁有 Godot 生成的 `.cs.uid` 文件，新建脚本后由编辑器自动生成，勿手动编辑。
- 路径/场景引用使用 `res://`（资源）与 `user://`（存档 `user://saves`、设置 `user://settings.json`）。
- 错误处理：资源缺失或异常用 `GD.PushError` / `GD.PushWarning`（带中文消息），不要抛出未处理异常。

## 注意事项

- **WebView2 Runtime 依赖**：Windows 上 godot_wry 使用 WebView2（Chromium），目标机器需安装 WebView2 Runtime（Win10/11 通常自带）。
- `addons/godot_wry` 的 `WebView` 是 GDExtension 类，无 C# 绑定，C# 中一律用 `Call()` / `Connect()` / 属性 `Set()` 操作；Web UI 文件必须放在 `res://` 下（插件自定义 res 协议，仅支持 res://）。
- `交互式文本.csproj.old` 为升级 Godot 版本（4.6.3 → 4.7.0）时自动生成的备份，不要将其当作主项目文件。
- `.godot/` 目录为编辑器缓存，已被 `.gitignore` 忽略，不要手动修改。
