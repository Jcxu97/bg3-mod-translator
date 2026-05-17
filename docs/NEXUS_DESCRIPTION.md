**Drag a mod in. Get a CHS pak out. Upgrade later without breaking saves.**

BG3 Mod Translator is a Windows tool that takes any Baldur's Gate 3 mod (`.pak`, BG3MM `.zip`, or unpacked folder) and produces a standalone Simplified Chinese localization pak — ready to drop into BG3 Mod Manager. When the original mod releases a new version, feed the old CHS pak back in and the tool reuses your old translations, keeps the same UUID, and only retranslates the diff. Players see a normal mod update, not a broken save.

[size=4][b]Why this exists[/b][/size]

Most BG3 mod authors ship English only. Volunteer translators come and go — a mod gets a great Chinese pack at v2, then the translator disappears at v3 and the community is stuck. This tool is built so any user can produce a usable CHS pack in five minutes, and any maintainer can upgrade an abandoned translation without retranslating thousands of strings from scratch.

[size=4][b]Features[/b][/size]

[list]
[*][b]Drag-and-drop input[/b] — accepts .pak, BG3MM .zip, or an unpacked folder
[*][b]Upgrade mode[/b] — supply the previous CHS pak; the tool keeps the original UUID/Folder, reuses translations for unchanged English, only sends new/changed strings to the AI. Existing player saves keep working.
[*][b]Built-in official glossary[/b] — 232,000 sentence-level lookups + 8,000 term constraints extracted from the game's own Chinese.pak, embedded into the exe (single 86 MB file, no external data files needed)
[*][b]Multiple translation backends[/b] — Databricks Claude, DeepSeek, any OpenAI-compatible endpoint, local Ollama, plus a free Google fallback
[*][b]Placeholder validation[/b] — checks every output for `[1]`, `<LSTag/>`, `&entity;`, `{0}` markers; falls back to source if anything is malformed
[*][b]Term verification + retry[/b] — after each translation, verifies that vanilla glossary terms used the official Chinese rendering; on failure, retries with a stricter prompt listing the missing terms
[*][b]Version safety[/b] — guarantees Chinese loca version >= English so the game never silently falls back to English
[*][b]One-step BG3MM packaging[/b] — outputs both `<Mod>_CHS.pak` and `<Mod>_CHS.zip` with `info.json` + MD5, ready to upload to Nexus
[*][b]Local SQLite cache[/b] — same English line, same translation, no second API call across runs
[/list]

[size=4][b]How to use[/b][/size]

[b]New translation:[/b]

[list=1]
[*]Download `BG3LocTool.App.exe` from GitHub Releases. No installer.
[*]Drag the mod (.pak / .zip / folder) into the input box.
[*]Pick a translation backend. Databricks/DeepSeek give the best Chinese; Google Free works without an API key.
[*]Fill in author + version, choose output folder.
[*]Click Generate. Done.
[/list]

[b]Upgrade existing CHS pack:[/b]

[list=1]
[*]Switch the top toggle to Upgrade mode.
[*]Drop the new English mod into box 1, the old CHS pak into box 2.
[*]Generate. UUID is preserved, unchanged lines reuse old translations.
[/list]

You can also edit translations directly in the right-hand grid before exporting — useful for proofreading AI output.

[size=4][b]Requirements[/b][/size]

[list]
[*]Windows 10 / 11
[*].NET 8 runtime is bundled in the single-file exe — nothing to install
[*]An API key for whichever paid backend you use (or run Ollama locally, or use Google Free for zero setup)
[*]Tested against BG3 Patch 8 + Hotfix 8 (LSLib v1.20.4, all 9 vanilla module UUIDs verified)
[/list]

[size=4][b]Translation backends compared[/b][/size]

[b]Databricks Claude / DeepSeek / OpenAI[/b] — best Chinese quality, full glossary-constraint prompt with verify-and-retry loop. Recommended for any mod you actually plan to play.

[b]Ollama (local)[/b] — fully offline, no API cost. Quality depends on the model; `qwen2.5:14b` or larger gives reasonable results.

[b]Google Free[/b] — zero setup, no key. Works through Google's public translate endpoint, but it has no system-prompt concept, so the term-constraint pipeline is bypassed. Use for quick tests, not for final shipping packs.

[size=4][b]FAQ[/b][/size]

[b]Q: Will this break my saves?[/b]
A: In Upgrade mode, no — the UUID/Folder is preserved, so BG3 and BG3MM treat it as the same mod. From-scratch mode creates a new CHS pak, which the game registers as a new mod (translations apply via global contentuid override; no save corruption, just a new entry in your mod list).

[b]Q: Where does the 232K-sentence glossary come from?[/b]
A: It's extracted from your local legitimate game install at `Data/Localization/Chinese/Chinese.pak` and used purely for term alignment. The original .pak is not redistributed — only derived lookup tables are embedded in the exe.

[b]Q: Does it work for languages other than Chinese?[/b]
A: Currently no. The whole pipeline is tuned around the official Simplified Chinese glossary. Other languages will translate but won't have the term-alignment guarantee. Generalization to other locales is on the roadmap.

[b]Q: Will my CHS pack break when the original mod updates?[/b]
A: No. The CHS pak is an independent mod that overrides text via global contentuid lookup, so the original mod can update freely. When you want the new English content translated, run Upgrade mode again.

[b]Q: Patch 9 just dropped — does this still work?[/b]
A: Probably, but the embedded glossary needs to be rebuilt from the new Chinese.pak and LSLib may need an update. A new release will follow each major patch.

[b]Q: Should I install Mod Fixer alongside this?[/b]
A: No. Mod Fixer became harmful starting with Patch 8 and should be uninstalled. This is community consensus, unrelated to this tool.

[size=4][b]Source code[/b][/size]

GitHub: https://github.com/Jcxu97/bg3-mod-translator (MIT licensed)

Bug reports and PRs welcome via GitHub Issues.

[size=4][b]Credits[/b][/size]

[list]
[*][b]Norbyte[/b] — LSLib, without which none of the .pak/.loca/.lsx round-tripping would be possible
[*][b]Larian Studios[/b] — the official Simplified Chinese localization that anchors every term in the glossary
[*][b]The BG3 modding community[/b] — corpus analysis of 462 local mods informed every default and fallback in this tool
[/list]
