using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DigitalMusicAnalysis
{
    public static class stpBench
    {
        static StringBuilder sbuild = new StringBuilder().AppendLine($"Name [ Milliseconds | Ticks ]\n{new string('~', 40)}").AppendLine($"System Frequency Rate (f/s): {Stopwatch.Frequency}");

        public static void addtime(string name, TimeSpan span)
        {
            sbuild.AppendLine($"{name} [ {span.TotalMilliseconds} | {span.Ticks} ]");
        }

        public static void saveresults(string filepath)
        {
            File.WriteAllText(filepath, sbuild.ToString());
        }
    }
}
