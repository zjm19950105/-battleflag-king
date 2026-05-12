# Passive Migration Plan

最后更新：2026-05-10

目标：逐步把被动从 legacy tags 迁到结构化 `effects`，让每个被动的运行时效果、日志、测试和数据契约一致。本文是初版计划，不包含代码改动。

## 原则

- 每次只迁移一个小批次，优先 1 到 3 个被动，避免同时改多个触发时机和多个战斗阶段。
- 先写或补测试，再改 JSON `effects`，最后删除对应 `DataContractTest.LegacyTagOnlySkillAllowlist` 的 `passive:*` 条目。
- 有 `effects` 的被动不再依赖 tags 执行；迁移时必须把运行时语义全部写入 effects 或明确写成设计待办。
- tags 可以短期保留做显示/兼容，但不能作为战斗语义来源。完成迁移后，应删除会误导的 legacy tags。
- 一批迁移结束后固定跑 `dotnet test goddot-test\goddot-test.csproj --no-restore` 与 `dotnet build goddot\goddot.csproj --no-restore`。
- 涉及 API、JSON schema、枚举或效果类型扩展时，同步更新 `docs/csharp-architecture.md`。

## 推荐工作流

1. 选定小批次，并在 `docs/passive-system-audit.md` 中确认当前风险和角色影响。
2. 为每个被动补最小可失败测试：直接发布对应 EventBus 事件，断言 HP/AP/PP/buff/debuff/calc/pending action 的具体变化。
3. 如果现有 executor 已支持所需效果，只改 `passive_skills.json` 的 `effects`。
4. 如果 executor 不支持，先加通用原子 effect，避免在 `PassiveSkillProcessor` 继续加专用 tag 分支。
5. 迁移后删除该被动的 `passive:*` allowlist 项，确保不会留下 stale allowlist。
6. 检查日志：攻击类 pending action 应有 `BattleLogEntry`；非攻击类至少需要可测试的文本日志，后续再补结构化日志能力。
7. 跑测试和 build，记录本批剩余风险。

## 批次建议

### Batch 1：精灵女先知可见 no-op

范围：`pas_healing_touch`、`pas_spirit_guard`。

状态：已完成。`pas_healing_touch` 已迁移为 `HealRatio(target=Defender)` + `CleanseDebuff(target=Defender)`；`pas_spirit_guard` 已迁移为 `HealRatio(target=RowAllies)` + `TemporalMark(DebuffNullify)` + `ModifyCounter(Sprite)`。`passive:pas_healing_touch` 已从 legacy tag-only allowlist 移除。

原因：
- `PassiveSkillProcessorTest.RealPassiveJson_ElfSibylHealingTouch_HealsAndCleansesAttackedAlly` 覆盖 `pas_healing_touch` 的结构化治疗/净化。
- 同一角色还持有 `pas_spirit_guard`，现在由真实 JSON 测试覆盖 `HealRatio`、`TemporalMark(DebuffNullify)` 和 `ModifyCounter`。

测试先行：
- `pas_healing_touch` 在 `AllyOnAttacked` 后治疗被攻击友方或最低 HP 友方，并移除 debuff。
- `pas_spirit_guard` 在 BattleStart 对行/友方治疗或净化，同时保留 Sprite counter。
- 断言 PP 消耗、目标选择、日志。

迁移注意：
- 如果 `CleanseDebuff` 已足够表达净化，优先复用。
- 行目标如果语义不明确，先写成明确的 `RowAllies` 或 `AllAllies`，不要靠 tag。

### Batch 2：方向性高风险 calc 修改

范围：已完成 `pas_block_seal`。

原因：
- `pas_quick_curse` 已完成：`SelfBeforeHit` 改为 structured `AddDebuff(target=Attacker, Str -20%)` + `StatusAilment(target=Attacker, CritSeal)`，并删除误导性的 `SureHit` tag 与 allowlist。
- `pas_block_seal` 已迁移为 `CounterAttack(target=Attacker)`，不再修改 incoming calc；`IgnoreDef100Heavy` 通过 pending action `ignoreDefenseRatio=1` + `ignoreDefenseTargetClass=Heavy` 表达。

