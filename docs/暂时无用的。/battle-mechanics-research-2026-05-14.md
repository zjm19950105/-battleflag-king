# 战斗机制调研 - 2026-05-14

范围：本文只记录调研结论和实现规划，不代表代码已经完成。下一轮实现仍必须补 NUnit 回归测试，并且所有新增语义都要继续落在 structured effects 中。

## 已检查来源

- 官方系统页：战斗行动会持续到所有角色都无法行动，或其中一方被击败；AP/PP 是战斗资源。<https://unicorn-overlord.com/system/stage-battle/>
- Atlus 官方提示：默认目标选择倾向前排，除非战术或远程/飞行条件介入；High Swing 被列为远程列攻击。<https://www.atlus.co.jp/news/22997/4>
- Game.srv 全技能列表：用于核对 AP、威力、射程特性、前后贯通、Shield Bash、Passive Steal、Defender、Arrow Cover、Attract、First Aid、Rapid Order、Sharp Yell、High Swing、Long Thrust、Javelin、Enhanced Spear。<https://game.srv.world/note?os=UNICORN_OVERLOAD&p=skill>
- ds-can 被动笔记：同时发动限制按触发时机判断，Arrow Cover 会掩护远程物理攻击，并提供被动行动阶段细节。<https://ds-can.com/unicorn/chara/passive.html>
- WikiWiki 战术页：默认目标排序、近战被前排阻挡、优先条件与限定条件的关系，以及 FrontAndBack 选择说明。<https://wikiwiki.jp/uol_db/%E4%BD%9C%E6%88%A6%E3%81%AB%E3%81%A4%E3%81%84%E3%81%A6>
- Usagiga 机制笔记：多段攻击每 hit 独立判定命中/暴击/格挡/闪避；Attract 合法性细节；双方都无法行动时战斗结束；部分掩护/防御边界情况。<https://note.com/usagiga/n/n74811e955385>
- HyperWiki Bastard's Cross：确认 70 物理威力、2 hit，目标 HP 越高威力越高，最高 +60。<https://hyperwiki.jp/unicorn/active-skill/a46/>
- tkgukei 防御笔记：防御减伤为无盾 25%、盾 50%、大盾 75%。<https://note.com/tkgukei/n/n18c686e66698>
- RPGDL 数据贴：buff/debuff 作用于战斗中的最终装备后属性值。<https://rpgdl.com/forums/index.php?topic=7199.0>
- Fandom Vanguard 页面：Provoke 为战斗开始被动，令敌一排集中攻击自己并 +50% Guard；Provoke II 勇气技能持续 10 秒。<https://unicornoverlord.fandom.com/wiki/Vanguard>
- GameWith First Aid 页面与 TheGamer 道具说明：First Aid/First Aid Kit 表达为治疗友方/其他队员，当前实现按“不能治疗自己”落地。<https://gamewith.jp/unicorn-overlord/449048> / <https://www.thegamer.com/unicorn-overlord-best-accessories-ranked/>
- Kiro 伤害公式讲解：攻击力按“基础面板攻击力 + 武器威力”再乘加减攻 buff 倍率。<https://kirokiro.cc/games/114376>

## 2026-05-14 追加确认并已落地的规则

