using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlertManager2
{
    public class AlertFetcher
    {
        private readonly ApiClient _client;
        private const string AlertUrl = "https://alertmanager-primary.aquabyte.ai/api/v2/alerts";

        public AlertFetcher(ApiClient client)
        {
            _client = client;
        }

        public async Task<List<Alert>> FetchUnsilencedAlerts()
        {
            string json = await _client.GetAsync(AlertUrl);
            var alerts = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Alert>>(json);
            return alerts
                .Where(a => a.Status?.SilencedBy == null || a.Status.SilencedBy.Count == 0)
                .ToList();
        }

        /// <summary>Return only alerts whose alertname label matches <paramref name="alertName"/>.</summary>
        public List<Alert> FilterByAlertName(List<Alert> alerts, string alertName) =>
            alerts.Where(a => a.Labels.GetValueOrDefault("alertname") == alertName).ToList();

        public void DisplayAlerts(List<Alert> alerts)
        {
            for (int i = 0; i < alerts.Count; i++)
            {
                var labels = alerts[i].Labels;
                Console.WriteLine(
                    $"[{i}] alertname: {labels.GetValueOrDefault("alertname", "N/A")}, " +
                    $"site: {labels.GetValueOrDefault("site_name", "N/A")}, " +
                    $"pen: {labels.GetValueOrDefault("pen_name", "N/A")}");
            }
        }
    }
}
