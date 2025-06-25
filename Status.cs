using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AlertManager2
{
    public class Status
    {
        [Newtonsoft.Json.JsonProperty("state")]
        public string State { get; set; }

        [Newtonsoft.Json.JsonProperty("silencedBy")]
        public List<string> SilencedBy { get; set; }
    }
}