- 多段攻击：每 hit 独立走伤害公式，独立判定命中/闪避/暴击/格挡；每 hit 的 raw 小数伤害四舍五入成整数后再累加。本项目保留每 hit raw 明细和 applied 整数。
- 默认目标：没有 `Only` / `Priority` 条件且没有强制目标时，`SingleEnemy` 在所有合法目标中随机选择；近战前排阻挡仍先过滤合法候选。
- Buff/debuff 百分比：按装备后基准值加算，不按当前值乘算；物理/魔法攻防也包含 `phys_atk` / `phys_def` / `mag_atk` / `mag_def` 后再吃百分比。
- Provoke / 吸引：被动版整场战斗持续；勇气技能 Provoke II 是大地图 10 秒效果。
- Rapid Order / 快速命令：开场被动，速度 +20 整场战斗持续。
- First Aid / 急救：治疗其他友方，不治疗自己；当前用 structured target 参数 `excludeSelf: true` 表达。
- Keen Call / 锐利呐喊：对友方下一次攻击赋予 100% 暴击率，多段攻击中每个 landed hit 都暴击。
- 普通 stat buff/debuff 持续：当前资料支持“未特别写明的一般 buff/debuff 持续到本次战斗结束，或被 dispel / anti-debuff 移除”。因此 JSON 里普通 `AddBuff` / `AddDebuff` 使用 `turns: -1`；只有明确写“一次/下一次/一度だけ”的效果使用 `oneTime: true` 或有限回合。已据此把 Guardian / ガーディアン 与 Outrage / アウトレイジ 的可叠加 buff 从 `turns: 1` 改为 `turns: -1`。
- 带伤害攻击后的对象 debuff：公开技能表只写“対象の物理防御力-xx%”，没有一手资料直接说明 MISS 后是否附加；但玩家机制讨论与游戏通常语义支持“攻击至少一 hit 命中后才附加”。项目采用保守建模：Rolling Axe / ロールアックス、Wide Breaker / ワイドブレイカー、Smash、Frenzy/反击类物防下降都包在 `OnHitEffect`，MISS/全回避不减防。
- Rolling Axe / ロールアックス：HyperWiki 明列 AP1、物理威力 40、3 hit、命中 75%、敌一列、对象物防 -15%。本地 `act_whirlwind_slash` 已修正为 `apCost: 1`、`targetType: Row`，并保持 `OnHitEffect(AddDebuff Def -15%, turns -1)`。
- Wide Breaker / ワイドブレイカー：HyperWiki 明列 AP2、物理威力 100、1 hit、命中 100%、敌一列、对象物防 -30%、对象有 debuff 时威力 +50。本地 `act_break_formation` 已保持 `SkillPowerBonus: 50` + `targetHasDebuff: true`，并保持 `OnHitEffect(AddDebuff Def -30%, turns -1)`。

本轮新增来源：
- HyperWiki Rolling Axe / ロールアックス：技能名、AP1、物理 40、3 hit、命中 75%、敌一列、物防 -15%。<https://hyperwiki.jp/unicorn/active-skill/a92/>
- HyperWiki Wide Breaker / ワイドブレイカー：AP2、物理 100、1 hit、命中 100%、敌一列、物防 -30%、目标 debuff 时威力 +50。<https://hyperwiki.jp/unicorn/active-skill/a93/>
- HyperWiki Guardian / ガーディアン：物理攻击受击后触发，自身物防 +20%、格挡率 +20%、可叠加。<https://hyperwiki.jp/unicorn/passive-skill/a442/>
- HyperWiki Outrage / アウトレイジ：友方受击时触发，自身攻击力 +20%、命中 +20、可叠加。<https://hyperwiki.jp/unicorn/passive-skill/a340/>
- GameFAQs 讨论贴 Start of battle skills duration：社区结论为 buff/debuff skills 持续整场，除非技能写明只适用于一次攻击。<https://gamefaqs.gamespot.com/boards/426397-unicorn-overlord/80773147>
- Reddit 讨论 Do Druid/shaman curses last an entire fight unless cleansed?：玩家回答支持除 Quick Curse 等特例外持续整场。<https://www.reddit.com/r/UnicornOverlord/comments/1bkkee6/do_druidshaman_curses_last_an_entire_fight_unless/>
- TV Tropes 状态效果条目：二手资料称 buffs/debuffs 持续到战斗结束或被移除。<https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/UnicornOverlord>
- Reddit 近期机制讨论 Confusion over Featherbow Mercenary Dialogue：玩家称“至少一 hit 命中才附加 debuff/buff”，但这不是官方资料。<https://www.reddit.com/r/UnicornOverlord/comments/1nbbpyy/confusion_over_featherbow_mercenary_dialogue/>

仍不确定：
- 没找到官方手册或游戏内提示逐字说明“攻击附带 debuff 在 MISS/全回避时不附加”。当前采用 `OnHitEffect` 是基于玩家机制讨论、技能描述语义和项目既有命中后效果模型。
- 没找到 Rolling Axe 多段攻击是“任意一 hit 命中即附加一次 debuff”还是“每个目标每次技能最多附加一次”的官方文本；当前 `OnHitEffect` 按本次伤害结果命中后执行一次，符合项目已有 on-hit 语义。

