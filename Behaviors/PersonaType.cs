using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPM_driver.Behaviors
{
    internal enum PersonaType
    {
        FastScanner,
        DeepReader,
        IdleLurker,
        ClickExplorer
    }

    internal static class PersonaHelper
    {
        // FastScanner 60%, DeepReader 25%, IdleLurker 10%, ClickExplorer 5%
        private static readonly (PersonaType persona, double weight)[] _weights =
        {
            (PersonaType.FastScanner, 0.60),
            (PersonaType.DeepReader, 0.25),
            (PersonaType.IdleLurker, 0.10),
            (PersonaType.ClickExplorer, 0.05)
        };

        public static PersonaType GetRandomPersona(Random rng)
        {
            double roll = rng.NextDouble();
            double acc = 0;
            foreach (var w in _weights)
            {
                acc += w.weight;
                if (roll <= acc) return w.persona;
            }
            return _weights[^1].persona;
        }
    }
}