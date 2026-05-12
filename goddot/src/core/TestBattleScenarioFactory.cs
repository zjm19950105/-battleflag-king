using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Data;

namespace BattleKing.Core
{
    public sealed record TestBattleScenarioSlot(
        string CharacterId,
        int Position,
        bool IsPlayer,
        int Day,
        bool IsCc = false);

    public sealed class TestBattleScenarioFactory
    {
        private readonly GameDataRepository _gameData;
        private readonly BattleSetupService _battleSetup;

        public TestBattleScenarioFactory(GameDataRepository gameData)
        {
            _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            _battleSetup = new BattleSetupService(gameData);
        }

        public BattleContext CreateContext(IEnumerable<TestBattleScenarioSlot> slots)
        {
            if (slots == null)
                throw new ArgumentNullException(nameof(slots));

            var slotList = slots.ToList();
            ValidateSlots(slotList);

            var context = new BattleContext(_gameData);
            foreach (var slot in slotList.OrderBy(s => s.IsPlayer ? 0 : 1).ThenBy(s => s.Position))
            {
                var unit = _battleSetup.CreateUnit(slot.CharacterId, slot.IsPlayer, slot.Position, slot.Day, slot.IsCc);
                if (slot.IsPlayer)
                    context.PlayerUnits.Add(unit);
                else
                    context.EnemyUnits.Add(unit);
            }

            return context;
        }

        public static BattleContext CreateContext(GameDataRepository gameData, IEnumerable<TestBattleScenarioSlot> slots)
        {
            return new TestBattleScenarioFactory(gameData).CreateContext(slots);
        }

        private static void ValidateSlots(List<TestBattleScenarioSlot> slots)
        {
            ValidateSide(slots, isPlayer: true);
            ValidateSide(slots, isPlayer: false);

            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.CharacterId))
                    throw new ArgumentException("CharacterId is required.", nameof(slots));

                if (slot.Position < 1 || slot.Position > 6)
                    throw new ArgumentOutOfRangeException(nameof(slots), "Position must be between 1 and 6.");
            }

            var duplicate = slots
                .GroupBy(s => new { s.IsPlayer, s.Position })
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new ArgumentException("Each side can only use a position once.", nameof(slots));
        }

        private static void ValidateSide(List<TestBattleScenarioSlot> slots, bool isPlayer)
        {
            int count = slots.Count(s => s.IsPlayer == isPlayer);
            if (count < 1 || count > 6)
            {
                string sideName = isPlayer ? "Player" : "Enemy";
                throw new ArgumentException($"{sideName} side must contain 1 to 6 units.", nameof(slots));
            }
        }
    }
}
