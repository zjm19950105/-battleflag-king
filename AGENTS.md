# 战旗之王 - Codex 接手指南

最后更新：2026-05-14  
状态：Phase 1.4-A / A01-A16、策略系统、active/passive legacy tag-only allowlist 已清空；当前测试 395/395 通过，游戏项目 build 0 警告。

本文件给 Codex 使用；内容与 `CLAUDE.md` 保持同一套接手规则。不要把 `docs/next-ai-task-prompts.md` 当作新的待办清单，它现在只是 A01-A16 完成归档。

## 先做

1. `git status --short`
2. `dotnet test goddot-test\goddot-test.csproj --no-restore`
3. `dotnet build goddot\goddot.csproj --no-restore`
4. UI/Godot 改动后人工 F5 冒烟。

## 项目地图

```text
JSON 数据
  -> GameDataRepository
  -> BattleSetupService 创建 BattleUnit
  -> StrategyEvaluator / TargetSelector 选技能和目标
  -> BattleEngine 推进战斗状态机
  -> EventBus 发布战斗阶段事件（主动宣言/扣 AP 后/攻击前/命中前后）
  -> PassiveSkillProcessor 仲裁被动
  -> SkillEffectExecutor 执行结构化 effects
  -> DamageCalculator 结算伤害并记录 per-hit breakdown
  -> BattleLogEntry + OnLog 输出给测试/UI
```

## 改动时读这些

- 代码改动：先读相关 `.cs` 和对应测试。
- API / JSON / 枚举改动：读并更新 `docs/csharp-architecture.md`。
- 阶段判断：读 `progress.md` / `roadmap.md`。
- 历史坑：读 `docs/dev-mistakes.md`。

## 关键文件

| 责任 | 文件 |
| --- | --- |
| UI 和流程 | `goddot/Main.cs` |
| 单位创建/装备/默认策略 | `goddot/src/core/BattleSetupService.cs` |
| 战斗状态机 | `goddot/src/core/BattleEngine.cs` |
| 条件判断 | `goddot/src/Ai/ConditionEvaluator.cs` |
| 条件 UI 元数据 | `goddot/src/Ai/ConditionMeta.cs` |
| 目标选择 | `goddot/src/Ai/TargetSelector.cs` |
| 伤害计算 | `goddot/src/Pipeline/DamageCalculator.cs` |
| 技能效果执行 | `goddot/src/Skills/SkillEffectExecutor.cs` |
| 被动触发仲裁 | `goddot/src/Skills/PassiveSkillProcessor.cs` |
| 结构化战斗日志 | `goddot/src/core/BattleLogEntry.cs` |
| 测试沙盒 | `goddot/src/ui/TestSandboxView.cs` |
| 沙盒装备/策略表 | `goddot/src/ui/SandboxEquipmentPanel.cs`, `goddot/src/ui/SandboxStrategyTableView.cs` |
| 测试 | `goddot-test/*Test.cs` |

## 已完成的 A01-A16 加固

- 战斗终局统一：`StartBattle`、`StepBattle`、`StepOneAction` 共用 `EndBattle` 语义。
- BattleEnd 被动可以在 `BattleEndEvent` 后通过治疗、HP 改变或 pending action 影响最终胜负，最终结果会在 BattleEnd 后重算并锁定。
- `pas_rampage`、`pas_give_ap`、`pas_battle_horn` 已迁移到 structured `effects`，不再依赖 legacy tags。
- 掩护闭环：`DamageResult.ResolvedDefender` 是实际承伤者。
- 条件系统 canonical value：JSON 存稳定值，中文只做 UI。
- 条件/目标选择使用 CC 后兵种和运行时属性。
- 同时发动限制先检查条件，再占用 fired set。
- 删除空的 `ISkillEffect` 双轨。
- 增加金样例、数据契约、结构化日志、合规扫描测试。
- 追加修复：`EnemyClassExists=无`、`TeamSize` 相对阵营和包含边界、`ApPp highest PP`。
- 测试沙盒已支持战斗中/战前热插拔：阵容槽可清空、拖回角色池退下，右键可扣 10 HP、扣 50% HP、恢复 50% HP、设置气绝。
- 测试沙盒第一批 UI 修复已完成：右侧“当前说明”区域常驻战场状态，左侧详情不再显示冗余测试场景说明，重新点击同一角色会复用草稿装备/策略状态。
- `UnitState.Stunned` 已有真实战斗语义：轮到行动时跳过一次并恢复 `Normal`。
- 队列・状况条件已按原版语义修正：`前后排一列` 是纵列 1-4 / 2-5 / 3-6；`人数最多/最少一排`、`2/3体以上一排` 是前排/后排人数，不是纵列人数。
- 新增 `QueueStatusStrategyManualPlanTest`，把队列・状况手动测试方案自动化；后续其它策略分类建议照这个模式写验收测试。

