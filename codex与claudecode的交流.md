# Codex × Claude Code 协作协议

> **最后更新**: 2026-05-08
> **当前焦点**: Phase 1.4 — 6v6 完整竞技场

---

## 一、能力分工原则

### Claude Code（我）
| 优势 | 具体使用 |
|------|----------|
| **1M 上下文窗口** | 全文通读项目 + 全局理解 + 跨文件一致性审计 |
| **基础代码/苦力活** | 批量 JSON 填充、模板代码、UI 布局、测试铺量、文档同步 |
| **项目记忆持久化** | CLAUDE.md / progress.md / roadmap.md 全面更新、归档 |
| **compact 能力** | 长会话上下文压缩，不怕对话变长 |

### Codex（OpenAI）
| 优势 | 具体使用 |
|------|----------|
| **模型代码能力强** | 复杂架构设计、核心战斗规则层改动、疑难 bug 修复 |
| **258K 上下文（有限）** | 尽量用 subagent 执行重度代码任务，保留主上下文做架构把控 |
| **架构审查** | 新模块设计评审、数据流一致性验证、重构方案决策 |

### 基本规则
1. **架构/核心规则层改动 → Codex 主导**（如 BattleEngine 流程、DamageCalculator 重写、EffectType 扩展）
2. **铺量/填充/UI/文档同步 → Claude Code 主导**（如 JSON 数据填充、UI 调整、测试用例批量添加、CLAUDE.md/progress.md/roadmap.md 更新）
3. **双方同时修改项目时，通过本文档第二节【变更日志】同步，避免覆盖对方工作**
4. **Codex 每次会话开头应通读本文档第二节，了解 Claude Code 最近改了什么**

---

## 二、变更日志

> 格式: `日期 | 谁 | commit 摘要 | 涉及文件（关键）`
> **双方写完代码 commit 后必须在此记录，对方下次接手时先读此表**

### 2026-05-08

| 时间 | 谁 | 做了什么 | 涉及文件 |
|------|-----|----------|----------|
| 上午 | Codex | **Phase A 启动**: 测试地基修正（TargetSelector 断言修正、Row/Column 不误伤友方、DamageCalculator 测试禁用随机暴击/格挡、DataContractTest 新增、魔法攻防装备读取修正、EquipmentSlot 兼容 hit/block_rate）。63/63 测试通过 | `TargetSelectorTest.cs`, `DamageCalculatorTest.cs`, `DataContractTest.cs`, `EquipmentSlotTest.cs`, `TargetSelector.cs`, `EquipmentSlot.cs` |
| 上午 | Codex | **主动技能效果管线接通**: 新增 SkillEffectExecutor.cs (~425行)、SkillEffectExecutionState.cs，接入 BattleEngine。Type 直出伤害、ApDamage、Heal、Poison/Burn/Freeze/Stun/Blind 均实现 | `SkillEffectExecutor.cs`, `SkillEffectExecutionState.cs`, `BattleEngine.cs`, `BattleUnit.cs`, `SkillEffectExecutorTest.cs` (273行) |
| 上午 | Codex | **被动效果执行重构**: 共用 SkillEffectExecutor 执行被动效果，消除 passive/active 双轨代码 | `PassiveSkillProcessor.cs` |
| 上午 | Codex | **文档**: effect extension doctrine（效果扩展教条）、codex-architecture-diagnosis.md、AGENTS.md 创建 | `AGENTS.md`, `docs/codex-architecture-diagnosis.md` |
| 下午 | Claude Code | **协作协议文件创建 + Codex 反馈修正**: 根据 Codex 审查结果修正任务看板（3 失败测试→已过期、补充 effectType 缺口详情、hitChance 公式具体行号），同步修正 AGENTS.md/CLAUDE.md 测试数量为 69/69 | `codex与claudecode的交流.md`, `AGENTS.md`, `CLAUDE.md` |
| 下午 | Codex | **主动技能 effects 全管线竣工**: 审计确认项目无真实 EffectType enum、当前以字符串 effectType 驱动；补齐 SkillEffectExecutor 的 ApDamage/PpDamage、GrantSkill、RemoveBuff/CleanseDebuff、HealRatio、AddDebuff、TemporalMark、CoverAlly、Counter/Pursuit/Preemptive handler，并补充 ModifyDamageCalc 参数覆盖。新增/扩展测试后 `dotnet test` 75/75 通过，`goddot` 主项目 `dotnet build` 0 警告 0 错误 | `SkillEffectExecutor.cs`, `BattleUnit.cs`, `SkillEffectExecutorTest.cs`, `docs/csharp-architecture.md` |
| 下午 | Claude Code | **C1 默认策略迁移**: Main.cs ApplyDefaultStrategies 180行 switch → 16行 JSON 查找；strategy_presets.json 新增18个角色默认策略；新增 PresetStrategyData.SkillId 字段；DataContractTest 期望值 3→21 | `Main.cs`, `strategy_presets.json`, `StrategyPresetData.cs`, `DataContractTest.cs` |
| 下午 | Claude Code | **C2 ConditionMeta 补全 + C3 hitChance 公式修正**: ConditionMeta.Status 新增非毒/非炎上/非冻结/非气绝/非黑暗 5个反向值；ConditionEvaluator 新增"非"前缀取反逻辑；BattleLogHelper 公式从 (Hit-Eva)×skillHit% 改为 skillHit+Hit-Eva + 飞行近战半减 | `ConditionMeta.cs`, `ConditionEvaluator.cs`, `BattleLogHelper.cs` |

