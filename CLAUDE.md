# bg3-mod-translator (BG3LocTool)

> BG3 子项目之一。**全局约定见 `@D:\Github\bg3-workspace\CLAUDE.md`**（启动时自动加载）

## 本项目角色

LLM 自动翻译 BG3 mod `.pak`：解包 → 抽 XML → 调 LLM 翻译 → 写回 → 编译 `.loca` → 重新打包。

## 启动检查清单

1. 读 `D:\Github\bg3-workspace\STATUS.md`——本项目当前 blocker：**ABoL_CHS.pak 是空壳**（Chinese XML 等于 English XML，没有 .loca）
2. 读本目录 `HANDOFF.md` 同步本项目上下文
3. 翻译参考 `D:\Github\bg3-workspace\shared\glossary\` 和 `shared\bg3-official-loca\`，**对齐 BG3 简中官方译名**（Lathander=洛山达，不是拉山德）

## 内置工具

- `lib\lslib\Divine.exe` — LSPak 解包/打包 + .loca ↔ .xml 互转

## 完成里程碑后

1. 翻译产物落到 `D:\Github\bg3-workspace\shared\translations\`
2. 更新 `D:\Github\bg3-workspace\STATUS.md` 的 `bg3-mod-translator` 段（覆盖式，一句话）
