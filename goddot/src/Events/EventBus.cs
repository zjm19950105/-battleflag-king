using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleKing.Events
{
    public class EventBus
    {
        private Dictionary<Type, List<Delegate>> _handlers = new();

        public void Subscribe<T>(Action<T> handler) where T : IBattleEvent
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type))
                _handlers[type] = new List<Delegate>();
            _handlers[type].Add(handler);
        }

        public void Publish<T>(T evt) where T : IBattleEvent
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type))
                return;
            foreach (var handler in _handlers[type].Cast<Action<T>>())
                handler(evt);
        }
    }
}