## 已完成的技能系统迁移

- 主动技能 allowlist 已清空；passive legacy tag-only allowlist 也已清空，`goddot-test/DataContractTest.cs` 当前为 `new()`。
- G1 资源/命中/击杀收益已落地：`OnHitEffect`、`OnKillEffect`、`TransferResource`，覆盖 `act_pierce`、`act_kill_chain`、`act_hache`、`act_holy_blade`、`act_passive_steal`、`pas_formation_counter`、`pas_pursuit_slash`。
- G2/G3 异常、debuff、诅咒和常见 ranged/assist active 已迁移；`OnHitEffect` 支持 `chance`，主动攻击附带异常/debuff 必须命中后触发时要包在 `OnHitEffect`。
- `CritSeal` 方向已修正：攻击者身上有 `CritSeal` 时禁止暴击；防守者身上的 `CritSeal` 不保护自己。
- `StatusAilment.Stun` 会同步 `UnitState.Stunned`，轮到行动时跳过一次并恢复 `Normal`。
- `BuffManager` 已允许同一技能的不同属性纯 buff/debuff 共存；仍会去重同一技能 + 同一属性。
- 剩余 15 个 passive 已迁移到 structured effects：`pas_hundred_crit`、`pas_muscle_swelling`、`pas_calm_cover`、`pas_hawk_eye`、`pas_rapid_reload`、`pas_emergency_cover`、`pas_cut_grass`、`pas_fervor`、`pas_quick_reload`、`pas_pursuit_magic`、`pas_magic_blade`、`pas_quick_cast`、`pas_magic_barrier`、`pas_row_barrier`、`pas_berserk`。
- 新增/确认的 passive 机制：
  - `AugmentCurrentAction`：当前主动行动附加 calculation / on-hit / queued action / tags；用于 `pas_rapid_reload`、`pas_quick_reload`、`pas_magic_blade`、`pas_muscle_swelling`、`pas_cut_grass`、`pas_pursuit_magic`。
  - `AugmentOutgoingActions`：战斗长期阵营光环，当前用于 `pas_calm_cover`。
  - `ActionOrderPriority`、`ForcedTarget`、`RowAlliesOfTarget`、`MagicDamageNullify`、`AilmentNullify`、`DeathResist`、`ForceCrit`、`stackPolicy: "Stack"` 已落地。
- 装备资源同步已修复：装备改变 HP/AP/PP 后，`BattleUnit.SyncResourceCapsFromStats(...)` 会同步当前 HP、入场 AP/PP、被动 PP 预算和当前 AP/PP；HP=0 不会因换装复活。战斗中 AP/PP 硬上限固定为 `BattleUnit.ResourceCap == 4`，`MaxAp`/`MaxPp` 用于显示和恢复 clamp，始终是 4；被动装备预算使用 `PassivePpBudget`（装备后入场 PP），不要用固定 4。
- 新增/确认的 active 收尾机制：
  - `ColumnAlliesOfTarget`：以选中目标为锚点，选其同阵营同纵列存活单位；当前用于 `act_line_defense`。
  - `SkillPowerBonus` + `casterRow` / `requiresCasterFrontRow`：结构化固定威力加算和施法者前后排条件；当前用于 `act_frontline_heavy_bolt`。
  - `AmplifyDebuffs`：只放大已有负向纯 debuff，不影响 buff、不创建新 debuff；当前用于 `act_curse_disaster`。
