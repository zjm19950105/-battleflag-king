# 开发错误记录

本文档记录项目开发中反复出现的问题，供后续 Subagent 调用和自我审查参考。

## Subagent 调用规范

### 0. 调 Subagent 前必须先读架构文档
**问题**：Subagent 凭记忆或语义猜测枚举值、字段名，导致 `JsonException`、字段缺失。
**规则**：
- **每次调用 Subagent，prompt 第一条必须是**：`先读取 docs/csharp-architecture.md，对照其中的枚举定义和类字段，再动手修改 JSON/C# 代码。`
- Subagent 的回复中必须引用它读到的文档内容（证明它确实读了）。
- 如果文档和代码冲突，以代码为准，但必须在回复中标注文档过时。

### 1. 枚举值必须显式列出合法值，且禁止自行创造新枚举值
**问题1**：批量填充 JSON 时，Subagent 使用了 `"Support"` 作为 `SkillType`，但 C# 枚举定义的是 `Assist`，导致 `JsonException`。
**问题2**：Subagent 给兵士新增被动 `pas_formation_counter` 时，凭语义创造了 `"AllyOnActiveUse"` 作为 `triggerTiming`，但 `PassiveTriggerTiming.cs` 中并没有这个值，只有 `SelfOnActiveUse`。编译通过不了。
**根因**：
- 没有把枚举的**完整合法值**一字不差地列出。
- Subagent 在发现现有枚举无法满足语义时，**自行创造了新枚举值**，而不是停下来报告问题。
**规则**：
1. 凡是涉及枚举的字段，指令中必须原样复制 C# 枚举的所有成员名。
2. **绝对禁止 Subagent 创造新的枚举值**。如果现有枚举无法表达某个概念（如需要 `AllyOnActiveUse` 但只有 `SelfOnActiveUse`），Subagent 必须：
   - **停止填充该条目**
   - 在回复中明确报告：`"需要新增枚举值 X，但现有枚举中没有。请确认是否新增。"`
3. 只有主会话（我）有权决定是否新增枚举值并修改 C# 代码。Subagent 无权修改枚举定义。
**检查点**：Subagent 写入 JSON 前，必须用脚本核对所有枚举字段值是否在合法列表内。如果有不在列表内的值，必须报错而不是静默写入。

### 2. 数值必须标注来源，禁止臆测
**问题**：Subagent 多次使用网络来源或自行编造技能名称、威力数值，与本地参考文档不符。
**规则**：
- 所有角色属性、技能威力、命中率必须来自 `C:/Users/ASUS/Music/圣兽之王资料整理/`。
- 如果参考文档缺失某项数据，必须明确标注"参考文档未提供，采用近似值"，不得静默填充。

### 3. 修改 C# 数据结构后必须同步架构文档
**问题**：`CharacterData` 新增了 `CcInitialEquipmentIds`，`BattleUnit` 新增了 `IsCc`，但 `docs/csharp-architecture.md` 没有同步更新。导致 Subagent 读到的文档和实际代码不一致。
**规则**：
- 任何 C# 类字段、枚举值、方法签名的变更，**必须在同一批次**更新 `csharp-architecture.md`。
- `dev-mistakes.md` 和 `progress.md` 也应同步记录变更。
- 如果来不及同步，在 Subagent prompt 中明确告知"文档未更新，以代码为准"。

### 4. JSON 修改后必须做格式校验
**问题**：手动追加 JSON 数组元素时漏逗号、多逗号、括号不匹配。
**规则**：每次修改 JSON 后，必须用 Python `json.load()` 验证，再汇报完成。

---

## 游戏机制规则

### 4. 角色出场默认未 CC（未转职）
**问题**：`Main.cs` 默认 `isCc: true`，导致所有角色出场即CC状态。
**规则**：
- 角色初始状态必须是 **未CC**。
- 只有在玩家主动选择/达成条件后才进入CC状态。
- 测试时需要验证CC状态，应显式传参 `isCc: true`，不能作为默认值。

### 5. 武器槽永远有装备，饰品槽可为空
**问题**：未区分"核心装备不可卸下"和"可选装备"。
**规则**：
- **MainHand（武器）**：任何情况下不能为空。角色创建时必须装备武器。
- **OffHand（盾/副手武器）**：如果装备槽存在（CC后解锁），则必须有装备。未解锁时可为空。
- **Accessory（饰品）**：可为空。
- 实现上：`CharacterData` 需要分别定义 `InitialEquipmentIds`（未CC）和 `CcInitialEquipmentIds`（CC后），后者必须覆盖新增/变化的装备槽。

### 6. 双持装备逻辑
**问题**：`EquipmentSlot.Equip` 把 Sword 永远放进 MainHand，导致双持时第二把剑覆盖主手。
**规则**：
- 如果 MainHand 已有武器，且放入的是武器类（Sword/Axe/Spear/Bow/Staff），则放入 OffHand。
- 双持攻击力计算：主手全额 + 副手一半（已有 `GetTotalStat` 部分实现，但 `CanDualWield` 当前硬编码为 false，需后续根据职业判断）。

### 7. 装备槽统一规则（3基础 → 4 CC）
**问题**：饰品槽数量反复修改，先统一双饰品、后统一单饰品、再逐个核对参考文档，导致混乱且难以维护。
**规则**：
- **统一规则**：所有角色基础状态固定 **3个槽**，CC后固定 **4个槽**。
- **分配逻辑**：武器（含盾/大盾）占多少槽，剩余全是饰品槽。
  - 单武器（无副手）：基础=1武器+2饰品（3槽），CC后=1武器+3饰品（4槽）
  - 剑+盾 / 双持：基础=2武器+1饰品（3槽），CC后=2武器+2饰品（4槽）
- **示例**：
  - 剑士（双持剑）：基础 `[Sword, Sword, Accessory]`，CC后 `[Sword, Sword, Accessory, Accessory]`
  - 佣兵（剑+盾）：基础 `[Sword, Accessory, Accessory]`，CC后 `[Sword, Shield, Accessory, Accessory]`
  - 斗士（斧+盾）：基础 `[Axe, Shield, Accessory]`，CC后 `[Axe, Shield, Accessory, Accessory]`
  - 猎人（弓）：基础 `[Bow, Accessory, Accessory]`，CC后 `[Bow, Accessory, Accessory, Accessory]`
- **CC必须提供 tangible benefit**：所有角色CC后都比基础多1个饰品槽。

### 8. 无盾格挡也减25%
**问题**：`GetBlockReduction()` 在无盾时返回0%，但参考文档确认所有角色都有基础格挡能力，无盾成功格挡也应减伤25%。
**规则**：
- 无盾格挡：减免 **25%**
- 装备盾（Shield）：减免 **25%**（盾的优势在于提升格挡率 `block_rate`，而非减伤率）
- 装备大盾（GreatShield）：减免 **50%**
- `Block` 属性是格挡**概率**，不是格挡前提条件。