## 已确认或强支持的修复项

### 1. Arrow Cover / 远程物理掩护

当前本地风险：
- `pas_cheer_shout` 已正确标记为 `RangedCover`，但 `PassiveSkillProcessor` 从战斗开始就一直保留 `_defenseFired`，没有按每次来袭攻击/触发时机重置。这会让同时发动限制下的防御被动几乎变成每场战斗只能触发一次。
- `act_wing_gust` 当前是 `attackType: "Melee"`，但外部来源把 High Swing / 风翼突袭 列为远程列物理攻击。
- Arrow Cover 将伤害无效化时，日志仍可能打印一个以 0 结尾的格挡公式，例如 `BLOCK(-50%)` 且伤害为 0。这个 0 来自 `NullifyPhysicalDamage`；日志应该清楚显示无效化。

计划：
- 按每个 `BeforeHitEvent` 重置防御时机的同时发动限制状态，或在每次 `ProcessTiming` 调用中使用局部 `fired set`（已触发集合）。它应该阻止同一次来袭攻击中同一时机的两个掩护被动同时触发，而不是阻止之后的攻击/回合。
- 将 `act_wing_gust.attackType` 改为 `Ranged`。
- 增加测试：Arrow Cover 会对 High Swing / 风翼突袭触发；只要 PP 还够，后续攻击可以再次触发；不会对近战前后贯通枪技触发；无效化伤害日志包含 `NULLIFY`/`无效`，而不是只有误导性的格挡公式。

### 2. Long Thrust / Enhanced Spear 前后贯通

当前本地风险：
- `act_enhanced_spear`（长枪突刺 / Long Thrust）和 `act_throwing_spear`（强化长枪 / Enhanced Spear）的中文描述都写了前后贯通，但 `targetType` 是 `SingleEnemy`。
- 来源技能列表显示 Long Thrust 和 Enhanced Spear 是前后贯通攻击。Javelin 才是带远程特性的枪技能；Long Thrust 和 Enhanced Spear 不等同于 Javelin。
- `act_throwing_spear` 当前消耗 AP1，但来源列出 Enhanced Spear 为 AP2。

计划：
- 将两个前后贯通枪技能都改为 `targetType: "FrontAndBack"`。
- 审核攻击类型：
  - Javelin / 标枪 是远程。
  - Long Thrust / 长枪突刺 看起来是非远程的前后贯通物理。
  - Enhanced Spear / 强化长枪 尽管当前 id 名为 `act_throwing_spear`，但看起来也是非远程的前后贯通物理。
- 如果来源映射与项目内目标中文技能确认一致，将 Enhanced Spear 的 AP 消耗改为 2。
- 增加测试，覆盖从选中锚点扩展到 1/4、2/5、3/6 位置都正确。

### 3. Shield Bash 气绝

当前本地风险：
- JSON 中 `act_shield_bash` 的 `OnHitEffect` 带有 `chance: 25`。
- 公开技能描述说 Shield Bash 会造成气绝；已检查来源没有提到低概率。

计划：
- 除非找到更好的来源说明有概率，否则移除 25% 概率，让气绝在命中后必定附加。
- 增加测试：命中会附加 `StatusAilment.Stun` 并使下一次行动跳过；miss/evasion 不附加气绝。

### 4. Buff 状态与 Enhanced Spear +50

当前本地风险：
- `SkillEffectExecutor.HasBuff(caster)` 目前只把正向 ratio buff 视为 buff 状态。Rapid Order 的 `Spd +20` 这类正向 flat buff 会被漏掉。
- 来源和战术页面都把 `buff state` 当作一般正向状态类别，而不是只限 ratio buff。

计划：
- 对 `casterHasBuff` / `targetHasBuff`，把任何纯正向 buff（`Ratio > 0` 或 `FlatAmount > 0`）都视为 buff 状态。
- 增加测试：Rapid Order 会让 soldier 被判定为已获得 buff，因此 Enhanced Spear 获得 +50 威力。

### 5. 没有人能实际行动时的战斗终止

