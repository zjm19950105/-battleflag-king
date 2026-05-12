# C# 架构速查

最后更新：2026-05-13
用途：给新 AI 快速定位代码边界。这里不复述历史方案；以当前代码为准。

## 当前状态

- Godot 4 + C# / .NET 8。
- NUnit 测试项目：`goddot-test/goddot-test.csproj`。
- Phase 1.4-A / A01-A16 已完成。
- 当前测试基线：`dotnet test goddot-test\goddot-test.csproj --no-restore` 为 358/358 通过；游戏项目 `dotnet build goddot\goddot.csproj --no-restore` 应保持 0 警告。
- 当前核心目标：规则可信后再扩 6v6。

## 模块边界

| 模块 | 职责 | 关键文件 |
| --- | --- | --- |
| `data` | 加载 JSON，保存只读模板数据 | `GameDataRepository.cs`, `src/data/Models/*` |
| `core` | 运行时单位、战斗状态机、创建服务、结构化日志 | `BattleUnit.cs`, `BattleEngine.cs`, `BattleSetupService.cs`, `BattleLogEntry.cs` |
| `ai` | 策略条件、目标选择、条件目录和兼容 UI 元数据 | `StrategyConditionCatalog.cs`, `ConditionEvaluator.cs`, `TargetSelector.cs`, `ConditionMeta.cs` |
| `pipeline` | 伤害判定流水线 | `DamageCalculation.cs`, `DamageCalculator.cs`, `DamageResult.cs` |
| `skills` | 主动/被动包装、effects 执行、被动仲裁、pending action | `SkillEffectExecutor.cs`, `PassiveSkillProcessor.cs`, `PendingAction.cs` |
| `equipment` | 装备槽、buff、职业 trait | `EquipmentSlot.cs`, `BuffManager.cs`, `TraitApplier.cs` |
| `events` | 战斗阶段事件总线 | `EventBus.cs`, `BattleEvents.cs` |
| UI | Godot 页面、阶段导航、布阵/装备/被动/策略/战斗视图和通用控件辅助 | `goddot/Main.cs`, `src/ui/BattleUiFlowController.cs`, `src/ui/FormationSetupView.cs`, `src/ui/EquipmentSetupView.cs`, `src/ui/PassiveSetupView.cs`, `src/ui/StrategySetupView.cs`, `src/ui/BattleView.cs`, `src/ui/*Helper.cs` |
| 测试沙盒 UI | 快速拖阵容、调装备/策略、热插拔战斗状态、跑战斗日志 | `src/ui/TestSandboxView.cs`, `src/ui/SandboxEquipmentPanel.cs`, `src/ui/SandboxStrategyTableView.cs`, `src/ui/SandboxUnitHeaderView.cs` |

## 主流程

```text
GameDataRepository.LoadAll(dataPath)
  -> BattleSetupService.CreateUnit(...)
  -> BattleEngine.InitBattle()
  -> BattleEngine.StepOneAction()
     -> StrategyEvaluator 选技能
     -> TargetSelector 选目标
     -> BeforeActiveUseEvent / BeforeHitEvent
     -> PassiveSkillProcessor 修改 DamageCalculation 或入队 PendingAction
     -> SkillEffectExecutor 执行 active/passive structured effects
     -> DamageCalculator.Calculate(...)
     -> DamageResult.ResolvedDefender
     -> 扣血 / AfterHitEvent / OnKnockdownEvent
     -> BattleLogEntry
  -> EndBattle(...)
```

`StartBattle()` 和 `StepBattle()` 只是循环调用同一套 `StepOneAction()` 状态机；不要再写第二套战斗推进逻辑。

### UI 阶段导航

`Main.cs` 仍负责具体 `Phase_xxx` 页面内容、装备/被动/策略编辑控件和战斗按钮行为。`BattleUiFlowController` 负责通用 UI 流程壳：`GamePhase`、阶段历史栈、`Go(...)` / `GoBack(...)`、左右面板清空、按钮栏清空、显式 `AddBackButton()`，以及战斗页临时把 log 移到右侧面板再恢复。返回按钮由具体阶段/视图决定是否添加，避免战斗页和局部视图出现重复或危险回退。

