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
        public TargetType TargetType => Data.TargetType;
        public bool HasPhysicalComponent => Data.Type == SkillType.Physical;
        public bool HasMixedDamage => false; // Simplified for MVP

        public ActiveSkill(ActiveSkillData data, GameDataRepository gameData)
        {
            Data = data;
        }
    }
}
