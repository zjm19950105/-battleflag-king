# Codex 架构诊断与下一阶段方案

> 日期：2026-05-07  
> 范围：参考 `C:\Users\ASUS\Music\圣兽之王资料整理\有用的资料\README.md` 中的核心资料，结合当前 Godot/C# 项目代码与测试。

## 总结

当前项目不需要推倒重来。方向是对的：`BattleEngine`、`EventBus`、`DamageCalculation`、`TargetSelector`、`PassiveSkillProcessor` 这些骨架基本符合圣兽之王式自动战斗的系统形态。

但现在不能继续优先扩角色、扩 6v6 或批量填技能。真正的下一步应该是冻结内容扩张，先做一次“核心战斗规则层中改”。原因是当前代码已经出现数据很丰富、实际执行不完整的断层：主动技能效果没有统一执行，被动规则集中在巨型处理器里，战斗推进有两套路径，目标选择和条件系统还有规则漏洞。

## 从参考资料得到的核心规则

圣兽之王式战斗的核心不是“多人站位 + 自动攻击”，而是以下规则系统叠加：

1. 战斗阶段必须显式建模：战斗开始、主动行动、攻击方被动、防守方被动、命中/格挡/回避/掩护/伤害、行动后、战斗结束。
2. 速度不仅决定主动行动顺序，也决定被动触发优先权。
3. `同時発動制限` 不是全局锁，而是分阶段、分阵营的限制。
4. 作战策略的本质是目标列表重写：默认目标列表 -> 条件1 -> 条件2 -> 选择最高优先级目标。
5. “优先”不是发动条件；“仅/限定”才会让技能跳过。
6. OR 条件不是单条策略内实现，而是多行同技能实现。
7. 近接、远隔、飞行、前后列、Row、Column、All 的目标搜索边界必须严格区分。
8. 命中、黑暗、必中、回避技能、格挡、掩护、多段攻击必须是独立判定层，不能揉进一个伤害公式。
9. 格挡只减物理威力部分，混合攻击必须拆分物理/魔法伤害。
10. 战斗结束前仍可能触发被动或终结攻击，不能简单在 HP 归零时立刻终止全部流程。

## 当前代码的关键问题

### 1. 主动技能效果管线没有真正接上

数据里主动技能已经写了 `effects`，例如 `ModifyDamageCalc`、`AddBuff`、`ModifyCounter`、`HitCount`。但 `BattleEngine.ExecuteSkillAgainstTargets()` 目前主要只创建 `DamageCalculation` 并调用 `DamageCalculator`，没有统一执行主动技能效果。

同时，`DamageEffect`、`BuffEffect` 等 `ISkillEffect` 实现基本是空的。这会导致“数据看起来完成，实战不生效”。

典型影响：
- `act_meteor_slash` 写了 `HitCount: 9`，但当前主动路径不会可靠应用它。
- 主动技能里的 `AddBuff`、`ModifyCounter`、异常赋予容易被静默忽略。

### 2. 被动处理器已经变成规则黑洞

`PassiveSkillProcessor` 同时负责触发时机、条件检查、PP 消耗、同时发动限制、效果执行、反击入队、掩护修改、TemporalState、legacy tag 兼容。

短期能跑，长期会变成每个技能一个补丁。

一个具体风险：同时发动限制在条件检查前就可能占用 fired set。高速单位条件不满足时，可能错误地挡掉后续真正满足条件的单位。

### 3. 战斗推进有两套路径

`StartBattle/StepBattle` 和 `StepOneAction` 都能推进战斗，但清理 buff、终局处理、`BattleEndEvent` 触发不完全一致。

UI 很可能使用 `StepOneAction`，这意味着战斗结束被动、终结攻击、战斗结束前处理可能和完整自动战斗路径不一致。

### 4. 目标选择存在阵营与形状边界风险

`GetPiercingTargets`、`GetColumnTargets`、`GetRowTargets` 使用 `_ctx.AllUnits` 二次捞目标，可能把友方也纳入 Row/Column/贯通目标。

正确模型应该是：

1. 先确定目标阵营候选池。
2. 应用默认前排/远程/飞行规则。
3. 应用“仅/优先”条件。
4. 根据选中的 anchor，在同一阵营池内扩展 Row/Column/FrontAndBack。

### 5. 条件系统过于宽松

很多条件遇到 null、未知值、未知枚举时直接返回 true。原型阶段方便，但可编程战斗里会隐藏数据错误。

已发现风险：
- `UnitClass` 判断用 `Data.Classes`，没有用 CC 后的 `GetEffectiveClasses()`。
- `EnemyClassExists` 固定扫描 `_ctx.EnemyUnits`，不是相对 subject 的敌方。
- `strategy_presets.json` 里 `SelfHp greater_than value=50`，但代码按 0-1 比例判断，可能永远不满足。
- 装备 stat key 混用：`hit`、`block_rate`、`mag_def` 与代码读取的 `Hit`、`Block`、`MDef` 不一致。

### 6. 伤害管线语义不完整

已发现风险：
- `CoverTarget` 只影响防御/格挡计算，最终扣血仍可能扣在原始 target 上。
- `ActiveSkill.HasMixedDamage` 永远是 false，混合伤害数据无法表达。
- 魔法攻防仍可能错误叠加 `phys_atk/phys_def`。
- 主动技能的异常赋予和 buff/debuff 生命周期没有统一闭环。
- 多段攻击测试存在随机暴击/格挡导致的非确定性。

