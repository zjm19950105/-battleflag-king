using BattleKing.Data;

namespace BattleKing.Skills
{
    public class PassiveSkill
    {
        public PassiveSkillData Data { get; private set; }

        public int PpCost => Data.PpCost;
        public PassiveTriggerTiming TriggerTiming => Data.TriggerTiming;
        public bool HasSimultaneousLimit => Data.HasSimultaneousLimit;

        public PassiveSkill(PassiveSkillData data, GameDataRepository gameData)
        {
            Data = data;
        }
    }
}
