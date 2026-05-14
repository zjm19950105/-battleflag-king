# 技能描述逻辑审计接力提示词 - 2026-05-14

用途：把这几轮手测和查证得到的“技能描述如何落到 structured effects”整理成一份可交给无记忆 AI 的任务书。下一位 AI 不要把本文当成已经实现完成的证明；每一项都要以代码、JSON、NUnit 和必要资料核对为准。

## 已确认的描述逻辑

1. 属性 buff/debuff 默认持续整场战斗。
   - GameFAQs 玩家机制讨论明确说，buff/debuff 会持续整场，除非技能写明只作用于一次攻击。
   - RPGDL 数据贴也把 stat-downs 描述为持续到本次 encounter 结束。
   - 因此 `AddBuff` / `AddDebuff` 对 `Str/Mag/Def/MDef/Spd/Hit/Eva/Crit/Block` 等属性的普通增减，默认应写 `turns: -1`。
   - 只有技能文本明确写“下一次攻击”“一度だけ”“for one attack”等，才使用 `oneTime: true`、`TemporalMark` 或有限次数。

2. 异常状态不是普通属性 buff/debuff。
   - Stun/气绝、Darkness/黑暗、Freeze/冻结、PassiveSeal、BlockSeal 等按各自异常生命周期处理。
   - 不要把“属性增减整场持续”的规则直接套到异常状态。

3. 带伤害攻击附带的 debuff，命中后才附加。
   - Smash、Rolling Axe、Frenzy、Wide Breaker 这类“攻击 + 目标物防下降”应走 `OnHitEffect(AddDebuff...)`。
   - MISS、EVADE、Darkness 导致的 miss、全 hit 未命中时，不附加 debuff。
   - 纯妨害技能没有伤害命中判定时，可以是顶层 `AddDebuff`。

4. 一个技能是一个原子动作，技能内伤害结算使用本次技能开始结算时锁定的目标状态。
   - 例如 Rolling Axe 描述为“敌一排 3hit，目标物防 -15%”：三段伤害都按减防前的 Def 计算；技能所有 hit 结算完后，才对命中的目标写入 Def -15%。
   - 该 debuff 不应该影响同一个 Rolling Axe 的第 2/3 hit。
   - 对每个目标每次技能最多附加一次该 debuff；不要按 3 hit 叠 3 层，除非找到明确原版证据。

5. 多段攻击内部仍然是 per-hit 判定。
   - 每 hit 独立命中/闪避/暴击/格挡/无效。
   - Keen Call / 锐利呐喊这类“下一次攻击”作用于整次攻击，因此多段攻击的每个 landed hit 都吃到 100% 暴击。
   - 但“命中后附加 debuff / 命中时资源收益”要看技能文本：有些是“任意 hit 命中后触发一次”，有些是“第 1 hit 未被格挡”等特殊 gate。不要一刀切。

6. 技能描述顺序很重要，但要结合原版术语。
   - “攻击，目标物防-15%”通常意味着先完成本次攻击伤害，再写入目标 debuff。
   - “自身有 buff 时威力+50”是伤害计算前条件。
   - “命中时”“击倒时”分别应映射到 `OnHitEffect` / `OnKillEffect`。
   - “下一次攻击”“一度だけ”是一次性增强或 temporal mark。
   - 对“攻击，自身物防+20%”这类描述，不能凭中文逗号自作主张，应查原文和手测，确认 buff 是攻击前还是攻击后生效。

7. 一列/一排/前后列必须严格区分。
   - 本项目 `TargetType.Row` 表示目标所在前排或后排横排。
   - `TargetType.FrontAndBack` 表示 1-4 / 2-5 / 3-6 的前后贯通。
   - `TargetType.Column` 表示同纵列，通常用于辅助或少数明确纵列效果。
   - 日文/英文资料里的 “row / 敵一列” 对当前战斗横排语义通常应落为 `Row`，不要误写成 `Column`。

8. buff/debuff 百分比叠加使用装备后基准值。
   - 物攻/物防/魔攻/魔防要先合并装备 combat stat：`Str+phys_atk`、`Def+phys_def`、`Mag+mag_atk`、`MDef+mag_def`。
   - 百分比 buff/debuff 按这个 equipped baseline 加算，不按当前值连续乘算。
   - UI 面板、预览、战斗日志和 `DamageCalculator` 必须显示/使用同一结果。

9. BattleEnd 被动会影响最终胜负。
   - BattleEndEvent 触发的治疗、HP 改变、pending attack 必须在最终 BattleEnd 结构日志和最终结果锁定前结算。
   - 结算后按当前 HP/存活状态重算结果。

10. 不要恢复旧双轨。
    - 新语义继续走 structured `effects`。
    - 不要恢复 `ISkillEffect` / `SkillEffectFactory` / `Skills/Effects/*`。

## 已确认的 Housecarl / Viking 技能要点

- Smash：单体物理攻击，目标物防 -20%，命中后附加，持续整场。
- Rolling Axe / Roll Ax：AP1，物理 40，3 hit，命中 75%，敌一排，目标物防 -15%。每个目标本技能任意 hit 命中后，在技能结算后附加一次 Def -15%，持续整场；不要让第 2/3 hit 吃到第 1 hit 的减防，也不要叠 3 层。
- Frenzy：反击，目标物防 -15%，命中后附加，持续整场。
- Wide Breaker：AP2，物理 100，1 hit，命中 100%，敌一排，目标物防 -30%；若目标已有 debuff，威力 +50。命中后附加 Def -30%，持续整场。

