# 战旗之王 - Codex 接手指南

最后更新：2026-05-12  
状态：Phase 1.4-A / A01-A16、BattleEnd 追加修复、测试沙盒热插拔调试、队列・状况“排”语义修正已完成，当前测试 203/203 通过。

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
  -> EventBus 发布战斗阶段事件
  -> PassiveSkillProcessor 仲裁被动
  -> SkillEffectExecutor 执行结构化 effects
  -> DamageCalculator 结算伤害
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
- `UnitState.Stunned` 已有真实战斗语义：轮到行动时跳过一次并恢复 `Normal`。
- 队列・状况条件已按原版语义修正：`前后排一列` 是纵列 1-4 / 2-5 / 3-6；`人数最多/最少一排`、`2/3体以上一排` 是前排/后排人数，不是纵列人数。
- 新增 `QueueStatusStrategyManualPlanTest`，把队列・状况手动测试方案自动化；后续其它策略分类建议照这个模式写验收测试。

## 当前真实风险

- `Main.cs` 仍偏大，UI 和阶段流仍集中。
- `SkillEffectExecutor`、`PassiveSkillProcessor` 偏大，新增 effect 前先复用已有原子。
- BattleEnd 语义已完成追加修复；当前剩余风险主要是继续收敛其它 legacy tag-only 技能，而不是 BattleEnd 关键被动。
- 仍有 legacy tag-only 技能白名单；新增战斗语义必须写 structured `effects`。
- 测试项目有 nullable/Godot source generator 警告；游戏项目 build 应保持 0 警告。
- `StrategyConditionCatalog` 里部分历史 ID 仍叫 `queue-most-column` / `queue-only-column-at-least-*`；这是兼容旧数据，代码注释已说明实际语义是“排”。不要把它们改回纵列。

## 编程方向

项目采用“数据驱动 + 面向对象领域模型 + 函数式规则管线 + 事件驱动被动仲裁”。不要走面向切面；不要把规则继续堆进 Godot UI；接口只在真实边界稳定后再加。
