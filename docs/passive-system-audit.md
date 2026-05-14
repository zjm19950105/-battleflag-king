# Passive System Audit

最后更新：2026-05-10

审计基线：当前工作区 `dotnet test goddot-test\goddot-test.csproj --no-restore` 为 151/151 通过，`dotnet build goddot\goddot.csproj --no-restore` 为 0 警告。本文只描述当前本地代码与 JSON 状态，不包含代码改动。

## 数据流

1. `goddot/data/passive_skills.json` 定义 `PassiveSkillData`：触发时机、PP 消耗、`tags`、`effects`、`hasSimultaneousLimit` 与解锁等级。
2. `GameDataRepository.LoadAll()` 读取 JSON，建立 `PassiveSkills` 字典。
3. `BattleSetupService.CreateUnit()` 创建 `BattleUnit`，应用天数等级、CC 状态、初始装备、默认策略，然后 `AutoEquipPassives()` 从 `BattleUnit.GetAvailablePassiveSkillIds()` 里按 PP 消耗自动装备可用被动。
4. `BattleUnit` 运行时持有 `EquippedPassiveSkillIds`、`PassiveConditions`、PP/AP/HP、buff、异常、临时状态与自定义计数器。
5. UI 战斗入口创建 `BattleEngine` 后创建 `PassiveSkillProcessor(engine.EventBus, gameData, AppendLog, engine.EnqueueAction)` 并 `SubscribeAll()`。
6. `BattleEngine` 在状态机关键点发布事件：`BattleStartEvent`、`BeforeActiveUseEvent`、`AfterActiveCostEvent`、`BeforeAttackCalculationEvent`、`BeforeHitEvent`、`AfterHitEvent`、`AfterActiveUseEvent`、`OnKnockdownEvent`、`BattleEndEvent`。
7. `PassiveSkillProcessor` 按事件映射到 `PassiveTriggerTiming`，排序候选单位，检查存活、时机、上下文、PP、玩家设置条件、同时发动限制，然后消耗 PP。
8. 被动执行优先走 `effects`。有 `effects` 时，逐个交给共享 `SkillEffectExecutor`；没有 `effects` 时才进入 `ExecuteLegacyTags()`。
9. `SkillEffectExecutor` 可改 HP/AP/PP、buff/debuff、异常、临时状态、计数器、`DamageCalculation`，也可排入 `PendingAction`。`BattleEngine.ProcessPendingActions()` 再把反击、追击、先制、BattleEnd 攻击送进 `DamageCalculator`。
10. `DamageCalculator` 读取被动改过的 `DamageCalculation`，处理掩护后的实际承伤者、命中、闪避、格挡、暴击、多段、免疫和最终伤害。
11. 日志分两路：`OnLog` 文本给 UI；`BattleLogEntry` 目前主要由主动攻击、pending 被动攻击和 BattleEnd 生成。纯资源、治疗、buff、calc modifier 被动没有独立结构化日志。

## 当前分类

### 已结构化或基本结构化

这些被动有 `effects`，当前能通过共享 executor 产生主要运行时效果。部分仍保留展示/历史 tags，但现有代码不会再按 tags 执行它们。

