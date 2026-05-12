# 开发易错点

最后更新：2026-05-11

这个文件只保留会反复伤人的规则。历史长文已删减；需要当前架构请读 `docs/csharp-architecture.md`。

## AI 接手错误

- 不要把 `docs/next-ai-task-prompts.md` 当待办。A01-A16 已完成。
- 不要看到“Phase 1.5 / 6v6”就直接扩人数；先处理 `roadmap.md` 的 Phase 1.4-B。
- 不要只看文档不看代码。文档是地图，代码和测试是事实。

## 数据错误

- JSON 条件值必须是 canonical value，不写中文运行时值。
- 枚举值必须来自 C# 枚举，不允许凭语义创造。
- 改 JSON 后必须跑测试；改 schema 后同步 `docs/csharp-architecture.md`。
- 新增战斗语义必须写 structured `effects`，不能只加 Tags。
- legacy tag-only 技能只能存在于 `DataContractTest` 的显式白名单。

## 战斗规则错误

- `StartBattle`、`StepBattle`、`StepOneAction` 必须共享战斗推进语义。
- 终局必须走 `EndBattle`，`BattleEndEvent` 只能发一次。
- 掩护后实际承伤者是 `DamageResult.ResolvedDefender`，不要用原 target 扣血。
- 条件失败不能占用同时发动限制。
- `TeamSize enemy:N` / `ally:N` 是相对当前 subject 的阵营，不是固定玩家视角。
- “以上/以下”是包含边界：`greater_or_equal` / `less_or_equal`。
- 策略条件里的 `Priority` 不是过滤器；找不到偏好目标时必须回退默认合法目标。
- 策略条件里的 `Only` 是硬过滤；找不到合法目标时跳过本条技能。
- 条件 1 + 条件 2 不能简单顺序过滤：`Priority+Only` 要先交集再回退到 `Only`，`Priority+Priority` 要先交集再按优先回退。
- 普通地面近战不能因为 `优先后排` / `仅后排` 越过前排阻挡。

## 架构错误

- 不要重新引入 `ISkillEffect.Apply()` / `SkillEffectFactory` / `Skills/Effects/*` 空双轨。
- 不要把新规则写进 `Main.cs`。
- `SkillEffectExecutor` 负责效果原子；`PassiveSkillProcessor` 负责触发仲裁。
- 新增接口前先证明有真实替换边界，不要为了“看起来架构化”抽象。

## UI 错误

- Godot UI 改动后必须 F5 冒烟。
- UI 可以显示中文，但不能把中文当运行时判断值存入策略 JSON。
- 策略 UI 必须保留 `Mode1` / `Mode2` 的 `Priority` / `Only`，不要在沙盒表格里偷偷强制全部写成 `Only`。
- `Main.cs` 已经偏大，新增 UI helper 时优先把视图构造拆出去。
