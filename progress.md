# 开发进度
最后更新：2026-05-09

## 当前状态

Phase 1.4-A 核心战斗规则可信化已完成，A01-A16 不再是待办；详情见 `docs/next-ai-task-prompts.md` 的完成归档。

当前验证：
- `dotnet test goddot-test\goddot-test.csproj --no-restore`：135/135 通过。
- `dotnet build goddot\goddot.csproj --no-restore`：游戏项目 0 warning / 0 error。

## 近期完成

- 统一 `StartBattle` / `StepBattle` / `StepOneAction` 终局路径。
- 修复掩护实际承伤者：`DamageResult.ResolvedDefender` 成为扣血、事件、日志统一来源。
- 条件系统迁移到 canonical value，中文仅用于 UI。
- 条件和目标排序使用 CC 后兵种与运行时属性。
- 同时发动限制改为条件通过后再占用。
- 删除空的 `ISkillEffect` 双轨。
- 增加金样例职业组、数据契约、结构化战斗日志、合规扫描测试。
- 抽出 `BattleSetupService`，降低 `Main.cs` 的纯逻辑负担。
- BattleEnd 追加修复：`BattleEndEvent` 只发一次，事件后的治疗、HP 改变和 pending action 会参与最终胜负重算。
- `pas_rampage`、`pas_give_ap`、`pas_battle_horn` 已迁移到 structured `effects`，不再依赖 legacy tags。
- 修复 Godot UI 冒烟前的两个明显问题：战斗日志不再在 `InitBattle()` 后被清空，返回按钮不再由 flow 和局部视图重复添加。

## 仍需注意

- `Main.cs` 仍偏大，下一刀应继续拆 UI 阶段流或视图构造，而不是继续堆逻辑。
- `SkillEffectExecutor` 和 `PassiveSkillProcessor` 偏大，拆分前必须先有覆盖测试；下一步适合按 effect family 拆 helper。
- 仍有 legacy tag-only 技能白名单；新增战斗语义必须写 effects。
- Godot UI 需要人工 F5 冒烟，尚无自动化 UI 验证。

## 下一步建议

1. Godot F5 人工冒烟：启动、1v1/3v3 选队、预设/自定义敌人、进入战斗、下一步、自动战斗、结果页。
2. 给 Godot UI 建最小冒烟验证方案。
3. 继续拆 `Main.cs` 的 UI 阶段流。
4. 按 effect family 收敛 `SkillEffectExecutor` / `PassiveSkillProcessor`，不新增抽象接口。
5. 进入 6v6 前，用金样例职业组做稳定整场战斗快照测试。