| ID | 名称 | 说明 |
| --- | --- | --- |
| `pas_battle_horn` | 医疗援助 | BattleEnd 行治疗；结构化治疗已接入。 |
| `pas_brute_force` | 蛮力 | AP+1、Str/Spd buff 已结构化。 |
| `pas_charge_action` | 蓄力行动 | AP+1、暴击伤害 buff 已结构化。 |
| `pas_cheer_shout` | 箭矢掩护 | 远程物理上下文过滤、掩护、物理伤害无效已结构化。 |
| `pas_deflect` | 偏转 | 近战免疫临时状态、Eva buff 已结构化。 |
| `pas_dodge_step` | 闪避 | 强制闪避、AP+1 已结构化。 |
| `pas_elemental_action` | 元素行动 | AP+1、Sprite 计数器已结构化。 |
| `pas_end_heal25` | 快速守护 | 强制格挡已结构化。 |
| `pas_fluorescent_cover` | 光辉掩护 | 掩护已结构化。 |
| `pas_focus` | 聚焦 | 友方攻击前强制命中已结构化。 |
| `pas_give_ap` | 急救 | BattleEnd 低 HP 友方治疗已结构化。 |
| `pas_healing_touch` | 治愈之触 | AllyOnAttacked 治疗被攻击友方并净化 debuff 已结构化。 |
| `pas_line_cover` | 重掩护 | 掩护与强制格挡已结构化；大/中格挡是否需要区别仍需规则确认。 |
| `pas_parry` | 招架 | 近战免疫临时状态、AP+1 已结构化。 |
| `pas_quick_strike` | 快速打击 | BattleStart 先制攻击已结构化，已有真实 JSON 测试覆盖。 |
| `pas_rampage` | 终结打击 | BattleEnd pending 攻击已结构化，并可影响最终胜负重算。 |
| `pas_spirit_guard` | 精灵加护 | BattleStart 同列友方治疗、DebuffNullify 临时标记和 Sprite 计数器已结构化。 |
| `pas_unstoppable_rage` | 鹰眼 | 自身攻击前强制命中已结构化。 |
| `pas_vengeance_guard` | 复仇守护 | 强制格挡、Str buff 已结构化。 |

### Legacy tag-only

这些被动没有 `effects`，仍在 `DataContractTest.LegacyTagOnlySkillAllowlist` 的 `passive:*` 白名单内。新增语义不应继续进入这个模式。

| ID | 名称 | 当前运行时风险 |
| --- | --- | --- |
| `pas_aid_cover` | 援助掩护 | `MediumGuardAlly` 不被 fallback 识别，`HealAlly` 仅记录文字，基本 no-op。 |
| `pas_berserk` | 狂暴 | 仅 `ApPlus1` 生效；献祭 PP、死撑、同时发动语义缺失。 |
| `pas_bounce` | 弹跳 | `Heal40`/`Self` 不被 fallback 识别，no-op。 |
| `pas_calm_cover` | 战斗号角 | `IgnoreBlockAll` 不被识别，no-op。 |
| `pas_concentration` | 专注 | 仅 AP+1 生效；命中 buff 缺失。 |
| `pas_curse_swamp` | 诅咒沼泽 | BattleStart 没有 `DamageCalculation`，`SureHit` 也不会生效，其余 tag 缺失，no-op。 |
| `pas_cut_grass` | 追击 | `Pursuit` 不被 fallback 识别，no-op。 |
| `pas_emergency_cover` | 暴怒 | Atk+20 生效；Hit+20 缺失。 |
| `pas_fervor` | 空中狙击 | 反击可排队；飞行特攻缺失。 |
| `pas_formation_counter` | 主动礼物 | `ApPlus1Ally` 仅记录文字，无资源变化。 |
| `pas_hawk_eye` | 守护者 | Def+20 生效；Block+20 缺失。 |
| `pas_hundred_crit` | 吸引 | 嘲讽、格挡 buff 都缺失，no-op。 |
| `pas_magic_barrier` | 魔法屏障 | 魔法无效、异常无效缺失，no-op。 |
| `pas_magic_blade` | 魔法剑刃 | `MagicBlade` 缺失，no-op。 |
| `pas_muscle_swelling` | 锐利呐喊 | 友方必暴缺失，no-op。 |
| `pas_pursuit_magic` | 追击魔法 | 追击魔法缺失，no-op。 |
| `pas_quick_cast` | 快速施法 | 最速/暴击下降缺失，no-op。 |
| `pas_quick_reload` | 快速装填 | 追击缺失，no-op。 |
| `pas_rapid_order` | 快速命令 | 全体加速缺失，no-op。 |
| `pas_rapid_reload` | 格挡束缚 | 格挡封印缺失，no-op。 |
| `pas_row_barrier` | 列屏障 | 列魔法/异常屏障缺失，no-op。 |

### 半迁移

这些被动已经有 `effects`，但 tags 中仍有未结构化语义，或 JSON 参数与 executor 枚举/目标选择不完全匹配。因为有 `effects` 时 fallback 不会执行，遗留 tags 只剩显示价值，不能补效果。

