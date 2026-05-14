using BattleKing.Core;

namespace BattleKing.Ui
{
    public static class SandboxDraftUnitState
    {
        public static bool MatchesSelection(
            BattleUnit unit,
            string characterId,
            int position,
            bool isPlayer,
            bool isCc,
            int day)
        {
            if (unit == null || string.IsNullOrWhiteSpace(characterId))
                return false;

            return unit.Data.Id == characterId
                && unit.Position == position
                && unit.IsPlayer == isPlayer
                && unit.IsCc == isCc
                && unit.CurrentLevel == DayProgression.GetConfig(day).MaxSkillLevel;
        }

        public static void MoveToSlot(BattleUnit unit, bool isPlayer, int slotIndex)
        {
            if (unit == null)
                return;

            unit.IsPlayer = isPlayer;
            unit.Position = slotIndex + 1;
        }
    }
}