`FormationSetupView` 负责角色池、1-6 号队伍槽、拖拽换位/放置和布阵确认按钮。确认后通过回调把当前 slot 数组交回 `Main.cs`；它不创建 `BattleUnit`，也不处理默认装备、策略或战斗执行。

`EquipmentSetupView` 负责装备槽下拉、装备/卸装 UI 操作和右侧属性/当前装备预览。`Main.cs` 只传入当前单位、`GameDataRepository` 和确认/跳过/返回回调；初始装备仍由 `BattleSetupService` 处理，装备合法性仍由 `EquipmentSlot` 处理。

`PassiveSetupView` 负责被动列表、PP 显示、被动装备/卸下按钮和被动触发条件选择。旧 UI 仍可写入 `BattleUnit.EquippedPassiveSkillIds` / `PassiveConditions`；原版风格策略栏应优先维护 `BattleUnit.PassiveStrategies`（顺序、`SkillId`、`Condition1`、`Condition2`），并保持 `EquippedPassiveSkillIds` 作为已装备白名单。

`StrategySetupView` 负责策略槽、技能下拉、条件级联 `OptionButton`、条件预览和技能说明。新角色初始只创建 1 条主动策略和 1 条被动策略；玩家后续通过 UI 自行新增。`SandboxStrategyTableView` 是沙盒内的同类编辑器。新 UI 优先消费 `StrategyConditionCatalog` / `StrategyConditionUiMapper.GetCatalogItems(...)` 的原版两栏目录；旧的 `ConditionMeta` 三段下拉只是兼容包装。所有条件对象必须通过 `StrategyConditionCatalog.BuildCondition(...)`、`StrategyConditionUiMapper.SaveCatalogSelection(...)`、`ConditionMeta.BuildCondition(...)` 或 `StrategyConditionUiMapper.SaveSelection(...)` 生成，保证 UI 中文选项转换为 canonical operator/value。`Mode1` / `Mode2` 保留 `Priority` / `Only` 语义：`Only` 硬过滤，`Priority` 只排序。

`BattleView` 负责战斗阶段的双方状态面板、`OnLog` 字符串日志、下一步/自动战斗按钮和 `BattleEngine.StepOneAction()` 调用。它接收 `BattleEngine` / `BattleContext`，战斗结束后只通过回调把 `BattleResult` 交回 `Main.cs`；战斗规则仍只属于 `BattleEngine` / skills / pipeline。

`TestSandboxView` 是测试专用页面：可拖拽我方/敌方阵容、编辑装备和策略、启动/重置/单步/自动测试战斗。它维护草稿单位数组和真实战斗 `BattleContext` 两套状态；开战前修改草稿，开战后右键调试优先修改真实战斗单位。阵容槽支持清空、拖回角色池退下；右键菜单支持扣 10 HP、扣 50% HP、恢复 50% HP、设置气绝。任何阵容热插拔会停止当前测试战斗并重建草稿单位。

新增页面时优先在 `Main.cs` 增加具体 `Phase_xxx`，并通过 `BattleUiFlowController.Go(...)` 进入；不要在页面方法里重新实现历史栈或通用清屏逻辑。

### BattleEnd 状态机

`EndBattle(preliminaryResult)` 分为两个内部阶段：

1. 进入结束阶段：只执行一次，清空行动队列，发布 `BattleEndEvent`。此时 `_battleEnded` 尚未锁定，事件里的 `Result` 是触发终局时的初判结果。
2. BattleEnd 后结算：`BattleEndEvent` 触发的被动可以治疗、改 HP 或入队 `PendingAction`；事件发布后必须调用 `ProcessPendingActions()`。之后才根据当前战场重新计算并锁定 `_finalResult` / `_battleEnded`。

最终胜负重算规则：

