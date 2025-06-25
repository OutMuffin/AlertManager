using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlertManager2
{
    public class GrafanaClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _dataSourceId;   // Prometheus datasource ID in Grafana

        public GrafanaClient(string baseUrl, string apiKey, string dataSourceId)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _dataSourceId = dataSourceId;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>Query a Prometheus metric via Grafana proxy.</summary>
        public async Task<JsonElement?> QueryPrometheusAsync(string promQL)
        {
            string now = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds().ToString();
            string url = $"{_baseUrl}/api/datasources/proxy/{_dataSourceId}/api/v1/query" +
                           $"?query={Uri.EscapeDataString(promQL)}&time={now}";

            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            if (doc.RootElement.GetProperty("status").GetString() != "success") return null;

            var data = doc.RootElement.GetProperty("data").GetProperty("result");
            return data.GetArrayLength() > 0 ? data[0] : null;
        }

        /// <summary>Return the RUTX port number for a given site & pen, or null.</summary>
        public async Task<int?> GetRutxPortAsync(string siteId, string penId)
        {
            // Adjust metric name if yours is different:
            string query = $"rutx_port_number{{site_id=\"{siteId}\",pen_id=\"{penId}\"}}";
            var res = await QueryPrometheusAsync(query);
            if (res == null) return null;

            // result format: { "metric": {...}, "value": [ <timestamp>, "<port>" ] }
            var valueArr = res.Value.GetProperty("value");
            if (valueArr.GetArrayLength() != 2) return null;
            if (int.TryParse(valueArr[1].GetString(), out int port)) return port;

            return null;
        }
    }
}
