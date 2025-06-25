using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlertManager2
{
    public class SilenceManager
    {
        private readonly ApiClient _client;

        public SilenceManager(ApiClient client)
        {
            _client = client;
        }

        public async Task SilenceAlert(Alert alert, string scope, TimeSpan duration, string comment)
        {
            var siteSilenceRemove = new List<string> { "alertname", "device_id", "instance", "job", "pen_id", "pen_name", "pen_number", "port_number" };
            var penSilenceRemove = new List<string> { "alertname", "instance_id", "job" };
            var removeKeys = scope == "site" ? siteSilenceRemove : penSilenceRemove;

            var matchers = alert.Labels
                .Where(kvp => !removeKeys.Contains(kvp.Key))
                .Select(kvp => new Matcher { name = kvp.Key, value = kvp.Value, isRegex = false })
                .ToList();

            var silence = new SilencePayload
            {
                matchers = matchers,
                startsAt = DateTime.UtcNow,
                endsAt = DateTime.UtcNow.Add(duration),
                createdBy = "Stefan",
                comment = comment
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(silence);
            await _client.PostJsonAsync("https://alertmanager-primary.aquabyte.ai/api/v2/silences", json);
            Console.WriteLine($"✅ Silenced: {alert.Labels.GetValueOrDefault("alertname")}");
        }

        public async Task TryAutoSilenceEmitteroo(List<Alert> alerts, Alert selectedAlert, string scope, TimeSpan duration, string comment)
        {
            var linkedAlert = alerts.FirstOrDefault(a =>
                a != selectedAlert &&
                a.Labels.GetValueOrDefault("site_name") == selectedAlert.Labels.GetValueOrDefault("site_name") &&
                a.Labels.GetValueOrDefault("pen_name") == selectedAlert.Labels.GetValueOrDefault("pen_name") &&
                a.Labels.GetValueOrDefault("alertname") == "emitteroo expected to be accessible");

            if (selectedAlert.Labels.GetValueOrDefault("alertname") == "camera expected to be accessible" && linkedAlert != null)
            {
                await SilenceAlert(linkedAlert, scope, duration, comment);
            }
        }
    }
}