- 一方全灭时，优先使用存活判定。
- 双方仍存活且双方 AP 都耗尽时，使用当前 HP 比例判定。
- HP 比例相同或双方全灭时，允许 `Draw`。
- 如果 BattleEnd 被动让 AP 不再耗尽且双方仍存活，保留进入结束阶段时的初判结果，不恢复战斗循环。

## 数据文件

| 文件 | 内容 |
| --- | --- |
| `goddot/data/characters.json` | 角色模板、基础属性、兵种、CC、初始装备、技能池 |
| `goddot/data/active_skills.json` | 主动技能、AP、目标类型、结构化 effects |
| `goddot/data/passive_skills.json` | 被动技能、PP、触发时机、结构化 effects / legacy tags |
| `goddot/data/equipments.json` | 装备类别、属性、赋予技能、限制 |
| `goddot/data/strategy_presets.json` | 旧策略预设，条件值必须是 canonical value；新角色初始策略不再填满 8 条 |
| `goddot/data/class_display_names.json` | 职业显示名映射，用于未来原创化/本地化 |

## 重要枚举

```csharp
UnitClass: Infantry, Cavalry, Flying, Heavy, Scout, Archer, Mage, Elf, Beastman, Winged
EquipmentCategory: Sword, Axe, Spear, Bow, Staff, Shield, GreatShield, Accessory
SkillType: Physical, Magical, Assist, Heal, Debuff
AttackType: Melee, Ranged, Magic
TargetType: Self, SingleEnemy, SingleAlly, TwoEnemies, ThreeEnemies, FrontAndBack, Column, Row, AllEnemies, AllAllies
ConditionCategory: Position, UnitClass, Hp, ApPp, Status, AttackAttribute, TeamSize, SelfState, SelfHp, SelfApPp, EnemyClassExists, AttributeRank
PassiveTriggerTiming: BattleStart, SelfBeforeAttack, AllyBeforeAttack, AllyBeforeHit, SelfBeforeHit, SelfBeforeMeleeHit, SelfBeforePhysicalHit, AllyOnAttacked, SelfOnActiveUse, AllyOnActiveUse, AfterAction, BattleEnd, OnHit, OnBeingHit, OnKnockdown
PendingActionType: Counter, Pursuit, Preemptive, BattleEnd
```

新增枚举值必须同步数据、测试和本文档。

## 条件 DSL

原则：JSON 存内部稳定值；中文只在 `StrategyConditionCatalog` / `ConditionMeta` UI 层出现。

| 类别 | Value 约定 |
| --- | --- |
| `Position` | `front`, `back`, `front_and_back`, `row_units:N`, `row_unit_count`, 兼容旧值 `column_units:N` / `column_unit_count`, `daytime`, `nighttime` |
| `UnitClass` / `EnemyClassExists` | `UnitClass` 枚举名，如 `Cavalry` |
| `Status` | `buff`, `debuff`, `ailment`, `none`, `StatusAilment` 枚举名，或 `not:Poison` / `not:ailment` |
| `TeamSize` | `enemy:N` / `ally:N`，相对 subject 阵营 |
| `AttackAttribute` | `physical`, `magical`, `melee`, `ranged`, `row`, `column`, `front_and_back`, `all` |
| `AttributeRank` | `HP`（当前 HP，旧数据兼容）, `MaxHp`, `MaxAp`, `MaxPp`, `Str`, `Mag`, `Def`, `MDef`, `Spd`, `Hit`, `Eva`, `Crit`, `Block` |
| `Hp` / `SelfHp` | 0-1 比例，例：`0.5` |
| `ApPp` / `SelfApPp` 排序 | `AP` 或 `PP` |
| `ApPp` / `SelfApPp` 阈值 | `AP:N` / `PP:N`，例：`PP:1` |
| `SelfState` | `self`, `not_self`, `buff`, `debuff`, `action:N`, `charging`, `stunned`, `frozen`, `darkness` |

Operator 约定：

- 普通比较：`equals`, `not_equals`, `less_than`, `greater_than`
- 包含边界：`greater_or_equal`, `less_or_equal`
- 排序：`lowest`, `highest`
- HP 平均条件：`less_than_average`, `greater_than_average`

