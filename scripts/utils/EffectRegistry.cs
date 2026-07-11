using BattleArena.scripts.interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BattleArena.scripts.utils
{
    public class EffectRegistry
    {
        private static readonly Dictionary<string, Type> _effectTypes = new();

        static EffectRegistry()
        {
            RegisterAllEffectsInNamespace("BattleArena.scripts.effects");
        }

        private static void RegisterAllEffectsInNamespace(string namespaceName)
        {
            var types = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t =>
                t.IsClass &&
                !t.IsAbstract &&
                typeof(IEffect).IsAssignableFrom(t) &&
                t.Namespace != null &&
                t.Namespace.StartsWith(namespaceName));

            foreach (var type in types)
            {
                _effectTypes[type.Name] = type;
            }
        }

        public static Type GetEffectType(string name)
        {
            if(_effectTypes.TryGetValue(name, out var type)) return type;

            throw new ArgumentException($"Effect type not found: {name}");
        }

        public static IEnumerable<string> GetAllEffectTypeNames() => _effectTypes.Keys;
    }
}
