# 本地化与合规审计

> Phase 3 目标是把参考源名称、世界观文案和具体技能/装备命名逐步原创化。当前阶段只做风险可见化，不要求一次性替换，也不阻塞 Phase 1.4-A 的战斗规则加固。

## 当前需原创化的数据类别

1. 角色名
   - 来源字段：`goddot/data/characters.json` 的 `name`、`ccName`，以及 `character_role_descriptions.json` 的 `displayName`。
   - 当前风险：仍保留参考职业/转职名，例如“女巫”“白骑士”“狮鹫骑士”“圣骑士”等。

2. 职业显示名
   - 来源字段：`goddot/data/class_display_names.json`。
   - 当前风险：该文件是全局显示名解析表，保留了大量参考职业名。它应被视为后续原创化改名的主要入口，而不是最终名称已经合规的证明。

3. 技能名
   - 来源字段：`active_skills.json`、`passive_skills.json` 的 `name`。
   - 当前风险：部分技能名来自参考机制或直译风格，后续需要改成项目自有命名。

4. 装备名
   - 来源字段：`equipments.json` 的 `name`、`specialEffects`、`note`。
   - 当前风险：装备名和地区/商店备注中可能含有参考源风格词。A16 的自动扫描雏形暂不覆盖装备文件，但审计范围必须包含它。

5. 描述文本
   - 来源字段：角色说明、技能 `effectDescription`、`learnCondition`、装备 `specialEffects`/`note`、敌方阵型描述、策略名。
   - 当前风险：描述中容易硬编码职业名、技能名、装备名。后续应使用稳定 ID 或 token 引用渲染，避免改名时漏同步。

## 已建立的扫描护栏

新增测试 `LocalizationComplianceAuditTest` 会扫描：

- `class_display_names.json`
- `characters.json`
- `active_skills.json`
- `passive_skills.json`

它会报告已知参考词出现的位置和上下文，但当前不失败。这样可以在测试输出中持续看到合规风险，同时不会阻塞 Phase 1.4-A 的规则修复。

后续进入 Phase 3 时，可以把同一测试从“报告模式”切换为“失败模式”，或把允许列表逐步缩小到空。

## token 引用如何支持未来全局改名

项目已有 `{char:ID}` / `{class:ID}` token 引用方向。正确使用方式是：

- 存储层保存稳定 ID，例如 `{char:griffin_knight}`、`{class:flying}`。
- 显示层通过 `GameDataRepository` 读取当前显示名，再把 token 渲染成 UI 文案。
- 真正改名时只更新 `characters.json`、`class_display_names.json` 等权威名称源。
- 角色定位、说明文本、策略提示等引用处自动跟随新显示名，不依赖全文搜索替换。

这套方式的价值是把“机制引用”和“显示名称”分离：机制可以继续稳定使用 ID，Phase 3 的原创化命名可以集中发生，并且不破坏战斗数据引用。
