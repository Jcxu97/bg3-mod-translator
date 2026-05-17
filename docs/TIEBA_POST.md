# 【工具分享】BG3 Mod 一键中文化神器,升级 mod 还能保留旧译(无感升级)

楼主自己玩博德 3 装了一堆 mod,但很多冷门 mod 根本没人做汉化,或者之前的汉化作者不更新了——新版英文,旧版中文,装上半中半英特别难受。

折腾了两周写了个工具,**把 mod 拖进去点一下就出独立汉化包**,而且原 mod 升级时塞一份旧汉化进去,工具会沿用 UUID 和已经翻好的句子,只重译新增的部分,玩家更新存档完全无感。

仓库:https://github.com/Jcxu97/bg3-mod-translator

发布页直接下 exe 双击就能用,86 MB 单文件免安装,Win10/11 都行。

---

## 它能干嘛

- 拖入 `.pak` / Nexus 下的 `.zip` / 解包后的文件夹,三选一
- 出一个独立的 `<原mod名>_CHS.pak`,直接丢 BG3MM 用
- 同时还出 BG3MM 标准 zip(含 info.json + MD5),想发 Nexus 直接传
- 升级模式:旧 CHS + 新英文 → 沿用旧 UUID 和已翻句子,只补新增部分
- 翻译后端可选 Databricks Claude / DeepSeek / OpenAI / 本地 Ollama,懒得办 key 就用谷歌免费翻译兜底
- 占位符 `[1]` `<LSTag/>` 这些自动校验,翻坏了自动回退原文,不会出乱码
- 中文 loca version 自动 >= 英文,不会出现游戏抽风显示英文的情况
- 本地 SQLite 缓存,改名重打包不浪费 token

---

## 截图占位

<!-- TODO 截图待补 -->
![主界面](screenshots/main.png)
![中英对照编辑](screenshots/editor.png)
![升级模式](screenshots/upgrade.png)

---

## 怎么用(傻瓜版)

1. 去 GitHub Releases 下 `BG3LocTool.App.exe`,双击
2. 把要汉化的 mod 拖进左边第 1 个框
3. 第 3 框选后端——**推荐 DeepSeek**(便宜效果好,¥1/百万 tokens 那种),没 key 选 Google Free 也能凑合
4. 第 4 框填作者名 + 版本号(就填 `1.0.0.1` 也行)
5. 第 5 框选输出目录,点 **⚡ 一键生成**

跑完输出目录里有一个 `_CHS.pak` 和一个 `_CHS.zip`,zip 拖进 BG3MM 就完事。

---

## 进阶玩法:升级老汉化包

碰到那种"原汉化作者跑路、新版没人接"的 mod:

1. 工具顶部切到 **🔄 版本更新** 模式
2. 第 1 框拖**新版英文 mod**,第 2 框拖**旧 CHS 包**
3. 一键生成

工具会沿用旧 CHS 的 UUID/Folder,英文没动的条目直接复用旧译,只把新增/改动的几十条送 AI 翻。玩家更新时存档完全无感(因为 BG3MM 看 UUID 当成同一个 mod 升级)。

楼主实测某 643 条句子的 Encounter mod,新版加了 70 句,工具只跑这 70 句,几秒钟就出包。

---

## 原理简单说说

不是单纯把英文丢 AI 那种水货实现:

- 楼主从游戏自带的 `Chinese.pak` 解出 **23 万条整句 + 8000 个术语**(像 `Tadpole=寄生蝌蚪`、`Lathander=洛山达` 这种官方译法)全打进了 exe
- 翻译每句之前,先在这 23 万条整句库里反查——命中就直接用官方译文,根本不动 AI
- 没命中的句子,自动从 8000 术语里挑出涉及的几个,塞进 AI 的 system prompt 强约束
- 翻完再校验一遍占位符 + 术语对不对,翻坏了带 missing 列表回炉重翻
- 跨任务有 SQLite 缓存,你给同个 mod 跑十次也只翻一次

所以名词不会出现"拉山德/洛山达/兰瑟"那种各个 mod 各翻各的乱象,vanilla 怎么叫,mod 里就怎么叫。

---

## 已知坑

- 目前测过 Patch 8 + HF8,Patch 9 出了得等楼主重打包字典
- **千万别装 Mod Fixer**,Patch 8 之后那玩意儿反而是坏的,装了 mod 加载会出问题(这个跟本工具无关,社区共识)
- 升级模式当前 `Group` UUID 还是新生成的,玩家可能要手调一下加载顺序,v2 修
- Google Free 后端不走术语强约束(谷歌的 HTTP API 没有 system prompt 概念),想要质量请用 AI 后端

---

## 反馈

bug / 建议 / 想加的功能直接在 GitHub Issues 说:
https://github.com/Jcxu97/bg3-mod-translator/issues

楼主自己天天用,有问题会修。MIT 协议,代码全开,二次开发随意。