---

## 三、任务看板

### 当前交接任务

| # | 任务 | 指派给 | 状态 | 备注 |
|---|------|--------|------|------|
| 1 | 主动技能 effects 管线接通 | Codex | ✅ 已完成 | SkillEffectExecutor 已接入 BattleEngine.cs:261 |
| 2 | 主动技能 effects 全管线竣工（补齐全 effectType handler + 测试） | Codex | ✅ 已完成 | 75/75 全绿；handler 覆盖 ApDamage/PpDamage/GrantSkill/RemoveBuff/CleanseDebuff/HealRatio/AddDebuff/TemporalMark/CoverAlly/Counter/Pursuit/Preemptive + ModifyDamageCalc 子参数 |
| 3 | ~~修复 3 个失败测试~~ | ~~Codex~~ | ❌ 已过期 | 75/75 全绿 |
| 4 | hitChance 公式一致性修正 | Claude Code | ✅ 已完成 | `BattleLogHelper.cs` 公式改为 `skillHit+Hit-Eva` + 飞行近战半减 |
| 5 | 默认策略迁移到 JSON | Claude Code | ✅ 已完成 | `Main.cs:462-640` 180行 switch → 16行 JSON 查找；`strategy_presets.json` 3→21 条 preset |
| 6 | ConditionMeta 补反向状态 + Evaluator 取反 | Claude Code | ✅ 已完成 | 新增 非毒/非炎上/非冻结/非气绝/非黑暗 5值；Evaluator 新增"非"前缀取反 |
| 7 | **主动技能 JSON Effects 数组示范贯通**（选2-3个代表性技能，写入完整 Effects 数组，建立集成测试验证全管线：Main→Strategy→SkillEffectExecutor→BattleEngine→正确结果） | Codex | ⬜ 待办 | Handler 已就绪，JSON 仍只有 4 种 effectType。需示范端到端后，Claude Code 批量铺量其余 52 个技能 |
| 8 | 职业定位描述数据结构化 + ID 引用规则 | Claude Code | ⬜ 待办 | 方案已有，需新建 CharacterRoleDescriptionData 模型 + JSON + 显示层解析 |
| 9 | 6v6 模式入口 + 流程（当前 UI 6 格但入口仍是 1v1/3v3） | TBD | ⬜ 待办 | `Main.cs:232` 无 6v6 流程 |
| 10 | 主动/被动技能 JSON Effects 批量填充（基于 Codex 示范模板 + 参考文档） | Claude Code | ⬜ 待办 | 依赖 #7 完成 |

---

## 四、交接流程

### Codex → Claude Code 交接
当 Codex 完成一个开发批次后：
1. commit 所有改动
2. 在本文档第二节记录变更
3. 在第三节更新任务状态
4. 如有未完成工作需要在第三节写明下一步

