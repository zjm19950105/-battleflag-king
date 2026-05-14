using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Equipment;

namespace BattleKing.Ui
{
    public static class EquipmentStatPreviewHelper
    {
        private static readonly string[] StatNames =
        {
            "HP", "AP", "PP", "Str", "Def", "Mag", "MDef", "Hit", "Eva", "Crit", "Block", "Spd"
        };

        public static EquipmentStatPreview Build(BattleUnit unit, string slotName, EquipmentData candidateEquipment)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (string.IsNullOrWhiteSpace(slotName)) throw new ArgumentException("Slot name is required.", nameof(slotName));

            var currentSlot = unit.Equipment;
            var previewSlot = CopySlot(currentSlot);
            previewSlot.EquipToSlot(slotName, candidateEquipment);

            var rows = StatNames
                .Select(statName => new EquipmentStatPreviewRow(
                    statName,
                    GetEffectiveStat(unit, currentSlot, statName),
                    GetEffectiveStat(unit, previewSlot, statName)))
                .ToList();

            return new EquipmentStatPreview(
                slotName,
                currentSlot.GetBySlot(slotName)?.Data,
                candidateEquipment,
                rows);
        }

        private static EquipmentSlot CopySlot(EquipmentSlot source)
        {
            return new EquipmentSlot
            {
                MainHand = source.MainHand,
                OffHand = source.OffHand,
                Accessory1 = source.Accessory1,
                Accessory2 = source.Accessory2,
                Accessory3 = source.Accessory3
            };
        }

        private static int GetEffectiveStat(BattleUnit unit, EquipmentSlot slot, string statName)
        {
            return unit.GetCurrentStat(statName, slot);
        }
    }

    public sealed class EquipmentStatPreview
    {
        public EquipmentStatPreview(
            string slotName,
            EquipmentData currentEquipment,
            EquipmentData candidateEquipment,
            IReadOnlyList<EquipmentStatPreviewRow> rows)
        {
            SlotName = slotName;
            CurrentEquipment = currentEquipment;
            CandidateEquipment = candidateEquipment;
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        public string SlotName { get; }
        public EquipmentData CurrentEquipment { get; }
        public EquipmentData CandidateEquipment { get; }
        public IReadOnlyList<EquipmentStatPreviewRow> Rows { get; }

        public EquipmentStatPreviewRow GetRow(string statName)
        {
            return Rows.First(row => row.StatName == statName);
        }
    }

    public sealed class EquipmentStatPreviewRow
    {
        public EquipmentStatPreviewRow(string statName, int current, int preview)
        {
            StatName = statName;
            Current = current;
            Preview = preview;
        }

        public string StatName { get; }
        public int Current { get; }
        public int Preview { get; }
        public int Delta => Preview - Current;
        public string DeltaBbcode => Delta switch
        {
            > 0 => $" [color=#88ff88]+{Delta}[/color]",
            < 0 => $" [color=#ff8888]{Delta}[/color]",
            _ => string.Empty
        };
    }
}