当前本地风险：
- `BattleEngine.CheckApExhaustion()` 只有在双方都没有 AP 时才结束。
- 如果单位有 AP，但没有合法技能/目标、卡在蓄力、或反复无法行动，`StepOneAction()` 会记录“无法行动，跳过”，但不消耗 AP，并可能一直转到安全上限。

来源：
- 官方与机制笔记都说明，战斗会在所有角色都无法行动时结束，而不是只看 AP 是否为 0。

计划：
- 增加完整一轮的“无可行动单位”检测。如果完整队列轮询没有产生行动，也没有 pending action，且双方都存活，则使用 AP 耗尽时同一套 HP 比例规则结束战斗。
- 增加测试，覆盖 AP 仍存在但所有单位都无法选择/使用任何行动的场景。

### 6. 同时发动限制

当前本地风险：
- `_allyBuffFired` 和 `_defenseFired` 的保存范围过宽，会压制后续本来合法的被动。

来源：
- ds-can 将同时发动描述为同一时机的限制。它不是 Arrow Cover 的每场战斗一次标记。

计划：
- 将 fired set 的作用域收窄到当前 timing occurrence。BattleStart 可以继续保持战斗开始范围。
- 增加测试：两次独立的远程来袭攻击中，只要 PP 足够，同一个掩护被动可以触发两次。

### 7. 技能描述 / UI 详情

当前问题：
- 一些可见描述缺少 AP、威力、hit 数、目标形状和攻击属性，导致用户必须从日志反推。

计划：
- 在 tooltip/detail UI 中展示数据里的 canonical 字段：AP、power、hit rate、hit count/effects summary、target type、skill type、attack type。
- 这是 UI/Godot 侧改动；实现后需要手动 F5 冒烟。

## 大概率已经正确，但需要回归测试的规则

### 8. 多段攻击的命中/暴击/格挡/闪避判定

来源：
- Usagiga 明确说明，多段攻击会按每 hit 独立判定命中、暴击、防御和闪避。

含义：
- 当前 per-hit roll 模型整体有来源支持。用户观察到“重装应该只挡第一 hit”，但已调研来源对普通多段结算更支持每 hit 判定。

已确认：
- 每 hit 独立公式和独立判定，并逐 hit 取整后累加。本项目用 `DamageHitResult` 同时记录每 hit raw 小数和 applied 整数。

计划：
- 保留日志中的 per-hit breakdown。
- 用测试锁住“小数多段伤害逐 hit 取整后累加”的行为。

### 9. Sharp Yell / ForceCrit 对多段攻击的作用

来源：
- Sharp Yell 是让友军当前一次行动暴击率 100% 的被动。
- 多段攻击每 hit 独立判定暴击。

含义：
- 如果当前行动的暴击率被强制为 100%，那么该行动中每个命中的 hit 都应该暴击，除非被 crit-seal 规则阻止。

计划：
- 增加回归测试：Sharp Yell + Meteor Slash 的每个 landed hit 都暴击。

### 10. Defender 在 2-hit 攻击上的 PP 获得

来源：
- Defender 描述为攻击命中时 PP +1，没有说必须所有 hit 都命中。

含义：
- 当前“任意 landed hit 即可”的行为很可能正确。已检查来源不支持 all-hit requirement。

计划：
- 增加测试：一个 hit 命中会获得 PP；零 hit 命中不会获得 PP。

### 11. Buff 叠加算术

当前本地行为：
- Buff ratio 在相关 base/current-stat 快照上加算，所以 Def 42 加两个 +20% buff 时日志大约是 42->50->58，而不是乘算 42->50->60。

已确认：
- Buff/debuff 百分比按装备后基准值加算，不按当前值乘算。物理攻击/防御这类战斗值使用 `Str + phys_atk` / `Def + phys_def` 作为百分比基准。

计划：
- 保持加算叠加。
- 为正向 ratio 叠加、装备后 combat baseline、flat/ratio 混合 buff 显示增加显式测试。

### 12. 物理防御与魔法防御拆分

