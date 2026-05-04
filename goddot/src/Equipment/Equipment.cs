using BattleKing.Data;

namespace BattleKing.Equipment
{
    public class Equipment
    {
        public EquipmentData Data { get; private set; }

        public Equipment(EquipmentData data)
        {
            Data = data;
        }
    }
}