### Claude Code → Codex 交接
当 Claude Code 完成工作后：
1. commit 所有改动
2. 在本文档第二节记录变更
3. 同步更新 CLAUDE.md / progress.md / roadmap.md
4. 如有需要 Codex 接手的高难度任务，在第三节写明并标注

### 给 Codex 发提示词
用户在 Claude Code 中看到本文档后，会复制第三节/第四节相关内容发给 Codex 作为任务启动上下文。

---

## 五、共享文档清单

| 文档 | 谁维护 | 说明 |
|------|--------|------|
| `CLAUDE.md` | Claude Code | 概念框架、机制规则、完整开发状态 |
| `AGENTS.md` | Codex | Codex 版项目文档（与 CLAUDE.md 内容平行） |
| `progress.md` | Claude Code | 开发进度日志 |
| `roadmap.md` | Claude Code | 开发路线图 + 想法池 |
| `docs/csharp-architecture.md` | 双方 | C# 技术架构（改代码同步更新） |
| `docs/dev-mistakes.md` | 双方 | 开发错误记录 |
| `docs/codex-architecture-diagnosis.md` | Codex | Codex 架构诊断报告 |
| `codex与claudecode的交流.md` | 双方 | 本文档 — 协作日志 + 任务看板 |

---

## 六、Codex 下一步提示词

以下内容可直接复制给 Codex 作为新对话任务：

---

**给 Codex 的提示词（2026-05-08 第二轮）**:

```
项目: C:\Users\ASUS\战旗之王
先读: AGENTS.md + docs/csharp-architecture.md + codex与claudecode的交流.md

当前状态: 75/75 测试全绿，0错误0警告编译。你上次完成了所有 effectType handler（ApDamage/PpDamage/GrantSkill/RemoveBuff/HealRatio/AddDebuff/TemporalMark/CoverAlly/Counter/Pursuit/Preemptive + ModifyDamageCalc 子参数），但 active_skills.json 的 55 个技能中 Effects 数组仍未使用这些新 handler。

Claude Code 这轮完成了: 默认策略 JSON 迁移、ConditionMeta 反向状态补全、hitChance 公式修正（详见 codex与claudecode的交流.md 第二节）。

你的任务: 主动技能 JSON Effects 示范贯通

选 3 个代表性主动技能，在 active_skills.json 中写入完整的 Effects 数组，建立集成测试验证全管线: Strategy→SkillEffectExecutor→BattleEngine→正确战斗结果。

选角建议:
- 1 个物理攻击技（如 act_sharp_slash 锐利斩击）— 覆盖 AddBuff/ModifyDamageCalc
- 1 个 debuff 技（如 act_smash 粉碎）— 覆盖 AddDebuff
- 1 个治疗技（如 act_row_heal 列治愈）— 覆盖 HealRatio

具体步骤:
1. 读取 active_skills.json 中这 3 个技能的当前 Effects 数组（可能为空或仅占位）
2. 对照参考文档 C:\Users\ASUS\Music\圣兽之王资料整理\有用的资料\ 中的技能效果描述，写入结构化的 Effects 数组
3. 在 SkillEffectExecutorTest.cs 中为这 3 个技能添加集成测试：创建 BattleUnit → 设置 Strategy → 执行 SkillEffectExecutor → 验证 HP/buff/debuff 变化正确
4. 如果 JSON Effects 格式需要扩展（例如某些 handler 需要新字段），同步更新 SkillEffectData 模型
5. dotnet test (全绿) + dotnet build (0错误0警告)

注意: 数值必须对照参考文档，不得擅自修改。效果扩展教条见 docs/effect-extension-doctrine.md。

完成后: commit，并在 codex与claudecode的交流.md 第二节记录变更。Claude Code 将基于你的 3 个示范模板批量填充其余 52 个技能的 Effects 数组。
```

---

## 七、Git 状态备忘

| 项目 | 状态 |
|------|------|
| 分支 | `main` → `origin/main` ahead 5 (Codex 4 commits + 待 commit Claude Code 改动) |
| 已删除未提交 | `battle_log.txt`, `build.bat` |
| 未跟踪 | `codex与claudecode的交流.md` |
| 测试 | 75/75 全绿 |
| 编译 | 0 错误 0 警告 |

---