### 7. `Main.cs` 太大

`Main.cs` 约 1332 行，同时负责 UI、流程状态机、单位创建、默认策略、装备配置、被动配置、策略编辑和战斗启动。它现在是可运行原型，但不适合继续承载大规模规则扩展。

## 当前测试判断

当前测试大体覆盖：
- BuffManager
- ConditionEvaluator
- DamageCalculator
- EquipmentSlot
- TargetSelector

主要缺口：
- 没有真实 JSON 加载与跨表引用测试。
- 没有主动技能 effects 生效测试。
- 被动结构化效果覆盖不足。
- Row/Column 是否误伤友方没有测试。
- 战斗结束被动、终结攻击、StepOneAction 与 StartBattle 一致性没有测试。

当前红测要分开看：

1. `TargetSelectorTest` 里两个红测的断言参数写反，业务逻辑可能不是这两个用例显示的那样坏。
2. `DamageCalculatorTest.多段攻击_每hit独立判定` 有随机性，默认测试单位有 5% 暴击和 3% 格挡，导致伤害可能超过测试假定上限。

这说明“先让测试稳定可信”本身就是第一阶段任务。

## 建议路线

### Phase A：测试地基修正

目标：让测试能真正代表规则。

任务：
- 修正 `TargetSelectorTest` 写反的断言。
- 让多段攻击测试禁用暴击和格挡，或用可注入 RNG。
- 新增 `GameDataRepository.LoadAll` 真实数据加载测试。
- 新增数据契约测试：ID 无重复、引用不断链、装备 stat key 白名单、策略条件单位合法。
- 新增 Row/Column/FrontAndBack 不误伤友方测试。

完成标准：
- `dotnet test` 稳定 54/54 或新增测试后全部通过。
- 红测不再包含随机失败。

### Phase B：统一主动/被动效果执行

目标：让 JSON 里的技能效果真的进入战斗。

长期原则：内容扩展不能驱动代码扩展。新增角色和新增技能应默认只改 JSON，
通过组合已有 `effectType` 表达规则；只有出现已有原子无法表达的全新规则语义时，
才新增一个通用 `effectType`，并同时补测试。禁止继续增加“只有 tag、没有结构化
effects 执行语义”的新技能，否则每批角色都会重新制造一次代码补丁。

建议新增一个规则服务，例如：
- `SkillEffectExecutor`
- `EffectExecutionContext`
- `EffectTiming`

它负责执行通用 effect：
- `ModifyDamageCalc`
- `AddBuff`
- `RecoverAp`
- `RecoverPp`
- `RecoverHp`
- `StatusAilment`
- `ModifyCounter`
- `ConsumeCounter`
- `CoverAlly`
- `CounterAttack`
- `TemporalMark`

主动技能和被动技能都通过它执行。`PassiveSkillProcessor` 只保留“谁在什么时机能发动”的仲裁职责。

完成标准：
- 主动 `HitCount`、`ForceHit`、`AddBuff` 有测试证明生效。
- 被动 `RecoverAp`、`CoverAlly`、`CounterAttack` 有测试证明生效。

### Phase C：统一战斗推进状态机

目标：UI 单步和完整自动战斗走同一套核心流程。

任务：
- 让 `StepOneAction`、`StepBattle`、`StartBattle` 共享同一条内部状态机。
- 所有终局都走 `EndBattle()`。
- 战斗结束被动在胜负判定前有明确时机。
- buff 清理时机只定义在一个地方。

完成标准：
- UI 单步与自动跑完整战斗在同一 fixture 下结果一致。
- `BattleEndEvent` 在两种路径都触发。

### Phase D：目标选择与条件系统硬化

目标：实现圣兽之王式作战配置的可信核心。

任务：
- `TargetSelector` 改成“阵营池 + 条件重写 + 形状扩展”模型。
- 补双优先、优先前/后排特殊规则。
- `ConditionEvaluator` 使用相对阵营和 `GetEffectiveClasses()`。
- 未知枚举、未知 operator、坏 value 不再静默 true，至少记录错误。
- 统一 HP 条件使用 0-1 还是 0-100，建议 UI/JSON 用百分比，内部转换为比例。

完成标准：
- 前排阻挡、远程越排、飞行攻击、Row、Column、FrontAndBack、All 的测试覆盖。
- “优先”和“仅”规则测试覆盖。

### Phase E：再决定内容扩张

在 A-D 完成前，不建议扩充角色或继续批量填技能。

A-D 完成后，下一步不是盲目扩角色，而是做“金样例职业组”：

- 1 个近战物理输出
- 1 个远程弓手
- 1 个飞行单位
- 1 个重装/盾单位
- 1 个魔法单位
- 1 个回复/辅助单位
- 1 个反击/追击单位
- 1 个掩护单位

每个职业只保留 2-3 个能代表机制的主动/被动技能，用测试锁死。等这组能打出可信战斗，再扩到 18 角色完整内容和 6v6。

## 结论

下一步不是扩 6v6，也不是继续扩角色。

下一步应该是：

1. 修测试可信度。
2. 接通主动技能 effect 管线。
3. 拆出统一 effect executor。
4. 统一战斗推进状态机。
5. 硬化目标选择和条件系统。
6. 用金样例职业组验证规则。
7. 最后再扩 6v6 与内容规模。

这样项目会从“能演示的原型”变成“能继续长大的战斗系统”。