本地代码事实：
- `DamageCalculator` 调用 `resolvedDefender.GetCurrentDefense(skill.Type)`。
- `BattleUnit.GetCurrentDefense(Physical)` 使用 `Def/phys_def`；`Magical` 使用 `MDef/mag_def`。
- `act_hache` 当前是 `Physical`。

含义：
- 仅靠代码检查，我没有直接找到“物理攻击用了 MDef”的 bug。它可能是数据映射、日志解读、混合伤害或测试沙盒状态问题。

计划：
- 增加强回归测试：
  - 物理技能只随 Def/phys_def 变化；
  - 魔法技能只随 MDef/mag_def 变化；
  - Def 和 MDef buff 不会交叉应用；
  - 日志足够清楚地标出所选防御，方便调试。

## 需要手测或更好来源确认的开放问题

### 13. Passive Steal 第一 hit 被闪避

来源确认：
- Passive Steal 在命中时偷 PP；第一 hit 被防御会阻止偷取。

未确认：
- 如果第一 hit 被 evaded/missed，但第二 hit 命中，会发生什么。

当前本地行为：
- `requireFirstHitUnblocked` 只检查 `!firstHit.Blocked`；第一 hit miss/evasion 且第二 hit 命中时仍可能偷取。

计划：
- 如果手测确认需要新语义，再增加一个可选语义，例如 `requireFirstHitLandedAndUnblocked`。
- 在此之前，不要静默修改这条规则。

### 14. Lean Edge / Lord slash 治疗动画

来源确认：
- 该技能命中后治疗 25%，击杀时额外治疗 25%。

未确认：
- 原版 UI 是播放两段治疗动画，还是合并成一次 50% 动画。

计划：
- 逻辑上应保留两个独立 effect，因为它们是两个独立条件。
- 动画/日志显示可以继续作为两个 post-effect 条目，除非真实游戏录像显示它们会合并。

### 15. Bastard's Cross 精确 HP 比例公式

来源确认：
- 基础威力 70、2 hit；目标 HP 越高加成越大，最高 +60。

可能但未完全证明：
- 线性加成 `floor(60 * targetCurrentHp / targetMaxHp)`。
- 对 94/151 来说，这会得到 +37，有效威力 107；该算术内部自洽。

未确认：
- floor 还是 round；
- 比例使用行动开始时的当前 HP、每 hit 前 HP，还是原目标与掩护目标的 HP；
- 超过最大 HP 时是否 clamp 到 100%。

计划：
- 在用户提供原版游戏校准点前，保留线性 floor 公式作为显式假设。
- 如果继续保留该假设，需要在架构文档中记录。

### 16. Attract 持续时间、作用范围与合法性

来源支持：
- Attract / Provoke 是 battle-start，令敌方一排攻击转向施法者，并提供 block +50；被动版整场战斗持续。
- 机制笔记说明，如果 attract 使用者在后排且有前排友军，非远程攻击可能不合法；远程攻击仍可选中。

当前本地风险：
- `ForcedTarget` 只是施法者身上的时效标记，很可能影响所有敌人，而不是一排敌人。
- 它可能完全没有建模“受影响的 attacker row”。

可能计划：
- 扩展 `ForcedTarget`，记录受影响的敌方单位/排，以及合法目标规则。
- 保持整场战斗持续。
- 增加测试：
  - 只有受影响的敌方排会被重定向；
  - 远程攻击者可以选中被吸引的后排施法者；
  - 近战攻击者仍遵守前排合法性，除非来源/手测证明不是这样；
  - Row / Column / FrontAndBack 技能只有在合法时才以 forced target 作为锚点。

### 17. First Aid 自身目标

来源与用户确认：
- First Aid 描述为 ally single HP recovery；用户追加资料按“其他友方”解释，项目当前规则为不能治疗自己。

含义：
- 战斗结束时急救不能把最低 HP 目标选成施法者自己。

计划：
- 使用 structured target 参数 `excludeSelf: true` 表达，不恢复 legacy 分支。
- 增加测试：有其他受伤友方时治疗其他友方；没有其他存活友方时不治疗自己且不崩溃。

### 18. 无条件默认目标

来源与用户确认：
- 没有设置条件时，技能在可攻击的合法目标中随机选择；战术条件用于控制目标偏好。

