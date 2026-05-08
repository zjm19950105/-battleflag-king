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

---

## 三、任务看板

### 当前交接任务

| # | 任务 | 指派给 | 状态 | 备注 |
|---|------|--------|------|------|
| 1 | 主动技能 effects 管线接通 | Codex | ✅ 已完成 | SkillEffectExecutor 已接入 BattleEngine.cs:261 |
| 2 | EffectType 扩展：补充缺失的效果类型（RecoverAp/TemporalMark/CoverAlly/CounterAttack/PreemptiveAttack 等被动效果，以及 ApDamage 主动效果的 JSON 证据） | Codex | 🔄 进行中 | 当前 active JSON 只有 AddBuff/ConsumeCounter/ModifyCounter/ModifyDamageCalc；passive 还缺 |
| 3 | ~~修复 3 个失败测试~~ | ~~Codex~~ | ❌ 已过期 | 当前 69/69 全绿，0 失败 |
| 4 | **主动技能 effects 全 effectType 管线竣工**（含 ApDamage/GrantSkill/RemoveBuff/StealApPp/Knockback/HealRatio 等所有已定义的 effectType 在 SkillEffectExecutor 中实现到可测试） | Codex | ⬜ 待办 | SkillEffectExecutor 当前 ~425 行，仍有 effectType 分支待补齐 |
| 5 | hitChance 公式一致性修正 | Codex | ⬜ 待办 | `BattleLogHelper.cs:26` 用 `(Hit - Eva) × skillHit%`，`DamageCalculator.cs:209` 实际是 `skillHit + Hit - Eva`，含飞行近战半减 |
| 6 | 职业定位描述数据结构化 + ID 引用规则 | Claude Code | ⬜ 待办 | 方案已有，需新建 CharacterRoleDescriptionData 模型 + JSON + 显示层解析 |
| 7 | 默认策略从 Main.cs 迁移到 strategy_presets.json | Claude Code | ⬜ 待办 | `Main.cs:462` 仍有大型 ApplyDefaultStrategies 硬编码 |
| 8 | ConditionMeta 补"非毒/非buff"等未定义值 | Claude Code | ⬜ 待办 | `Main.cs:564` 已硬编码用"非毒"，但 `ConditionMeta.cs:60` 状态值列表缺反向状态；Evaluator 未处理 |
| 9 | 6v6 模式入口 + 流程（当前 UI 6 格但入口仍是 1v1/3v3） | TBD | ⬜ 待办 | `Main.cs:232` 无 6v6 流程 |

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

**给 Codex 的提示词（2026-05-08）**:

```
项目: C:\Users\ASUS\战旗之王
先读: AGENTS.md + docs/csharp-architecture.md + codex与claudecode的交流.md + docs/codex-architecture-diagnosis.md

当前状态: 69/69 测试全绿，0错误0警告编译。上次你完成了主动技能 effects 管线初通（SkillEffectExecutor ~425行接入 BattleEngine:261）。

你的任务: 主动技能 effects 全管线竣工

背景: SkillEffectExecutor.cs 的 switch 分支仍有多个 effectType 未实现。active_skills.json 的 55 个技能中，Effects 数组当前只用了 AddBuff/ConsumeCounter/ModifyCounter/ModifyDamageCalc 四种。对照 C# EffectType 枚举和 docs/effect-extension-doctrine.md（如果存在），补齐所有已定义但未实现的 effectType handler。

具体步骤（用 subagent 执行重度代码）:
1. 审计: 列出 C# EffectType 枚举所有值 vs SkillEffectExecutor.cs switch 已实现分支 vs active_skills.json 实际使用的 effectType。输出缺口清单。
2. 按优先级实现缺失 handler:
   - ApDamage（扣对方 AP）
   - GrantSkill（临时赋予技能）
   - RemoveBuff（驱散 buff/debuff）
   - HealRatio（按比例回血）
   - ModifyCounter（自定义计数器增减）
   - 以及其他枚举已定义但 switch 未覆盖的 effectType
3. 每个新 handler 至少写 1 个测试用例到 SkillEffectExecutorTest.cs
4. 如有必要，更新 docs/csharp-architecture.md 的 EffectType 文档
5. 最终: dotnet test (全绿) + dotnet build (0错误0警告)

工作区提示: git 当前 ahead 4 commits，有 2 个已跟踪文件被删除（battle_log.txt, build.bat），codex与claudecode的交流.md 是未跟踪文件。你不需要处理这些。

完成后: commit，并在 codex与claudecode的交流.md 第二节记录变更。
```

---

## 七、Git 状态备忘

| 项目 | 状态 |
|------|------|
| 分支 | `main` → `origin/main` ahead 4 |
| 已删除未提交 | `battle_log.txt`, `build.bat` |
| 未跟踪 | `codex与claudecode的交流.md` |
| 测试 | 69/69 全绿 |
| 编译 | 0 错误 0 警告 |

---
