using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BattleKing.Ai;
using BattleKing.Data;

namespace BattleKing.Ui
{
    public static class StrategyConditionUiMapper
    {
        public static IReadOnlyList<StrategyConditionCatalogCategory> GetCatalogCategories()
        {
            return StrategyConditionCatalog.Categories;
        }

        public static IReadOnlyList<RenderedStrategyConditionCatalogItem> GetCatalogItems(
            StrategyConditionCatalogCategoryId category,
            ActiveSkillData skill = null,
            bool includeNotImplemented = false)
        {
            return StrategyConditionCatalog.GetItems(category, includeNotImplemented)
                .Select(item => new RenderedStrategyConditionCatalogItem(
                    item,
                    item.RenderLabel(skill),
                    item.ResolveTextColor(skill)))
                .ToList();
        }

        public static void SaveCatalogSelection(
            Strategy strategy,
            bool isCondition1,
            string conditionId)
        {
            if (strategy == null)
                throw new ArgumentNullException(nameof(strategy));

            Condition condition = null;
            var mode = ConditionMode.Priority;
            if (!string.IsNullOrWhiteSpace(conditionId))
            {
                var item = StrategyConditionCatalog.FindById(conditionId)
                    ?? throw new ArgumentException($"Unknown strategy condition catalog item id: {conditionId}", nameof(conditionId));
                condition = item.BuildCondition();
                mode = item.Kind == StrategyConditionKind.Only ? ConditionMode.Only : ConditionMode.Priority;
            }

            if (isCondition1)
            {
                strategy.Condition1 = condition;
                strategy.Mode1 = mode;
            }
            else
            {
                strategy.Condition2 = condition;
                strategy.Mode2 = mode;
            }
        }

        public static StrategyConditionCatalogItem FindCatalogItem(Condition condition, ConditionMode mode)
        {
            if (condition == null)
                return null;

            var expectedKind = mode == ConditionMode.Only
                ? StrategyConditionKind.Only
                : StrategyConditionKind.Priority;

            return StrategyConditionCatalog.GetItems(includeNotImplemented: false)
                .FirstOrDefault(item => item.Kind == expectedKind
                    && ConditionsMatch(condition, item.BuildCondition()));
        }

        public static ConditionEditorSelection FindSelection(Condition condition)
        {
            if (condition == null)
                return ConditionEditorSelection.Empty;

            int categoryIndex = ConditionMeta.AllCategories.IndexOf(condition.Category) + 1;
            if (categoryIndex <= 0)
                return ConditionEditorSelection.Empty;

            var operators = ConditionMeta.GetOperators(condition.Category);
            for (int opIndex = 0; opIndex < operators.Count; opIndex++)
            {
                var values = ConditionMeta.GetValues(condition.Category, operators[opIndex]);
                for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
                {
                    var candidate = ConditionMeta.BuildCondition(
                        condition.Category,
                        operators[opIndex],
                        values[valueIndex],
                        isOnly: false);
                    if (ConditionsMatch(condition, candidate))
                        return new ConditionEditorSelection(categoryIndex, opIndex, valueIndex);
                }
            }

            return new ConditionEditorSelection(categoryIndex, 0, 0);
        }

        public static void SaveSelection(
            Strategy strategy,
            bool isCondition1,
            int categoryIndex,
            string operatorLabel,
            string valueLabel,
            ConditionMode mode)
        {
            if (strategy == null)
                throw new ArgumentNullException(nameof(strategy));

            Condition condition = null;
            if (categoryIndex > 0 && categoryIndex <= ConditionMeta.AllCategories.Count)
            {
                var category = ConditionMeta.AllCategories[categoryIndex - 1];
                condition = ConditionMeta.BuildCondition(category, operatorLabel, valueLabel, mode == ConditionMode.Only);
            }

            if (isCondition1)
            {
                strategy.Condition1 = condition;
                strategy.Mode1 = mode;
            }
            else
            {
                strategy.Condition2 = condition;
                strategy.Mode2 = mode;
            }
        }

        private static bool ConditionsMatch(Condition left, Condition right)
        {
            return left.Category == right.Category
                && string.Equals(left.Operator, right.Operator, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeValue(left.Value), NormalizeValue(right.Value), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeValue(object value)
        {
            if (value == null)
                return "";

            if (value is JsonElement json)
            {
                return json.ValueKind switch
                {
                    JsonValueKind.Null => "",
                    JsonValueKind.String => json.GetString() ?? "",
                    JsonValueKind.Number => json.GetDouble().ToString("0.###", CultureInfo.InvariantCulture),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => json.ToString()
                };
            }

            return value switch
            {
                float floatValue => floatValue.ToString("0.###", CultureInfo.InvariantCulture),
                double doubleValue => doubleValue.ToString("0.###", CultureInfo.InvariantCulture),
                decimal decimalValue => decimalValue.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
        }
    }

    public readonly record struct ConditionEditorSelection(int CategoryIndex, int OperatorIndex, int ValueIndex)
    {
        public static ConditionEditorSelection Empty => new(0, 0, 0);
    }

    public readonly record struct RenderedStrategyConditionCatalogItem(
        StrategyConditionCatalogItem Item,
        string Label,
        StrategyConditionTextColor TextColor);
}
