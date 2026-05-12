# 战旗之王 - AI 接手指南

最后更新：2026-05-12  
状态：Phase 1.4-A / A01-A16、BattleEnd 追加修复、测试沙盒热插拔调试、队列・状况“排”语义修正已完成，当前测试 203/203 通过。

这是新 AI 的唯一必读入口。不要先读完整历史文档；先用本文建立地图，再按任务读代码和测试。

## 项目一句话

Godot 4 + C# 的“战前编程策略 -> 自动战斗”原型。当前重点不是扩内容，而是让战斗规则可信、可测、可继续扩到 6v6。

## 接手后先做

1. 查看工作区：`git status --short`
2. 跑测试：`dotnet test goddot-test\goddot-test.csproj --no-restore`
3. 跑游戏项目编译：`dotnet build goddot\goddot.csproj --no-restore`
4. 如果改 UI 或 Godot 场景，再用 Godot F5 做人工冒烟。

## 当前不要误读的文档

- `docs/next-ai-task-prompts.md` 是 A01-A16 完成归档，不再是待领取任务清单。
- `docs/codex-architecture-diagnosis.md` 是历史诊断，核心建议已执行到 A01-A16。
- `docs/被动技能实现融入现有框架.md` 是早期方案，不代表当前实现。

## 真正需要读的文档

- 改代码前：读相关 `.cs` 文件和对应测试。
- 改 API / JSON 语义 / 枚举：再读 `docs/csharp-architecture.md`。
- 看阶段状态：读 `progress.md` 和 `roadmap.md`。
- 查容易犯错的规则：读 `docs/dev-mistakes.md`。

## 核心数据流

```text
JSON 数据
  -> GameDataRepository
  -> BattleSetupService 创建 BattleUnit
  -> StrategyEvaluator / TargetSelector 选技能和目标
  -> BattleEngine 推进战斗状态机
  -> EventBus 发布战斗阶段事件
  -> PassiveSkillProcessor 仲裁被动
  -> SkillEffectExecutor 执行结构化 effects
  -> DamageCalculator 结算伤害
  -> BattleLogEntry + OnLog 输出给测试/UI
```

## 关键文件

| 责任 | 文件 |
| --- | --- |
| Godot UI 和流程 | `goddot/Main.cs` |
| 单位创建、初始装备、默认策略 | `goddot/src/core/BattleSetupService.cs` |
| 战斗状态机 | `goddot/src/core/BattleEngine.cs` |
| 运行时单位 | `goddot/src/core/BattleUnit.cs` |
| 条件判断 | `goddot/src/Ai/ConditionEvaluator.cs` |
| 条件 UI 元数据 | `goddot/src/Ai/ConditionMeta.cs` |
| 目标选择 | `goddot/src/Ai/TargetSelector.cs` |
| 伤害计算 | `goddot/src/Pipeline/DamageCalculator.cs` |
| 技能效果执行 | `goddot/src/Skills/SkillEffectExecutor.cs` |
| 被动触发仲裁 | `goddot/src/Skills/PassiveSkillProcessor.cs` |
| 结构化战斗日志 | `goddot/src/core/BattleLogEntry.cs` |
| 测试沙盒 | `goddot/src/ui/TestSandboxView.cs` |
| 沙盒装备/策略表 | `goddot/src/ui/SandboxEquipmentPanel.cs`, `goddot/src/ui/SandboxStrategyTableView.cs` |
| 核心回归测试 | `goddot-test/*Test.cs` |

## 已完成的规则加固

- `StartBattle` / `StepBattle` / `StepOneAction` 共用终局语义，`BattleEndEvent` 只发一次。
- BattleEnd 被动可以在 `BattleEndEvent` 后通过治疗、HP 改变或 pending action 影响最终胜负，最终结果会在 BattleEnd 后重算并锁定。
- `pas_rampage`、`pas_give_ap`、`pas_battle_horn` 已迁移到 structured `effects`，不再依赖 legacy tags。
- `CoverTarget` 已用 `DamageResult.ResolvedDefender` 闭环：扣血、AfterHit、OnKnockdown、日志都指向实际承伤者。
- 条件系统使用 canonical value：JSON 存英文枚举/稳定值，中文只属于 UI。
- `EnemyClassExists`、`UnitClass` 使用运行时有效兵种，支持 CC 后兵种。
- `AttributeRank` / 目标排序使用运行时属性，装备和 buff 生效。
- 同时发动限制改为条件通过后才占用 fired set。
- 空的 `ISkillEffect` / `SkillEffectFactory` / `Skills/Effects/*` 双轨已删除。
- 已建立金样例职业组、数据契约、结构化日志、合规扫描测试。
- 2026-05-08 追加修复：`EnemyClassExists=无`、`TeamSize` 相对阵营与“以上/以下”包含边界、`ApPp highest PP` 排序。
- 2026-05-12：测试沙盒支持战斗中/战前热插拔：阵容槽清空、拖回角色池退下、右键扣 10 HP、扣 50% HP、恢复 50% HP、设置气绝。`UnitState.Stunned` 现在会跳过一次行动后恢复 `Normal`。
- 2026-05-12：队列・状况条件已按原版语义修正：`前后排一列` 是纵列 1-4 / 2-5 / 3-6；`人数最多/最少一排`、`2/3体以上一排` 是前排/后排人数。`QueueStatusStrategyManualPlanTest` 覆盖对应手动方案。

## 当前真实风险

- `Main.cs` 仍偏大，虽然已抽出 `BattleSetupService`，但 UI、阶段流、控件构造仍集中。
- `SkillEffectExecutor` 和 `PassiveSkillProcessor` 仍偏大；新增 effect 前必须先查能否复用现有原子。
- BattleEnd 语义已完成追加修复；当前剩余风险主要是继续收敛其它 legacy tag-only 技能，而不是 BattleEnd 关键被动。
- 主动/被动技能仍有一批 legacy tag-only 白名单；新增战斗语义必须写 structured `effects`。
- `dotnet test` 当前会显示测试项目 nullable/Godot source generator 警告；游戏项目 `dotnet build goddot\goddot.csproj --no-restore` 应保持 0 警告。
- Godot UI 缺少自动化截图/冒烟测试，UI 改动要人工 F5。
- `StrategyConditionCatalog` 中保留了历史 ID `queue-most-column` / `queue-only-column-at-least-*` 以兼容旧数据；显示和实际语义已经是“排”。不要因 ID 名称把逻辑改回纵列。

## 编程范式

本项目不是纯面向对象，也不是纯面向过程。正确方向是：

- 数据驱动：角色、技能、装备、策略条件优先写 JSON。
- 面向对象：`BattleUnit`、`BattleEngine`、`EquipmentSlot` 表示有状态领域对象。
- 函数式/管线式：条件判断、目标选择、伤害计算尽量做成输入明确、输出明确的纯规则。
- 事件驱动：被动技能通过 `EventBus` 在战斗阶段介入。
- 少量接口：只在稳定边界需要替换时加接口；不要为了“抽象”制造空层。

不要走面向切面；不要把规则继续堆进 Godot UI。新增规则的理想落点通常是 JSON effect + `SkillEffectExecutor` 原子 + 测试。

## 修改守则

- 先补/改测试，再改实现。
- 任何 C# API、枚举、数据 schema 变动，都同步 `docs/csharp-architecture.md`。
- JSON 条件值不要写中文运行时值。
- 不要重新引入空 `Apply()` 效果类。
- 不要直接扩 6v6、批量加角色、批量补技能，除非当前任务就是这个。