测试先行：
- 被动触发后不应增强敌方 incoming 攻击，除非设计明确如此。
- 如果应生成反击/诅咒 pending action，断言 action 的 actor、target、damageType、attackType、tags。
- 如果应修改攻击者 debuff，断言目标是 attacker 而不是 defender。

迁移注意：
- 先确认技能设计：这是防御反制、先制 debuff，还是修改下一次己方攻击。
- 不明确前不要把旧 tag 机械映射成 calc flags。

### Batch 3：pending action 语义补全

范围：已完成 `pas_counter_magic`、`pas_wide_counter`；可选 `pas_pursuit_slash`。

原因：
- `pas_counter_magic` 已改为 `damageType: Magical` + `attackType: Magic`。
- `pas_wide_counter` 的 `targetType: Row` 已在 `BattleEngine.ProcessPendingActions()` 按锚点目标展开同排目标。
- `pas_pursuit_slash` 反击已实现，但 `HitPpPlus1` 缺失。

测试先行：
- 魔法反击应产生 `DamageType=Magical`、`AttackType=Magic`。
- 广域反击应实际命中同一行目标，或如果设计只打 attacker，则改名/删 tag。
- 追击斩命中后 PP 恢复要有明确触发点。

迁移注意：
- 目标展开已在 pending action 处理处统一完成，不在单个被动里硬编码。

### Batch 4：日志文字但无状态变化

范围：`pas_formation_counter`、`pas_aid_cover`、`pas_bounce`。

原因：
- 这些被动容易在 UI 里看起来“触发了”，但没有改变 AP/HP/防御/掩护状态。

测试先行：
- 主动礼物必须明确给谁 AP+1。
- 援助掩护必须明确是否同时掩护、格挡、治疗。
- 弹跳必须明确治疗比例和目标。

迁移注意：
- `RecoverAp`、`RecoverHp`、`CoverAlly`、`ModifyDamageCalc` 已有原子，优先复用。

### Batch 5：屏障/免疫统一设计

范围：`pas_magic_barrier`、`pas_row_barrier`、`pas_holy_guard`，后续扩展到 `pas_curse_swamp`。

原因：
- 魔法伤害无效、异常无效、debuff 免疫/净化需要统一接入 `DamageCalculation` 与状态应用流程。
- 当前如果分散实现，后续主动技能异常、被动异常、BattleEnd 状态都会变难维护。

测试先行：
- 魔法攻击被无效化时伤害为 0，但是否仍算 hit 要明确。
- 异常/debuff 被阻止时，`AppliedAilments` 与目标 `Ailments/Buffs` 都应断言。
- 行屏障目标必须限制到同 row/column 设计范围。

迁移注意：
- 可能需要新增通用 effect，例如 `TemporalMark` 已能覆盖一部分免疫；如果不够，再扩展 executor。

## Allowlist 删除策略

当前 `DataContractTest` 会报告并校验所有 tag-only 技能；passive 迁移时按以下方式收敛：

1. 一个被动获得完整 effects 后，删除对应 `["passive:xxx"]` allowlist 项。
2. 如果 tags 只剩显示用途，确认不再被测试视为 tag-only；必要时删掉旧 tags。
3. 若 effects 只覆盖部分语义，不删除 allowlist 之外的风险记录；在审计文档保留“半迁移”。
4. 不把 active 技能 allowlist 和 passive 技能混在一个批次里处理，除非该批明确同时覆盖 active/passive 两侧。

## 第一批完成标准（已达成）

- `pas_healing_touch` 不再是 tag-only。
- `pas_spirit_guard` 不再只加计数器；治疗/净化设计有测试。
- `elf_sibyl` 的测试场景中，触发被动后至少能观察到 HP 或 debuff 状态变化。
- 对应 `passive:*` allowlist 项减少。
- `dotnet test goddot-test\goddot-test.csproj --no-restore` 通过。
- `dotnet build goddot\goddot.csproj --no-restore` 0 警告。
