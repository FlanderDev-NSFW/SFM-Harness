using System;

namespace Harness.Logic
{
    /// <summary>
    /// Example of the pattern that keeps mods testable: decisions and maths live in plain C# with NO Unity or
    /// Il2Cpp types, so they are unit-tested without starting the game (see tests/Harness.Tests).
    /// The Il2Cpp-facing code (Plugin.cs) stays a thin adapter around it.
    /// </summary>
    public static class GreetingPolicy
    {
        public static string Greeting(string name, string version)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A mod name is required.", nameof(name));
            return name + " " + version + " loaded";
        }
    }
}
