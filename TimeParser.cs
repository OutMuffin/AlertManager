using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlertManager2
{
    public static class TimeParser
    {
        public static TimeSpan Parse(string input)
        {
            if (input.EndsWith("h")) return TimeSpan.FromHours(double.Parse(input.TrimEnd('h')));
            if (input.EndsWith("m")) return TimeSpan.FromMinutes(double.Parse(input.TrimEnd('m')));
            if (input.EndsWith("d")) return TimeSpan.FromDays(double.Parse(input.TrimEnd('d')));
            throw new ArgumentException("Invalid duration format. Use '2h', '30m', or '1d'.");
        }
    }
}
