using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlertManager2
{
    public class Alert
    {
        [Newtonsoft.Json.JsonProperty("labels")]
        public Dictionary<string, string> Labels { get; set; }

        [Newtonsoft.Json.JsonProperty("annotations")]
        public Dictionary<string, string> Annotations { get; set; }

        [Newtonsoft.Json.JsonProperty("startsAt")]
        public DateTime StartsAt { get; set; }

        [Newtonsoft.Json.JsonProperty("endsAt")]
        public DateTime EndsAt { get; set; }

        [Newtonsoft.Json.JsonProperty("status")]
        public Status Status { get; set; }
    }
}
