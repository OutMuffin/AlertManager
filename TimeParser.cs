using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlertManager2
{
    public static class TimeParser
    {
        // Eksempel på eksisterende Parse (behold det du allerede har her)
        // Dette er bare en very simple dummy – din versjon kan være mer avansert.
        public static TimeSpan Parse(string input)
        {
            input = input.Trim().ToLower();
            if (input.EndsWith("h"))
                return TimeSpan.FromHours(double.Parse(input[..^1]));
            if (input.EndsWith("m"))
                return TimeSpan.FromMinutes(double.Parse(input[..^1]));
            if (input.EndsWith("d"))
                return TimeSpan.FromDays(double.Parse(input[..^1]));

            // fall-back (eks: “00:30:00”)
            return TimeSpan.Parse(input);
        }

        // NY wrapper – gjør at alle "TryParse"-kall virker
        public static bool TryParse(string input, out TimeSpan result)
        {
            try
            {
                result = Parse(input);
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
        }
    }
}
