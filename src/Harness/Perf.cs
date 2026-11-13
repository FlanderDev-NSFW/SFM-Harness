using System;
using System.Diagnostics;
using UnityEngine;

namespace Harness
{
    /// <summary>
    /// Diagnostics: time spent in each part of the harness, and the game's frame time, summed over a few seconds and logged. Off unless switched on with
    /// game call Harness.HarnessController PerfNow 1. Costs two timestamps per section when on, one bool test when off.
    /// </summary>
    internal static class Perf
    {
        public static bool On;
        public const int Place = 0, Measure = 1, Settle = 2, Bake = 3, Cuts = 4, Refit = 5, Bow = 6, Wire = 7, Chains = 8, Stretches = 9, Survey = 10, Cords = 11, Lock = 12, Rings = 13, BakeMesh = 14, BakeFetch = 15, BakeWorld = 16, RefitWarp = 17, RefitUpload = 18, RefitCuts = 19, HoopLook = 20, Wrap = 21;
        private static readonly string[] Names = { "place", "measure", "settle", "bake", "cuts", "refit", "bow", "wire", "chains", "stretches", "survey", "cords", "lock", "rings", "bakemesh", "bakefetch", "bakeworld", "refitwarp", "refitupload", "refitcuts", "hooplook", "wrap" };
        private static readonly long[] Ticks = new long[Names.Length], Max = new long[Names.Length];
        private static readonly int[] Count = new int[Names.Length];
        private static int _frames, _hitches; private static double _frameSum; private static float _frameMax, _next; private static long _alloc0;

        public static long Now => On ? Stopwatch.GetTimestamp() : 0;

        public static void Add(int id, long start)
        {
            if (!On || start == 0) return;
            long d = Stopwatch.GetTimestamp() - start;
            Ticks[id] += d; Count[id]++; if (d > Max[id]) Max[id] = d;
        }

        public static void Frame(float dt)
        {
            if (!On) return;
            if (_frames == 0) { _alloc0 = GC.GetAllocatedBytesForCurrentThread(); _next = Time.realtimeSinceStartup + 5f; }
            _frames++; _frameSum += dt; if (dt > _frameMax) _frameMax = dt; if (dt > 0.025f) _hitches++;
            if (Time.realtimeSinceStartup < _next) return;
            double f = Stopwatch.Frequency / 1000.0;
            var sb = new System.Text.StringBuilder("[SFM-ACCEPT] perf: frame " + (_frameSum / _frames * 1000).ToString("0.00") + " ms avg, " + (_frameMax * 1000).ToString("0.0") + " max, " + _hitches + " over 25 ms in " + _frames + " frames; harness per frame:");
            for (int i = 0; i < Names.Length; i++)
                if (Count[i] > 0) sb.Append(' ').Append(Names[i]).Append(' ').Append((Ticks[i] / f / _frames).ToString("0.000")).Append(" (max ").Append((Max[i] / f).ToString("0.0")).Append(" x").Append(Count[i]).Append(')');
            sb.Append("; managed alloc ").Append(((GC.GetAllocatedBytesForCurrentThread() - _alloc0) / _frames / 1024.0).ToString("0.0")).Append(" KB/frame");
            Plugin.Logger.LogInfo(sb.ToString());
            Array.Clear(Ticks, 0, Ticks.Length); Array.Clear(Max, 0, Max.Length); Array.Clear(Count, 0, Count.Length);
            _frames = 0; _hitches = 0; _frameSum = 0; _frameMax = 0;
        }
    }
}
