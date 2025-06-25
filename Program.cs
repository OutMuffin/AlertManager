using AlertManager2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Diagnostics; // NEW: required for DepthSensorHandler

class Program
{
    /* -------- alert-name constants (avoid typos) ---------------- */
    private const string AlertCamera = "camera expected to be accessible";
    private const string AlertFlicker = "lights are flickering";
    private const string AlertTilt = "Camera tilted +- 30 degrees";
    private const string AlertWinch = "Winch status errors";
    private const string AlertDepthMissing = "depth sensor missing"; // NEW

    static async Task Main()
    {
        var client = new ApiClient("aquabyte:Kjell2024");
        var alertFetcher = new AlertFetcher(client);
        var silenceManager = new SilenceManager(client);

        string? currentFilter = null;   // null = no filter (show all)

        while (true)
        {
            /* --- fetch & apply view filter ----------------------- */
            var allAlerts = await alertFetcher.FetchUnsilencedAlerts();
            var alerts = string.IsNullOrEmpty(currentFilter)
                            ? allAlerts
                            : alertFetcher.FilterByAlertName(allAlerts, currentFilter);

            if (alerts.Count == 0)
            {
                WriteColored("No unsilenced alerts match current view. (Use 'a' or 'exit' to reset filter.)",
                             ConsoleColor.Yellow);
                await Task.Delay(500);
                continue;
            }

            if (!string.IsNullOrEmpty(currentFilter))
                WriteColored($"[FILTER: {currentFilter}]", ConsoleColor.Magenta);

            alertFetcher.DisplayAlerts(alerts);

            /* --- prompt ----------------------------------------- */
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(
                "\nSelect alert [index] — c=camera view, a=all, d=depth reboot, exit/x=clear filter, "
              + "f=silence flicker, t=silence tilt, w=silence winch, r=refresh, q=quit: ");

            Console.ResetColor();

            string input = Console.ReadLine().Trim().ToLower();

            /* --- global commands -------------------------------- */

            if (input == "q") return;   // quit
            if (input == "r") continue; // refresh (retain filter)

            if (input == "exit" || input == "x")
            {
                if (currentFilter != null)
                    WriteColored("🔁 Filter cleared. Showing all alerts.", ConsoleColor.Cyan);
                else
                    WriteColored("No filter active to exit from.", ConsoleColor.Yellow);

                currentFilter = null;
                continue;
            }

            if (input == "c")
            {
                currentFilter = AlertCamera;
                WriteColored("🔍 View filtered: showing only camera alerts.", ConsoleColor.Cyan);
                continue;
            }

            if (input == "a")
            {
                currentFilter = null;
                WriteColored("🔁 View reset: showing all alerts.", ConsoleColor.Cyan);
                continue;
            }
            if (input == "d")
            {
                await DepthSensorHandler.HandleBatchFixAsync(alertFetcher);
                continue;
            }



            /* --- bulk-silence shortcuts ------------------------- */

            if (input == "f")
            {
                await BulkSilenceWithRecheck(alertFetcher, alerts, silenceManager,
                                             AlertFlicker, "Quick silence for flickering lights",
                                             TimeSpan.FromMinutes(5));
                continue;
            }

            if (input == "t")
            {
                await BulkSilenceWithRecheck(alertFetcher, alerts, silenceManager,
                                             AlertTilt, "Quick silence for camera tilt",
                                             TimeSpan.FromMinutes(5));
                continue;
            }

            if (input == "w")
            {
                await BulkSilenceWithRecheck(alertFetcher, alerts, silenceManager,
                                             AlertWinch, "Quick silence for winch errors",
                                             TimeSpan.FromMinutes(5));
                continue;
            }


            /* --- individual alert flow ------------------------- */

            if (!int.TryParse(input, out int idx) || idx < 0 || idx >= alerts.Count)
            {
                WriteColored("⚠️  Invalid selection — try again.\n", ConsoleColor.Yellow);
                continue;
            }

            var selected = alerts[idx];

            // NEW: Automatic handling for depth sensor alerts ----------------------
            if (selected.Labels.TryGetValue("alertname", out var alertNm) &&
                alertNm.Equals(AlertDepthMissing, StringComparison.OrdinalIgnoreCase))
            {
                // Call the DepthSensorHandler before showing details.
                DepthSensorHandler.HandleDepthSensorAlert(selected);
            }
            // ----------------------------------------------------------------------

            await ShowAlertDetails(selected);   // <-- await async method

            Console.Write("Press [Enter] to silence, or type 'back' to cancel: ");
            if (Console.ReadLine().Trim().Equals("back", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("\n↩ Back to alert list...\n");
                continue;
            }

            Console.Write("\nSilence scope (site/pen): ");
            string scope = Console.ReadLine().Trim().ToLower();

            Console.Write("Enter silence duration (e.g., 2h, 30m, 1d): ");
            string durStr = Console.ReadLine().Trim().ToLower();
            TimeSpan duration;
            try { duration = TimeParser.Parse(durStr); }
            catch { WriteColored("❌ Invalid duration format.", ConsoleColor.Red); continue; }

            Console.Write("Enter a comment for the silence: ");
            string comment = Console.ReadLine().Trim();

            await silenceManager.SilenceAlert(selected, scope, duration, comment);
            await silenceManager.TryAutoSilenceEmitteroo(alerts, selected, scope, duration, comment);
            WriteColored("✅ Silenced alert successfully.\n", ConsoleColor.Green);
        }
    }

    /* --- helper: generic bulk-silence by alertname -------------- */
    static async Task BulkSilenceWithRecheck(AlertFetcher fetcher,
                                             List<Alert> view,
                                             SilenceManager mgr,
                                             string alertName,
                                             string comment,
                                             TimeSpan silenceDuration)
    {
        var initial = fetcher.FilterByAlertName(view, alertName);
        if (initial.Count == 0)
        {
            WriteColored($"No '{alertName}' alerts found in current view.", ConsoleColor.Yellow);
            return;
        }

        // Silence all now
        foreach (var a in initial)
            await mgr.SilenceAlert(a, "pen", silenceDuration, comment);
        WriteColored($"🔕 Silenced {initial.Count} '{alertName}' alerts for {silenceDuration.TotalMinutes} min.\n" +
                     "⏳ Venter 10 minutter for å se om noen dukker opp igjen…",
                     ConsoleColor.Cyan);

        await Task.Delay(TimeSpan.FromMinutes(10));

        // Fetch alerts again
        var after10 = await fetcher.FetchUnsilencedAlerts();
        var stillFlickering = fetcher.FilterByAlertName(after10, alertName);

        if (stillFlickering.Count == 0)
        {
            WriteColored("✅ Ingen 'lights are flickering'‑alarmer dukket opp igjen etter 10 min.", ConsoleColor.Green);
            return;
        }

        WriteColored("⚠️ Følgende 'lights are flickering'‑alarmer er fortsatt aktive etter 10 min:", ConsoleColor.Yellow);
        foreach (var a in stillFlickering)
            Console.WriteLine($" - {a.Labels["device_id"]} ({a.Labels["pen_name"]})");
    }

    /* --- helper: show details & camera reachability ------------- */
    static async Task ShowAlertDetails(Alert alert)
    {
        WriteColored("\nYou selected:", ConsoleColor.Cyan);
        foreach (var kv in alert.Labels)
            Console.WriteLine($"{kv.Key}: {kv.Value}");
        Console.WriteLine();

        WriteColored($"📊 Grafana URL: {GrafanaHelper.GenerateUrl(alert.Labels)}", ConsoleColor.Green);
        WriteColored($"🧠 Brain2  URL: {GrafanaHelper.GenerateBrain2Url(alert.Labels)}", ConsoleColor.Green);

        /* --- camera-reachability check -------------------------- */
        if (alert.Labels.TryGetValue("alertname", out var name) &&
            name == AlertCamera &&
            alert.Labels.TryGetValue("instance", out var instance) &&
            instance.Contains(':'))
        {
            var parts = instance.Split(':');
            var host = parts[0];
            if (int.TryParse(parts[1], out int port))
            {
                bool reachable = await IsPortOpenAsync(host, port, TimeSpan.FromSeconds(2));
                var status = reachable ? "✅ Camera accessible" : "❌ Camera not accessible";
                WriteColored(status, reachable ? ConsoleColor.Green : ConsoleColor.Red);
            }
        }
    }

    /* --- helper: coloured console line -------------------------- */
    static void WriteColored(string msg, ConsoleColor color)
    {
        var orig = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ForegroundColor = orig;
    }

    /* --- helper: quick TCP connectivity test -------------------- */
    static async Task<bool> IsPortOpenAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeout));
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}