| ID | 名称 | 缺口 |
| --- | --- | --- |
| `pas_holy_guard` | 神圣守护 | 强制格挡已结构化；`DebuffNullify` 缺失。 |
| `pas_iron_guard` | 狂暴 | 当前 effect 是 Def+20；tags 是 `Counter`/`DefDown15`，语义相互冲突，需要确认。 |
| `pas_noble_block` | 贵族守护 | 强制格挡和 Def+20 已结构化；`FreeBelow50Hp` 未实现，仍会消耗 PP。 |
| `pas_pursuit` | 列掩护 | 掩护和格挡已结构化；`scope: Row`/`MediumGuardRow` 目前未被 `CoverAlly` 执行逻辑使用，可能跨列/跨排掩护。 |
| `pas_pursuit_slash` | 追击斩 | 反击已结构化；命中回 PP 缺失。 |
| `pas_stealth_blade` | 潜行利刃 | 先制攻击已结构化；`MultiHit2` 与 `BlockSealEnemy` 状态未完整表达。 |

### 日志不足

当前结构化日志对“造成一次攻击”的被动覆盖较好，例如 `pas_quick_strike`、`pas_rampage`、反击类 pending action 会进入 `BattleLogEntry`。但以下场景只有 `OnLog` 文本，缺少可断言、可回放的 `BattleLogEntry`：

- 资源变化：RecoverAp、RecoverPp、ApDamage、PpDamage。
- 治疗与净化：RecoverHp、HealRatio、CleanseDebuff。
- buff/debuff：AddBuff、AddDebuff、RemoveBuff、RemoveDebuff。
- calc modifier：ForceHit、ForceBlock、ForceEvasion、CoverAlly、IgnoreDefense、NullifyDamage 等只体现在最终攻击文本或结果上，无法直接归因到哪个被动。
- legacy fallback 中的“待结构化”日志容易误导，例如 `HealAlly`、`ApPlus1Ally` 会写文字但没有改变状态。

### 需设计确认

这些语义不能只靠简单 effects 映射解决，迁移前需要先确认规则边界：

- 嘲讽/目标权重：`Taunt`、`IgnoreBlockAll`、`SpeedFastest`、`CritDown50`。
- 死撑/代价：`DeathResist`、`SacrificePp`、`FreeBelow50Hp`。
- 追击展开：`Pursuit` 的额外追击语义仍需确认；pending action 的 `targetType: Row` / `Column` 已在执行阶段按锚点目标展开。
- 行/列/范围掩护：`MediumGuardRow`、`LargeGuard`、`scope: Row`、`blockType` 如何影响候选人与减伤。
- 魔法/异常屏障：`DebuffNullify` 已先作为会被结构化 `AddDebuff` 消费的临时状态接入；`MagicNullify`、`AilmentNullify` 以及主动/被动异常应用前拦截仍需统一设计。
- 特攻与条件限定：`FlyingBonus100`、`MountedPpMinus1` 仍待设计；`pas_block_seal` 的 `IgnoreDef100Heavy` 已用 pending action `ignoreDefenseRatio` + `ignoreDefenseTargetClass` 表达。

## 角色维度风险