当前关键语义：

- `EnemyClassExists` 使用 subject 的相对敌方，不固定扫描 `_ctx.EnemyUnits`。
- `TeamSize enemy:N` 对玩家是敌方单位数，对敌人是玩家单位数。
- `UnitClass` 和 `EnemyClassExists` 使用 `BattleUnit.GetEffectiveClasses()`，CC 后兵种生效。
- `AttributeRank` 和目标排序使用运行时属性；非 HP 走 `BattleUnit.GetCurrentStat()`。
- `Position front_and_back` 是纵列语义：位置 1-4 / 2-5 / 3-6 同一列前后排都有人。
- `Position row_units:N` / `row_unit_count` 是前排/后排人数语义，对应原版“人数最多/最少一排”“2/3体以上一排”。早期内部 ID/值误写为 `column`，当前为兼容旧数据仍接受 `column_units:N` / `column_unit_count`，但不要把逻辑改回纵列人数。

## 目标选择

`TargetSelector` 的模型是：

```text
按技能确定目标阵营池
  -> 默认排序/前排阻挡
  -> Only 条件硬过滤
  -> Priority 条件排序
  -> Row / Column / FrontAndBack 在同阵营内扩展
```

注意：

- `Self` 以 caster 作为唯一候选，仍会执行自身条件。
- 近战地面攻击被前排阻挡。
- 远程、魔法、飞行、贯通/列攻击可越排。
- Row / Column / FrontAndBack 不允许从 `AllUnits` 里误捞友方。
- `ApPp highest/lowest` 必须按 `Value` 指定的 `AP` 或 `PP` 排序。
- `Only` 是硬过滤：没有合法目标时本条技能跳过。
- `Priority` 是偏好排序：没有匹配目标时回退默认合法目标，不会单独导致技能跳过。
- `Priority + Only` 先找交集，找不到再回退到满足 `Only` 的合法目标。
- `Priority + Priority` 先找交集；没有交集时通常先条件 2、再条件 1。`Position front/back` 作为原版特殊站位优先，在条件 1 或条件 2 中都拥有更高回退权重。
- `Position front_and_back` 表示同一列前后排都有存活单位，供贯通/列类技能做条件；它仍不能突破技能自身的可达性。
- `Position row_units:N` 表示目标所在前排/后排至少/至多有 N 个存活单位；`row_unit_count` 用于人数最多/最少一排排序。旧 `column_units:N` / `column_unit_count` 只是兼容别名。
- `AttackAttribute` 在目标选择阶段使用当前 `ActiveSkillData`，在被动/伤害阶段仍可从 `BattleContext.CurrentCalc` 读取。
- `SelfState action:N` 使用“下一次行动序号”判断；`BattleUnit.ActionCount` 在每次行动完成后递增。
- `SelfState stunned` / `UnitState.Stunned` 有真实战斗语义：轮到该单位时跳过一次行动并恢复 `Normal`。
- 原版目录里的“各兵种攻击”需要主动技能提供结构化特攻/目标兵种元数据；当前只有零散 legacy tags，因此目录中标记为 `NotImplemented` 且默认不暴露。

## 策略条件验收测试

后续验证其它策略分类时，优先把“手动测试方案”转成 NUnit，而不是人工逐条点 UI。当前范例：

- `QueueStatusStrategyManualPlanTest.cs`：覆盖队列・状况 10 条验收，包括仅/优先前后排、前后排一列、2/3体以上一排、人数最多/最少一排、白天/夜晚，以及策略1不满足时走策略2兜底。
- 相关底层测试仍在 `ConditionEvaluatorTest.cs`、`TargetSelectorTest.cs`、`StrategyConditionCatalogTest.cs`。

写新验收测试的推荐方式：构造最小 `BattleContext` + `GameDataRepository` + 测试技能，直接调用 `TargetSelector.SelectTargets(...)` / `StrategyEvaluator.Evaluate(...)` / `ConditionEvaluator.Evaluate(...)`。只有 UI 表现问题才需要人工 F5。