计划：
- `TargetSelector` 先按技能合法性和前排阻挡得到候选，再在无条件 `SingleEnemy` 下随机选一个候选。
- `Only` / `Priority` / `ForcedTarget` 不走随机兜底，继续保留条件过滤和排序语义。

## 更广范围的技能审计清单

实现前应审计这些技能类别，而不只限于用户手测过的例子：

- 前后贯通技能：所有描述含“前后列”“前后贯通”的技能都必须有 `TargetType.FrontAndBack`，并且远程/近战特性正确。
- 远程掩护与掩护技能：掩护必须受来袭攻击类型、同时发动限制范围、PP 可用性和实际合法目标共同约束。
- Buff 状态条件技能：`casterHasBuff` 和 `targetHasBuff` 必须包含正向 flat buff 与正向 ratio buff。
- 多段技能：hit count、per-hit breakdown、on-hit effects、first-hit gates、nullify/evasion 消耗和日志分组都要检查。
- On-hit 资源效果：any-hit、all-hit、first-hit requirements 必须在 structured effect 参数里显式表达。
- Battle-start 强制目标/嘲讽：受影响方、排、持续时间、目标合法性，以及 row/column 扩展必须是显式数据，不能靠全局状态。
- Stun/ailments：所有描述“apply ailment”的技能都要验证概率；如果来源没有概率，默认应为命中后必定附加。
- Buff 持续时间：区分 battle-long buff 与“one action / one hit / one time” buff。`turns: -1` 用于整场战斗没问题，但一次性 buff 需要清理测试。
- 技能 tooltip/log 表面：AP、power、hit rate、hit count、attack type、target type 和 structured calculation modifiers 应该对测试者可见。

## 建议的下一个 AI 提示词

你正在 `C:\Users\ASUS\战旗之王` 中工作。先阅读 `AGENTS.md`、`docs/csharp-architecture.md`、`docs/dev-mistakes.md` 和 `docs/battle-mechanics-research-2026-05-14.md`。然后运行：

```powershell
git status --short
dotnet test goddot-test\goddot-test.csproj --no-restore
dotnet build goddot\goddot.csproj --no-restore
```

先只实现已确认或强支持的修复项；不要恢复旧的 `ISkillEffect` / `SkillEffectFactory` / `Skills/Effects` 双轨。所有新增语义都保留在 structured effects 中；如果 JSON/schema/enums 发生变化，更新 `docs/csharp-architecture.md`。

优先级顺序：

1. 修复防御类同时发动限制的作用域，让 Arrow Cover 和类似同一时机被动在之后的攻击中只要 PP 足够就能再次触发。为两次独立的远程物理攻击增加 NUnit 测试。
2. 将 `act_wing_gust` 修为远程物理，并确保 Arrow Cover 能掩护它；改进无效化日志，让 Arrow Cover 造成的 0 伤害显示为 nullify/无效，而不是误导性的格挡公式。
3. 在确认来源名称后，修复 Long Thrust / Enhanced Spear 的前后贯通目标数据与 AP/攻击类型映射。为 1/4、2/5、3/6 位置增加测试。
4. 修复 Shield Bash stun chance，除非有更好的来源确认它确实是概率效果。增加 hit/miss stun 测试。
5. 修复 buff 状态检查，让正向 flat buff 也计入 `casterHasBuff` / `targetHasBuff`。增加 Rapid Order + Enhanced Spear 威力加算测试。
6. 增加 no-action full-cycle battle termination，避免 AP 仍存在但无合法行动时无限循环。
7. 增加回归测试，覆盖 stat split Physical Def vs Magical MDef、multi-hit per-hit logs、Sharp Yell all-hit crit、Defender any-hit PP gain，以及 Passive Steal 当前 first-hit behavior。
8. 对 Attract，不要盲目快速修补。先把受影响的 enemy row、duration 和 legality 建模为 structured state，然后再补测试。

代码改动后运行：

```powershell
dotnet test goddot-test\goddot-test.csproj --no-restore
dotnet build goddot\goddot.csproj --no-restore
```

如果 UI/Godot tooltip/log display 有变化，说明需要手动 F5 冒烟。
