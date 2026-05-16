# 命名与资料源统一方案

最后更新：2026-05-16

## 结论

`UnicornOverlord-Skills-Datamine-CN.md` 是当前技能真源。技能中文名、英文名、效果、隐藏机制和后续录入审计都以它为准。

`unicorn-overlord-class-compendium.md` 已降级为职业描述资料，只保留职业标题、兵种、角色定位、主要角色和特点。它不再作为技能资料使用。

## 禁用资料

以下旧文档内容过时，禁止作为技能依据：

- `unicorn-overlord-active-skills.md`
- `unicorn-overlord-passive-skills.md`
- `unicorn-overlord-active-skills-by-type.md`
- `unicorn-overlord-passive-skills-by-timing.md`

## 代码命名边界

- `id`：稳定引用 ID，用于角色技能池、装备、策略、测试和存档兼容。除非单独做大迁移，不因为翻译调整而改。
- `englishName` / `ccEnglishName`：datamine 英文名，作为代码审计和后端语义命名依据。
- `name` / `ccName`：当前中文显示名，暂时使用 datamine 中文翻译。
- 未来原创化或本地化时，不改 `id`；优先改显示名映射或本地化表。

## 当前落地状态

- `characters.json` 已补 `englishName` / `ccEnglishName`，并将 18 个现有角色中文显示名调整为 datamine 口径。
- `active_skills.json` 已补 `englishName`，并将当前主动技能中文显示名调整为 datamine 口径。
- `passive_skills.json` 已补 `englishName`，并将当前被动技能中文显示名调整为 datamine 口径。
- `class_display_names.json` 已调整为 datamine 中文显示名口径，并补齐现有角色本职/转职 ID 的显示名。
- `character_role_descriptions.json` 保留 18 个现有角色定位；外部 `unicorn-overlord-class-compendium.md` 已裁剪为职业描述摘录，删除技能表、成长表、装备推荐和策略示例。

## 后续迁移顺序

1. 保持 `id` 不动，先完成显示名/英文名和资料源统一。
2. 用 datamine 逐条审计 `effectDescription` 和结构化 `effects`，不要仅按旧中文短描述猜。
3. 如果确实需要重命名 `id`，先出映射表，再同步 `characters.json`、技能 JSON、装备、策略预设、测试和文档引用。
4. 角色描述继续用 `{char:ID}` / `{class:ID}` token，不写死会被改名的角色或兵种显示名。
5. 后续原创化时增加正式本地化/显示名表，避免把 UI 文案反向写进规则数据。

## 为什么先改代码数据再改文档

如果先把文档翻译全部换成新名，再回头改代码，会丢失旧名到新名的对照关系。当前做法是先在代码数据里保留稳定 `id`，同时加入 datamine 英文名和中文显示名；这样后续审计时能同时看到旧引用骨架和新显示名，不会不知道“谁是谁”。
