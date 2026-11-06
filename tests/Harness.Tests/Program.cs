using System;
using Harness.Logic;

namespace Harness.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static int Main()
        {
            Check(GreetingPolicy.Greeting("Demo", "1.0") == "Demo 1.0 loaded", "greeting text");
            try { GreetingPolicy.Greeting(" ", "1.0"); Check(false, "blank name rejected"); }
            catch (ArgumentException) { Check(true, "blank name rejected"); }

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