## 初始策略与被动策略行

`BattleSetupService.CreateUnit(...)` 当前初始规则：

- 主动：从 `BattleUnit.GetAvailableActiveSkillIds()` 中选择当前等级可用、`UnlockLevel` 最低、原技能池顺序最靠前的 1 个主动技能，创建 `Strategies[0]`，两个条件均为空。
- 被动：从 `BattleUnit.GetAvailablePassiveSkillIds()` 中选择当前等级可用、`UnlockLevel` 最低、`PpCost` 最低、原技能池顺序最靠前且 `PpCost <= MaxPp` 的 1 个被动技能，写入 `EquippedPassiveSkillIds` 和 `PassiveStrategies[0]`，两个条件均为空。

被动后端模型：

- `EquippedPassiveSkillIds` 仍是兼容旧 UI/旧测试的已装备列表，也用于 `GetUsedPp()` / `CanEquipPassive(...)`。
- `PassiveStrategies` 是原版风格 UI 的行模型：`SkillId`、`Condition1`、`Condition2`、`Mode1`、`Mode2`。UI 可按列表顺序显示被动优先级 1、2、3...
- `PassiveSkillProcessor` 通过 `BattleUnit.GetPassiveStrategiesInOrder()` 读取被动触发顺序；有 `PassiveStrategies` 时优先使用行顺序，并过滤掉未装备的技能；没有行模型时自动从 `EquippedPassiveSkillIds` + `PassiveConditions` 构造旧兼容行。
- 被动条件语义同样是无条件 / 单条件 / 双条件 AND。旧 `PassiveConditions[skillId]` 只映射为兼容行的 `Condition1`。

## 伤害与掩护

`DamageCalculation` 是可变中间状态，被动可以在 `BeforeHitEvent` 阶段修改它。关键字段：

- `ForceHit`, `ForceEvasion`, `ForceBlock`
- `SkillPowerMultiplier`, `DamageMultiplier`, `IgnoreDefenseRatio`
- `NullifyPhysicalDamage`, `NullifyMagicalDamage`
- `CoverTarget`, `CannotBeCovered`, `ResolvedDefender`
- `CannotBeBlocked`, `HitCount`, `CounterPowerBonus`
- `LandedHits`, `MissedHits`, `EvadedHits`, `NullifiedHits`

状态语义：`CritSeal` 挂在攻击者身上时禁止暴击；防守者身上的 `CritSeal` 不会保护其免受暴击。`BlockSeal` 仍挂在防守者身上时禁止其格挡。`StatusAilment.Stun` 会同步设置 `UnitState.Stunned`，使目标下次行动跳过一次并恢复 `Normal`。

`DamageCalculator` 负责把 `CoverTarget` 解析成 `ResolvedDefender`。之后所有实际承伤语义都使用 `DamageResult.ResolvedDefender`：扣 HP、AfterHit、OnKnockdown、结构化日志。
`ForceEvasion` 只回避多段攻击的第 1 hit；`MeleeHitNullify` 这类 temporal state 只无效下一段命中的近接物理伤害。多段攻击即使部分 hit 被回避/无效，剩余命中段仍要正常扣血。
带 `RangedCover` tag 的被动是额外上下文限制：只允许在远程物理 `BeforeHit` 中触发；结构化效果仍用 `CoverAlly` + `ModifyDamageCalc.NullifyPhysicalDamage` 表达实际掩护/无效化。
同一技能可以同时挂多个不同属性的纯 buff/debuff；`BuffManager` 只把“同一技能 + 同一属性”的纯 buff/debuff 视作重复。

