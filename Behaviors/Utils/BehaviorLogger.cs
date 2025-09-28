using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPM_driver.Behaviors.Utils
{
    internal static class BehaviorLogger
    {
        public static void Log(string message)
        {
            Console.WriteLine($"[Behavior] {DateTime.UtcNow:O} {message}");
        }

        public static void LogAction(string action, string detail = "")
        {
            Console.WriteLine($"[Action] {DateTime.UtcNow:O} {action} {detail}");
        }
    }
}