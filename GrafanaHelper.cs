using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlertManager2
{
    public static class GrafanaHelper
    {
        public static string GenerateUrl(Dictionary<string, string> labels)
        {
            string baseUrl = "https://grafana.aquabyte.ai/d/3NFqNEoGz/all-farms-with-details";
            string site = Uri.EscapeDataString(labels.GetValueOrDefault("site_name", "All"));
            string pen = Uri.EscapeDataString(labels.GetValueOrDefault("pen_name", "All"));
            string job = Uri.EscapeDataString("node-exporter");  // hardcoded
            string device = Uri.EscapeDataString(labels.GetValueOrDefault("device_id", "All"));

            return $"{baseUrl}?" +
                   $"orgId=1&refresh=5s" +
                   $"&var-Site={site}" +
                   $"&var-Job={job}" +
                   $"&var-Pen={pen}" +
                   $"&var-Deviceid={device}" +
                   $"&from=now-1d&to=now";
        }


        public static string GenerateBrain2Url(Dictionary<string, string> labels)
        {
            string siteId = labels.GetValueOrDefault("site_id", "unknown");
            return $"http://brain2.internal:3000/site/{siteId}";
        }

    }

}