`ModifyDamageCalc` 可带条件参数限制本条伤害修正是否生效：`targetClass` / `targetClasses` 按当前声明目标兵种过滤，`targetHasDebuff` 要求目标已有纯 debuff，`casterHasBuff` 要求施法者已有纯 buff，`casterHpRatioMin` / `casterHpRatioMax` 按施法者当前 HP 比例过滤，`casterRow` (`Front` / `Back`) / `requiresCasterFrontRow` 按施法者当前前后排过滤。条件不满足时整条 calculation effect 跳过；这用于满血威力提升、低血威力提升、前排发动威力提升、目标已有 debuff 伤害提升、自身有 buff 伤害提升、兵种特攻无视防御/不可格挡等 structured effects。`SkillPowerBonus` 会加到 `DamageCalculation.EffectivePower`，与 `SkillPowerMultiplier` 不同，它表达固定威力值加算。

## Effects 执行

唯一有效执行路径：

- 主动技能：`BattleEngine.ExecuteSkillAgainstTargets()` 调 `SkillEffectExecutor`。
- 被动技能：`PassiveSkillProcessor` 判断时机、条件、PP、同时发动限制；能委派的效果委派给 `SkillEffectExecutor`。

不要重新引入旧的 `ISkillEffect.Apply()` / `SkillEffectFactory` / `Skills/Effects/*`。

`SkillType.Assist` 的自施辅助主动技（例如 `act_great_shield` / `act_formation_breaker`）把 `AddBuff`、`RecoverPp`、`HealRatio` 直接写在顶层 `effects`。当前非 Heal 的 Assist 仍会进入一次 0 威力命中/伤害流程；新增测试应断言只影响自身资源/面板，不对敌方造成伤害或附加副作用。

`SkillType.Debuff` 的纯妨害主动技（例如 `act_passive_curse` / `act_attack_curse` / `act_defense_curse` / `act_curse_disaster`）应把 `PpDamage`、`AddDebuff`、`AmplifyDebuffs`、`StatusAilment` 等直接写在顶层 `effects`。这些 action effects 在 `DamageCalculator` 命中/回避判定前执行，不受随机命中影响；只有“伤害命中后才附加”的异常/减益才用 `OnHitEffect` 包裹。

`SelfOnActiveUse` 被动在 `BeforeActiveUseEvent` 执行，早于主动 AP 扣费、主动 action effects 和伤害计算；因此 `RecoverAp` 可作为当前主动行动的 AP 回补，`oneTime` 命中等 buff 会参与当前主动伤害计算，并在该单位本次行动完成后由 `CleanupAfterAction` 清理。

常见 target DSL：

```text
Self, Caster, Target, Defender, Attacker,
AllTargets, AllAllies, Allies, AllEnemies, Enemies,
RowAllies, FrontRowAllies, BackRowAllies, ColumnAllies,
ColumnAlliesOfTarget,
LowestHpAlly, HighestHpAlly, RandomAlly
```

`ColumnAllies` 以施法者位置为列锚点；`ColumnAlliesOfTarget` 以 `calculation.Defender` 或当前 action target 为列锚点，选择锚点同阵营、同列的存活单位。

未知 target 当前回退到显式传入 targets。`targetClass` / `targetClasses` 会在 target DSL 选完后按 `UnitClass` 过滤，`maxTargets` 可限制最终目标数量。修改这个行为必须改测试。

BattleEnd 常用 structured effects：