| 角色 | 天生被动风险 | CC 被动风险 | 备注 |
| --- | --- | --- | --- |
| `swordsman` 剑士 | `pas_quick_strike` 已结构化；`pas_parry` 已结构化 | `pas_charge_action` 已结构化 | 测试角色：`BattleSetupServiceTest` 与 `BattleEngineTest` 覆盖快速打击，当前安全。 |
| `mercenary` 佣兵 | `pas_pursuit_slash` 半迁移，命中回 PP 缺失 | `pas_brute_force` 已结构化；`pas_vengeance_guard` 基本结构化 | 反击能打，但资源反馈缺失。 |
| `lord` 领主 | `pas_noble_block` 半迁移；`pas_fluorescent_cover` 已结构化 | `pas_rapid_order` tag-only no-op | CC 后快速命令会消耗 PP 但无全体加速。 |
| `fighter` 战士 | `pas_cheer_shout` 已结构化；`pas_end_heal25` 已结构化 | `pas_hundred_crit` tag-only no-op | CC 吸引/格挡 buff 缺失。 |
| `soldier` 士兵 | `pas_give_ap` 已结构化但 PP2，day1 默认 PP1 自动装不上；`pas_muscle_swelling` no-op | `pas_formation_counter` log-only no-op | 主动礼物会写日志但不加 AP。 |
| `huskarl` 家臣 | `pas_rampage` 已结构化但 PP2，day1 默认 PP1 自动装不上；`pas_iron_guard` 半迁移且语义冲突 | `pas_calm_cover` no-op | 狂暴/战斗号角都需要确认。 |
| `hoplite` 重装步兵 | `pas_line_cover` 基本结构化；`pas_hawk_eye` 只有 Def buff | `pas_pursuit` 半迁移 | 守护者缺格挡 buff；列掩护可能不按行限制。 |
| `gladiator` 角斗士 | `pas_bounce` no-op | `pas_wide_counter` 已按行展开 pending counter；`pas_berserk` 仅 AP+1 | day1 弹跳会被自动装备但无治疗效果。 |
| `warrior` 勇士 | `pas_rapid_reload` no-op | `pas_block_seal` 已迁移为对 attacker 的 pending counter，且仅对 Heavy 目标无视防御；`pas_emergency_cover` 缺 Hit buff | day1 格挡束缚不会再污染 incoming calc。 |
| `hunter` 猎人 | `pas_unstoppable_rage` 已结构化；`pas_cut_grass` no-op | `pas_fervor` 只有普通反击 | 追击与飞行特攻缺失。 |
| `shooter` 射手 | `pas_battle_horn` 已结构化但 PP2，day1 默认 PP1 自动装不上；`pas_quick_reload` no-op | `pas_aid_cover` 基本 no-op | 援助掩护缺格挡与治疗。 |
| `thief` 盗贼 | `pas_dodge_step` 已结构化 | `pas_stealth_blade` 半迁移 | 测试只覆盖先制排队参数，未覆盖多段/封印。 |
| `wizard` 巫师 | `pas_counter_magic` 已排入 Magical/Magic 反击 | `pas_pursuit_magic` no-op；`pas_concentration` 仅 AP+1 | 追击魔法仍待迁移。 |
| `witch` 女巫 | `pas_magic_blade` no-op | `pas_focus` 已结构化；`pas_quick_cast` no-op | day1 魔法剑刃会自动装备但无效果。 |
| `white_knight` 白骑士 | `pas_magic_barrier` no-op | `pas_holy_guard` 半迁移；`pas_row_barrier` no-op | 魔法屏障是高可见度 no-op。 |
| `griffin_knight` 狮鹫骑士 | `pas_deflect` 已结构化 | 无 | 当前低风险。 |
| `shaman` 萨满 | `pas_quick_curse` 已迁移为攻击者 Str-20% + CritSeal，不再修改 incoming `ForceHit` | `pas_curse_swamp` no-op | day1 快速诅咒已从必中污染风险中移除。 |
| `elf_sibyl` 精灵女先知 | `pas_healing_touch` 已结构化；`pas_spirit_guard` 已结构化；`pas_elemental_action` 已结构化但 unlock25 | 无 | 测试角色：治愈之触和精灵加护已有真实 JSON 运行时测试覆盖。 |

## 第一批建议修复顺序

1. 已完成：`pas_healing_touch`、`pas_spirit_guard` 已迁移到 structured effects，覆盖治疗、净化/临时 DebuffNullify、行目标和 Sprite 计数器。
2. 已完成：`pas_block_seal` 不再修改 incoming `DamageCalculation`，改为 pending counter；`pas_counter_magic` 使用 Magical/Magic；`pas_wide_counter` 按锚点行展开。
3. `pas_formation_counter`、`pas_aid_cover`：处理会写日志但不改状态的资源/掩护类 no-op。
4. `pas_magic_barrier`、`pas_row_barrier`、`pas_holy_guard`：集中设计并实现魔法/异常/净化屏障，避免散落到多个特殊分支。