## 下一位无记忆 AI 的任务提示词

你在 `C:\Users\ASUS\战旗之王` 工作。先读：

- `AGENTS.md`
- `docs/csharp-architecture.md`
- `docs/dev-mistakes.md`
- `docs/battle-mechanics-research-2026-05-14.md`
- `docs/skill-description-logic-audit-prompt-2026-05-14.md`

先执行：

```powershell
git status --short
dotnet test goddot-test\goddot-test.csproj --no-restore
dotnet build goddot\goddot.csproj --no-restore
```

目标：系统性审计每个角色的 active/passive 技能描述、JSON structured effects、战斗代码和 NUnit 回归是否一致。不要直接凭感觉大改；先建立清单，再分批修。

硬性约束：

- 不要恢复旧 `ISkillEffect` / `SkillEffectFactory` / `Skills/Effects/*` 双轨。
- 新机制继续走 structured `effects`。
- 改 JSON/schema/枚举后同步 `docs/csharp-architecture.md`。
- 不要恢复或覆盖用户/前序 AI 的未提交改动，尤其注意 `roadmap.md` 可能有无关脏改。
- UI/Godot 展示改动后提醒人工 F5 冒烟。

审计方法：

1. 从 `goddot/data/characters.json` 枚举所有角色可用 active/passive skill id。
2. 对每个 skill 读取 `goddot/data/active_skills.json` / `passive_skills.json`：
   - AP/PP cost
   - power
   - hitRate
   - hit count
   - skill type / attack type
   - target type
   - effectDescription
   - tags
   - structured effects
3. 对照外部资料和原版术语，优先使用 HyperWiki、GameFAQs guide、RPGDL 数据贴、官方/可信日文资料；查不到就标为“需手测确认”，不要编。
4. 对每个技能判断描述顺序：
   - 伤害前计算修正：`ModifyDamageCalc`
   - 命中后效果：`OnHitEffect`
   - 击倒后效果：`OnKillEffect`
   - 行动后/受击后/战斗结束：对应事件 timing
   - 一次性效果：`AugmentCurrentAction` / `TemporalMark` / `oneTime`
   - 整场属性 buff/debuff：`turns: -1`
5. 特别检查这些高风险类别：
   - “敌一列/row”是否误写成 `Column`
   - “前后贯通/front and back”是否误写成 `SingleEnemy` 或 `Row`
   - 攻击附带 debuff 是否包在 `OnHitEffect`
   - 多段技能是否错误地每 hit 叠 debuff，或让 debuff 影响同技能后续 hit
   - `casterHasBuff` 是否识别 flat buff
   - “下一次攻击”是否错误写成整场 buff
   - “整场 buff/debuff”是否错误写成 `turns: 1`
   - cover 是否正确区分 `Ranged/Melee/Magic`
   - pending/counter/pursuit 是否用 `SourceKind` 防止套娃
   - BattleEnd 被动是否在最终结果前结算
6. 输出一个审计表，建议列：
   - skill id
   - 中文名
   - 资料结论
   - 当前 JSON/代码状态
   - 是否一致
   - 修复方案
   - 需要新增的 NUnit 测试
   - 是否需人工手测
7. 分批实现修复，每批只改相关 JSON/代码/测试，并跑：

```powershell
dotnet test goddot-test\goddot-test.csproj --no-restore
dotnet build goddot\goddot.csproj --no-restore
```

优先第一批审计对象：

- Housecarl / Viking：Smash、Rolling Axe、Frenzy、Wide Breaker、War Horn、Frenzied Strike/Counter 类。
- Fighter / Vanguard：Arrow Cover、Attract Attention/Provoke、Defender、Shield Bash。
- Soldier / Sergeant：Long Thrust、Enhanced Spear、Javelin、First Aid。
- Lord / High Lord：Noble Guard、Luminous Cover、Rapid Order、Lord Slash/Holy Blade 类治疗顺序。
- Sellsword / Landsknecht：Killing Chain、Following Slash、Wide Counter、Bastard’s Cross。

已知资料起点：

- RPGDL 数据贴：<https://www.rpgdl.com/forums/index.php?topic=7199.0>
- HyperWiki Rolling Axe：<https://hyperwiki.jp/unicorn/active-skill/a92/>
- HyperWiki Wide Breaker：<https://hyperwiki.jp/unicorn/active-skill/a93/>
- GameFAQs buff/debuff 持续讨论：<https://gamefaqs.gamespot.com/boards/426397-unicorn-overlord/80773147>
- GameFAQs Infantry guide：<https://gamefaqs.gamespot.com/switch/426397-unicorn-overlord/faqs/81216/infantry>
- Fandom Housecarl：<https://unicornoverlord.fandom.com/wiki/Housecarl>

最终交付：

- 一份审计清单 md。
- 已修复的 JSON/代码/测试。
- NUnit 和 build 结果。
- 无法确认的机制清单，明确要求用户手测或继续查资料。