- `BattleEndAttack`: 入队 `PendingActionType.BattleEnd`，参数同 pending attack：`target`, `maxTargets`, `power`, `hitRate`, `damageType`, `attackType`, `targetType`, `tags`, `ignoreDefenseRatio`, `ignoreDefenseTargetClass`。
- `PendingAttack`: 通用入队攻击，额外用 `pendingActionType` 指定 `Counter` / `Pursuit` / `Preemptive` / `BattleEnd`。
- Pending action 的 `targetType: Row` / `Column` 在 `BattleEngine.ProcessPendingActions()` 里以已入队目标为锚点展开同阵营同排/同列目标；`ignoreDefenseRatio` 只修改 pending action 自己的 `DamageCalculation`，如果同时设置 `ignoreDefenseTargetClass`，只在实际目标拥有该 `UnitClass` 时生效。
- `RecoverHp` / `RecoverAp` / `RecoverPp`: 直接恢复资源，按 target DSL 选目标。
- `OnHitEffect`: 伤害判定命中后执行嵌套 `effects`；可选 `requireDamage`、`requireUnblocked` / `requireNotBlocked`、`chance` 进一步限制。`chance` 支持 `0..1` 比例或 `0..100` 百分比，表示命中后整组 nested effects 的触发率。主动攻击和 pending attack 共用 `SkillEffectExecutor.ExecutePostDamageEffects(...)`。
- `OnKillEffect`: 本次伤害击倒实际承伤者后执行嵌套 `effects`；击倒判断使用 `DamageResult.ResolvedDefender` 对应的扣血结果。
- `TransferResource`: 在选中的 `from` 与 `to` 目标之间转移资源，参数包括 `resource` (`AP` / `PP`)、`amount`（整数或 `"All"`）、`from`、`to`。当前用于命中后偷取 PP。
- `HealRatio`: 按最大 HP 比例治疗；可选 `lowHpThreshold` + `lowHpMultiplier` 表示低血量目标治疗倍率。
- `AmplifyDebuffs`: 放大选中目标身上已有的纯 debuff（`IsPureBuffOrDebuff` 且 `Ratio < 0` 或 `FlatAmount < 0`），只乘以负向数值；不影响正向 buff，不创建新 debuff。参数：`target`、`multiplier`。
- `TemporalMark`: 给目标添加一次性/限时标记。当前 `DebuffNullify` 会在下一次结构化 `AddDebuff` 时被消费并阻止该 debuff。

## 被动与同时发动限制

`PassiveSkillProcessor.ProcessTiming()` 顺序必须保持：

```text
按速度排序；同速组每次触发时随机打散
  -> skill timing 匹配
  -> PP 可用
  -> 玩家设置的被动条件通过
  -> 再占用 simultaneous fired set
  -> 消耗 PP
  -> 执行效果
```

条件失败不能占用同时发动限制。

## 结构化日志

`BattleEngine` 同时保留：

- `OnLog`: UI 旧字符串流。
- `OnBattleLogEntry`: 结构化日志回调。
- `BattleLogEntries`: 已收集日志。

`BattleLogEntry` 当前字段：`Turn`, `ActorId`, `SkillId`, `TargetIds`, `Damage`, `Flags`, `Text`。

当前覆盖：主动攻击、pending passive attack、BattleEnd。回放 UI 尚未实现。

## 当前风险

- active legacy tag-only allowlist 已清空；`act_line_defense`、`act_frontline_heavy_bolt`、`act_curse_disaster` 也已迁移到 structured effects。
- `pas_rampage`、`pas_give_ap`、`pas_battle_horn`、`pas_concentration`、`pas_curse_swamp`、`pas_rapid_order` 已迁移到 structured effects，不再依赖 legacy tags；剩余 legacy tag-only 技能集中在 passive allowlist，仍需按优先级逐步迁移。
- `pas_rapid_reload` 不要做 skill-id 特判；下一步建议实现通用 `AugmentCurrentAction`，把 `SelfBeforeAttack` / `AllyBeforeAttack` 被动的 calculation effects / on-hit effects 附着到当前主动行动。
- Godot UI 尚无自动化冒烟；当前已修正战斗日志初始化后被清空、返回按钮重复添加两个手测前风险点，仍需要人工 F5 覆盖启动、选队、进战斗、下一步、结果页。
- `Main.cs` 已抽出阶段导航、通用面板/按钮流程和拖拽布阵视图，但具体装备、被动、策略 UI 仍偏大；后续拆分要继续保持不改变流程顺序。
- `SkillEffectExecutor.cs`、`PassiveSkillProcessor.cs` 偏大，下一步是有测试地拆分。
- 测试项目有 nullable/Godot source generator 警告；游戏项目 build 应保持 0 警告。

## 验证命令

```powershell
dotnet test goddot-test\goddot-test.csproj --no-restore
dotnet build goddot\goddot.csproj --no-restore
```
