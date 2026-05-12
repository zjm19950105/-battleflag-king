using Godot;

namespace BattleKing.Ui
{
    public static class BattleLogTextRenderer
    {
        private static readonly Color PlayerColor = new(1.0f, 0.28f, 0.24f);
        private static readonly Color EnemyColor = new(0.35f, 0.65f, 1.0f);

        public static void Append(RichTextLabel label, string message)
        {
            if (label == null)
                return;

            AppendMarkedText(label, message ?? "");
            label.AppendText("\n");
        }

        private static void AppendMarkedText(RichTextLabel label, string text)
        {
            int index = 0;
            while (index < text.Length)
            {
                int markerStart = text.IndexOf("{{", index, System.StringComparison.Ordinal);
                if (markerStart < 0)
                {
                    label.AppendText(text[index..]);
                    return;
                }

                if (markerStart > index)
                    label.AppendText(text[index..markerStart]);

                bool isPlayer = HasMarker(text, markerStart, 'P');
                bool isEnemy = HasMarker(text, markerStart, 'E');
                if (!isPlayer && !isEnemy)
                {
                    label.AppendText("{{");
                    index = markerStart + 2;
                    continue;
                }

                int nameStart = markerStart + 4;
                int markerEnd = text.IndexOf("}}", nameStart, System.StringComparison.Ordinal);
                if (markerEnd < 0)
                {
                    label.AppendText(text[markerStart..]);
                    return;
                }

                label.PushColor(isPlayer ? PlayerColor : EnemyColor);
                label.AppendText(text[nameStart..markerEnd]);
                label.Pop();
                index = markerEnd + 2;
            }
        }

        private static bool HasMarker(string text, int start, char side)
        {
            return start + 3 < text.Length
                && text[start] == '{'
                && text[start + 1] == '{'
                && text[start + 2] == side
                && text[start + 3] == ':';
        }
    }
}
