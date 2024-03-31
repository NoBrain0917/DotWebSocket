using System;
using System.Timers;

namespace DotWebSocket
{
    internal class Utils
    {
        internal static string FastSplit(string o, string s1, string s2)
        {
            var f = o.IndexOf(s1, StringComparison.Ordinal) + s1.Length;
            var l = o.IndexOf(s2, StringComparison.Ordinal);
            return o.Substring(f, l - f).Trim();
        }
        
        internal static Timer SetTimer(Action action, int interval)
        {
            var timer = new Timer(interval);
            timer.Elapsed += (s, e) => { 
                timer.Enabled = false;
                action();
                timer.Enabled = true;
            };
            timer.Enabled = true;
            return timer;
        }

        internal static void StopTimer(Timer timer)
        {
            timer.Stop();
            timer.Dispose();
        }
    }
}