- 追加战斗语义修复：
  - pending 追击/反击 PP 延迟到 `ProcessPendingActions()` 真正结算前消耗；若前一个 Counter/Pursuit 已击杀目标，后续 pending action 不发动、不扣 PP、不写伤害日志。
  - 主动相关被动已拆阶段：`BeforeActiveUseEvent` 只表示主动宣言；`AfterActiveCostEvent` 在扣 AP 后触发 `SelfOnActiveUse` / `AllyOnActiveUse`；`BeforeAttackCalculationEvent` 再触发 `SelfBeforeAttack` / `AllyBeforeAttack`。`蓄力行动`、`专注`、`主动礼物` 这类 RecoverAp 不再在扣 AP 前被 clamp 掉。
  - 多段攻击已有 per-hit breakdown：`DamageHitResult` 记录每 hit 的 miss/evade/crit/block/nullify 和伤害，`BattleLogHelper` 按真实 hit 明细汇总普通/暴击/格挡段。
- 最近提交：
  - `a9ef9b2` `Migrate structured skill effects baseline`
  - `d8b637c` `Migrate remaining active structured effects`

## 当前真实风险

- `Main.cs` 仍偏大，UI 和阶段流仍集中。
- `SkillEffectExecutor`、`PassiveSkillProcessor` 偏大，新增 effect 前先复用已有原子；新增通用机制必须补真实 JSON 测试。
- BattleEnd 语义已完成追加修复；当前剩余风险主要是手测暴露的 UI/日志/战斗语义一致性，而不是 legacy allowlist。
- 手测暴露的优先风险：
  - 命中率日志公式用户认为不对：当前显示类似 `技能100 + 命中 - 回避`，需要确认 `ActiveSkillData.HitRate` 是否应作为命中率加值、命中倍率、或只是与威力混淆；不要只改文案，先查数据字段和原版语义。
  - 简易 buff/debuff 数值不能降到负数：例如 `快速施法` 的 Crit -50 最低应显示/计算为 0，不能负数。需要统一在 `GetCurrentStat` 或 buff 结算层 clamp。
  - pending action 日志不清晰：反击/追击实际在 `[Counter]` / `[Pursuit]` 行结算，但“行动结束”先出现，导致玩家看不懂目标是否已经扣血/死亡。需要重排或增强日志，明确 pending action 的 HP 变化和最终死亡。
  - `DeathResist` 当前在日志里可能表现为 `HP:109->1(-108)` 这种“先补血再留 1”的错觉；实际是 `BattleLogHelper` 用 `CurrentHp + damage` 反推 hpBefore。需要让伤害结果携带真实扣血前 HP，避免动画/日志后续耦合出错。
  - 用户手测怀疑追击系列、空中狙击、列屏障、守护者、暴怒/反击等实现或日志有问题；自动测试通过，但必须用手测日志复现并区分“没实现”和“日志表达不清”。
- 新增战斗语义必须继续写 structured `effects`；不要恢复旧 `ISkillEffect` / `SkillEffectFactory` / `Skills/Effects/*` 双轨。
- 测试项目有 nullable/Godot source generator 警告；游戏项目 build 应保持 0 警告。
- `StrategyConditionCatalog` 里部分历史 ID 仍叫 `queue-most-column` / `queue-only-column-at-least-*`；这是兼容旧数据，代码注释已说明实际语义是“排”。不要把它们改回纵列。

## 编程方向

项目采用“数据驱动 + 面向对象领域模型 + 函数式规则管线 + 事件驱动被动仲裁”。不要走面向切面；不要把规则继续堆进 Godot UI；接口只在真实边界稳定后再加。
