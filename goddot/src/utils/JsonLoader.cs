using System.IO;
using System.Text.Json;

namespace BattleKing.Utils
{
    public static class JsonLoader
    {
        public static T Load<T>(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json);
        }

        public static T Load<T>(string filePath, JsonSerializerOptions options)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, options);
        }
    }
}
