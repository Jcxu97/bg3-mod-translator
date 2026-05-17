# BG3 Mod Translator v0.1.0 — First Release

一键把 BG3 mod 自动汉化为独立 CHS 包。WPF GUI,单文件 exe (~82 MB),无需安装。

## ✨ 核心功能

- **拖入即用**:`.pak` / `.zip` (BG3MM 格式) / 已解包文件夹 三种输入
- **升级模式**:沿用旧 CHS 包的 UUID/Folder,玩家无感升级,旧译复用,只重译新增
- **官方字典内置**:从 BG3 简中 .pak 提取的 178,984 整句反查 + 8,000 术语强约束 prompt 注入,全打进 exe
- **多翻译后端**:OpenAI 兼容(DeepSeek / 智谱 / 百炼 / Databricks / 自定义)/ Ollama / Google Free
- **占位符校验**:`[1]` `<LSTag/>` `&entity;` 自动检查不达标回退原文
- **版本防降**:Chinese loca version ≥ English,杜绝游戏取错语言
- **BG3MM 一步出包**:.pak + .zip + info.json + MD5
- **本地缓存**:SQLite 跨任务复用,改名/重打不浪费 token

## 🛡️ Patch 8 验证

- LSLib v1.20.4 集成
- 9 个 vanilla 模块 UUID + Version64 实测对齐
- 真实升级 smoke 跑通(643 复用 / 70 重译)
- corpus:基于 462 个本地 mod 反推规律

## 📦 下载

`bg3-mod-translator-v0.1.0-win-x64.zip` — 解压双击 `BG3LocTool.App.exe` 即可。Windows 10/11 x64,自带 .NET 8 runtime。

## 🚀 用法

1. 双击 `BG3LocTool.App.exe`
2. 拖 mod (.pak/.zip/文件夹) 进左边大框
3. 选翻译后端,填 URL+Key+Model(或选 Google Free 零配置)
4. 填作者名 + 版本号
5. 点 ⚡ 一键生成 → 输出 `<Mod>_CHS.pak` + `<Mod>_CHS.zip`

升级旧 CHS:勾选「版本更新」,把旧 CHS 包拖到第二个框,工具自动沿用 UUID 复用旧译。

## ⚠️ 已知限制(v0.2 修)

- BG3MM info.json 的 `Group` UUID 升级模式当前会新生成,玩家手调加载顺序;v0.2 沿用旧值
- 全局 typo 修正待加(BG3 spec Step 6)
- 裸 Localization 型 pak 待支持

## 🙏 Credits

- LSLib (Norbyte) — pak/loca 编解码
- BG3 vanilla 简中字典 — 从游戏 Chinese.pak 提取
- 翻译后端测试:DeepSeek、Databricks Claude
