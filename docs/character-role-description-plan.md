# 角色描述结构化方案

最后更新：2026-05-16

## 结论

新增角色和技能前，先把现有 18 个角色的描述结构稳定下来。原因是角色描述会引用其他角色名、兵种名和后续原创化名称；如果现在继续写死中文字符串，后面改名会变成全文搜索替换，风险很高。

## 唯一资料源

- 角色、兵种、角色定位、特点、技能：`C:\Users\ASUS\Music\圣兽之王资料整理\有用的资料\unicorn-overlord-class-compendium.md`
- 禁止作为技能或角色描述依据：`unicorn-overlord-active-skills.md`、`unicorn-overlord-passive-skills.md`、`unicorn-overlord-active-skills-by-type.md`、`unicorn-overlord-passive-skills-by-timing.md`
- 字段规范和测试规范：`docs/skill-and-character-authoring-guide.md`

## 抽取规则

每个角色描述只抽以下最小必要字段：

- `displayName`：职业段标题，例如 `射手 / 盾射手 (Shooter / Shield Shooter)`。
- `unitClasses`：职业段 `| 兵种 | ... |` 的稳定兵种 ID。
- `mainRoles`：`#### 角色定位` 下 `主要角色` 的 bullet。
- `characteristics`：`#### 角色定位` 下 `特点` 的 bullet。
- `referencedCharacterIds`：正文里 `{char:ID}` token 的 ID 列表。
- `referencedClassIds`：正文里 `{class:ID}` token 的 ID 列表。

示例：

```json
{
  "characterId": "shooter",
  "displayName": "射手 / 盾射手 (Shooter / Shield Shooter)",
  "unitClasses": ["infantry"],
  "mainRoles": [
    "高单体火力弩兵",
    "{class:flying}系特攻",
    "转职后坦克",
    "战后回复辅助"
  ],
  "characteristics": [
    "物攻59为弓兵系最高，对高物防{char:wyvern_knight}、{char:feather_sword}等也能造成大伤害"
  ],
  "referencedCharacterIds": ["wyvern_knight", "feather_sword"],
  "referencedClassIds": ["flying"]
}
```

## 名称引用规则

- 描述正文不得写死会被改名的角色名，例如“飞龙”“羽剑士”“猎人”。
- 描述正文用 `{char:wyvern_knight}`、`{char:feather_sword}`、`{class:flying}` 这类稳定 ID token。
- UI 展示时再通过显示名映射解析 token。
- 改名时只更新显示名映射；引用该 ID 的所有描述自动更新。

## 现状

项目已经有基础结构：

- `goddot/data/character_role_descriptions.json`
- `goddot/data/class_display_names.json`
- `goddot/src/data/Models/CharacterRoleDescriptionData.cs`
- `GameDataRepository.CharacterRoleDescriptions`

但在继续新增角色前，还需要完成一次收口：

- 确认 18 个现有角色的 `mainRoles` / `characteristics` 都来自 `class-compendium`。
- 清理正文里的写死角色名和兵种名，统一改成 token。
- 增加数据契约测试：token 必须能在角色/显示名映射中解析，`referenced*Ids` 必须与正文 token 一致。
- UI 展示层统一走 token 解析，不直接显示原始 `{char:ID}`。

## 实施顺序

1. 先补齐并审计现有 18 个角色的 `character_role_descriptions.json`。
2. 补 token 解析和数据契约测试。
3. 再加入新角色和新技能。
4. 新增角色时同步录入角色描述，禁止先留空再回补。
