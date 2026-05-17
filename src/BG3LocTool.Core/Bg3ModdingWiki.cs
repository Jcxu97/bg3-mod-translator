namespace BG3LocTool.Core;

/// <summary>
/// BG3 modding 知识精华（从 bg3-llm-wiki/sources/*.md 9100 行提炼 ~6KB）。
/// 注入 LLM system prompt，靠 Anthropic/OpenAI prompt cache 跨翻译批次摊销成本。
/// 同批 N 条翻译共享 cache → 每条 system prompt 摊销 ~$0.0001。
///
/// 内容选取标准：直接影响翻译决策的"语境知识"，不重复 Bg3Glossary 字典数据。
/// </summary>
public static class Bg3ModdingWiki
{
    public static readonly string ForSystemPrompt = """
        BG3 MODDING CONTEXT (relevant when translating mod descriptions, spell text, item lore, UI strings):

        ## Patch 8 现状（2026-05）
        - BG3 已发布官方 Toolkit + 官方简中 (Chinese.pak)；玩家 99% 装 Script Extender (SE)
        - 字典权威来源：游戏本体 `Data/Localization/Chinese/Chinese.pak` 解包，218,696 条 Larian 官方对照
        - SE 当前内部版本 v32（Patch 6=v9 / Patch 7=v21 / Patch 8 起 v23+）
        - **Mod Fixer 在 Patch 8 起反而有害**（Patch 6/7 工具，Patch 8 已修，留着会引发问题）

        ## 物品/装备/spell mod 实现模式（影响翻译时的语境理解）
        - Patch 8 主流：**SE Lua runtime override** — 不建 RootTemplate，靠 BootstrapServer.lua hook StatsLoaded 改 vanilla 装备数值（如 Awakened Spear of Selune）
        - 翻译这类 mod 的描述时要理解："覆盖 vanilla 武器加 buff" 不是"创建新武器"
        - SpellType 主要 11 种：Target / Projectile / ProjectileStrike / Zone / Wall / Rush / Shout / Throw / Teleportation / Storm / MultiStrike

        ## 借用 asset library 是 Patch 8 主流
        - 自定义 spell 视觉常借：VFX_Library_SHV (Shivero, 496 VFX) / VladsCodex / VladsGrimoire / Icon_Library_SHV (1000+ icons)
        - 翻译涉及"chromatic spell"、"colorful VFX"、"Vlads' template" 时要懂这是引用社区资产库

        ## 占位符与标签（翻译时务必保留 100%）
        - `[1]` / `[2]` 等：动态数值占位符（伤害值/距离/回合数），后跟单位词（点伤害/米/回合）
        - `&lt;LSTag .../&gt;...&lt;/LSTag&gt;`：游戏内 tooltip 跳转，整体保留，只译标签内文本
        - `&lt;br&gt;` / `&lt;br/&gt;`：换行，保留
        - 严禁反转义为真正的 `<` `>`（会破坏 XML）

        ## 文体（D&D 5E + Larian 风格）
        - 技能描述：精确严谨。"Expend a spell slot" → "消耗一个法术位"（不是"花费"或"使用"）
        - 物品/角色描述：中世纪奇幻文学。"The ancient sword gleams" → "古剑绽放...光辉"（避免直译"古老的剑闪光"）
        - 状态：用大写英文 → 中文官译。`PRONE→倒伏 / RESTRAINED→束缚 / FRIGHTENED→恐慌 / STUNNED→震慑 / PARALYZED→麻痹`
        - 检定：`Saving Throw → 豁免检定`（不是"豁免投掷"），`Ability Check → 属性检定`，`Attack Roll → 攻击掷骰`

        ## D&D 5E 状态/资源关键词（用于 LSTag Tooltip 内）
        - Status types: BLEEDING / BURNING / POISONED / BLINDED / DEAFENED / CHARMED → 流血/燃烧/中毒/目盲/耳聋/魅惑
        - Resource: Spell Slot (法术位) / Sorcery Point (法力点) / Ki Point (气点) / Bardic Inspiration (吟游灵感)
        - Damage types: Slashing/Piercing/Bludgeoning/Fire/Cold/Lightning/Thunder/Acid/Poison/Necrotic/Radiant/Force/Psychic
                        → 挥砍/穿刺/钝击/火焰/寒冷/闪电/雷鸣/酸蚀/毒素/腐蚀/光耀/力场/心灵
        - Schools of magic: Abjuration/Conjuration/Divination/Enchantment/Evocation/Illusion/Necromancy/Transmutation
                            → 防护/咒法/预言/惑控/塑能/幻术/死灵/变化

        ## 翻译失败时的 fallback
        - 完全没把握 → 保留英文不翻（让用户看到原文比强行机翻好）
        - 占位符/标签数量原文有 N 个，译文必须严格 N 个（PlaceholderValidator 会校验，不一致会 retry）
        """;
}
