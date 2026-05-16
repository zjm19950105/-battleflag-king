using BattleKing.Data;

namespace BattleKing.Skills
{
    public class ActiveSkill
    {
        public ActiveSkillData Data { get; private set; }

        public int ApCost => Data.ApCost;
        public SkillType Type => Data.Type;
        public SkillType DamageType => Data.DamageType ?? GetDefaultDamageType(Data.Type);
        public AttackType AttackType => Data.AttackType;
        public int Power => Data.Power;
        public int? PhysicalPower => Data.PhysicalPower;
        public int? MagicalPower => Data.MagicalPower;
        public TargetType TargetType => Data.TargetType;
        public bool HasPhysicalComponent => Data.PhysicalPower.HasValue || UsesDefaultDamageComponent(SkillType.Physical);
        public bool HasMagicalComponent => Data.MagicalPower.HasValue || UsesDefaultDamageComponent(SkillType.Magical);
        public bool HasMixedDamage => Data.PhysicalPower.HasValue && Data.MagicalPower.HasValue;

        public ActiveSkill(ActiveSkillData data, GameDataRepository gameData)
        {
            Data = data;
        }

        private bool UsesDefaultDamageComponent(SkillType damageType)
        {
            if (Data.PhysicalPower.HasValue || Data.MagicalPower.HasValue)
                return false;

            return IsDamageSkill(Data.Type) && DamageType == damageType;
        }

        private static bool IsDamageSkill(SkillType type)
        {
            return type == SkillType.Physical || type == SkillType.Magical;
        }

        private static SkillType GetDefaultDamageType(SkillType type)
        {
            return type == SkillType.Magical ? SkillType.Magical : SkillType.Physical;
        }
    }
}
