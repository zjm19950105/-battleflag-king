using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;

namespace BattleKing.Equipment
{
    public interface ITrait
    {
        void OnCalculateDamage(DamageCalculation calc, BattleUnit owner);
        AttackType ModifyAttackType(AttackType baseType, BattleUnit owner);
    }
}
