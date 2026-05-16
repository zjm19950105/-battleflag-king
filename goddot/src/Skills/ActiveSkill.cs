using BattleKing.Data;

namespace BattleKing.Skills
{
    public class ActiveSkill
    {
        public ActiveSkillData Data { get; private set; }

        public int ApCost => Data.ApCost;
        public SkillType Type => Data.Type;
        public AttackType AttackType => Data.AttackType;
        public int Power => Data.Power;
        public int? PhysicalPower => Data.PhysicalPower;
        public int? MagicalPower => Data.MagicalPower;
        public TargetType TargetType => Data.TargetType;
        public bool HasPhysicalComponent => Data.Type == SkillType.Physical || Data.PhysicalPower.HasValue;
        public bool HasMagicalComponent => Data.Type == SkillType.Magical || Data.MagicalPower.HasValue;
        public bool HasMixedDamage => Data.PhysicalPower.HasValue && Data.MagicalPower.HasValue;

        public ActiveSkill(ActiveSkillData data, GameDataRepository gameData)
        {
            Data = data;
        }
    }
}
