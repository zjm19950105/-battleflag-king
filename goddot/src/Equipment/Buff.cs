namespace BattleKing.Equipment
{
    public class Buff
    {
        public string SkillId { get; set; }
        public string TargetStat { get; set; }
        public float Ratio { get; set; }
        public int FlatAmount { get; set; }
        public bool IsOneTime { get; set; }
        public bool IsPureBuffOrDebuff { get; set; }
        public int RemainingTurns { get; set; }
    }
}
