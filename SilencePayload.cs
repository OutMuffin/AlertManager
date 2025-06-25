using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlertManager2
{
    public class SilencePayload
    {
        public List<Matcher> matchers { get; set; }
        public DateTime startsAt { get; set; }
        public DateTime endsAt { get; set; }
        public string createdBy { get; set; }
        public string comment { get; set; }
    }
}
