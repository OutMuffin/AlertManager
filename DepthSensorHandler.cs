using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AlertManager2
{
    internal static class DepthSensorHandler
    {
        private const string AlertDepthMissing = "depth sensor missing";

        /* -------------------------------------------------------
         *  Tast "d" i hovedmenyen
         * ----------------------------------------------------- */
        public static async Task HandleBatchFixAsync(AlertFetcher fetcher)
        {
            var depthAlerts = (await fetcher.FetchUnsilencedAlerts())
                              .Where(a => a.Labels.TryGetValue("alertname", out var n) &&
                                          n == AlertDepthMissing)
                              .ToList();

            if (depthAlerts.Count == 0)
            {
                Console.WriteLine("✅ Ingen depth-sensor-alarmer funnet.");
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var logPath = $"depth_fix_{timestamp}.csv";
            await File.WriteAllTextAsync(logPath, "timestamp,device_id,fix_type\n");

            var allIds = depthAlerts.Select(a => a.Labels["device_id"]).ToList();
            await SmoothRestartAsync(fetcher, depthAlerts, logPath);

            var stillFailing = (await fetcher.FetchUnsilencedAlerts())
                               .Where(a => a.Labels.TryGetValue("alertname", out var n) &&
                                           n == AlertDepthMissing)
                               .Select(a => a.Labels["device_id"])
                               .Where(allIds.Contains)
                               .ToHashSet();

            if (stillFailing.Count == 0)
            {
                Console.WriteLine("🎉 Alle sensorer ble fikset av smooth-restart!");
                Console.WriteLine($"📄 Logg: {Path.GetFullPath(logPath)}");
                return;
            }

            await RebootAsync(fetcher, stillFailing.ToList(), logPath);

            Console.WriteLine("🏁 Batch-fix ferdig.");
            Console.WriteLine($"📄 Loggfil: {Path.GetFullPath(logPath)}");
        }

        /* ------------------------------------------------------
         *  Manuell enkelt-alert
         * ---------------------------------------------------- */
        public static void HandleDepthSensorAlert(Alert alert)
        {
            string portStr = alert.Labels.TryGetValue("port_number", out var p) ? p : "22";
            int port = int.TryParse(portStr, out var pn) ? pn : 22;

            Console.WriteLine($"🔍 Depth sensor missing – port {port}");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ssh camera -p {port}",
                UseShellExecute = true
            });
        }

        /* ------------------------------------------------------
         *  SMOOTH-RESTART med smart fallback
         * ---------------------------------------------------- */
        private static async Task SmoothRestartAsync(
            AlertFetcher fetcher,
            List<Alert> alerts,
            string logPath)
        {
            foreach (var a in alerts)
            {
                if (!a.Labels.TryGetValue("port_number", out var portStr) ||
                    !int.TryParse(portStr, out var port))
                    continue;

                string deviceId = a.Labels["device_id"];

                // --- prøv først "smooth" -------------------------
                Console.WriteLine($"🔧 restart smooth på port {port}");
                bool ok = await RunSshCommand(port, "sudo systemctl restart smooth");

                // hvis smooth feiler, prøv smooth-operator
                if (!ok)
                {
                    Console.WriteLine($"⚠️  smooth feilet – prøver smooth-operator på port {port}");
                    ok = await RunSshCommand(port, "sudo systemctl restart smooth-operator");
                }

                if (!ok)
                    Console.WriteLine($"❌ Ingen restart-kommando fungerte på port {port}");

                await Task.Delay(300);
            }

            Console.WriteLine("⏳ 1 min vent etter restart …");
            await Task.Delay(TimeSpan.FromMinutes(1));

            var unresolved = (await fetcher.FetchUnsilencedAlerts())
                             .Where(a => a.Labels.TryGetValue("alertname", out var n) &&
                                         n == AlertDepthMissing)
                             .Select(a => a.Labels["device_id"])
                             .ToHashSet();

            var resolved = alerts
                           .Select(a => a.Labels["device_id"])
                           .Except(unresolved)
                           .ToList();

            await File.AppendAllLinesAsync(logPath,
                resolved.Select(id => $"{DateTime.Now},{id},smooth"));

            Console.WriteLine($"✅ {resolved.Count} fikset av restart.");
        }

        /* ------------------------------------------------------
         *  FULL REBOOT
         * ---------------------------------------------------- */
        private static async Task RebootAsync(
            AlertFetcher fetcher,
            List<string> deviceIds,
            string logPath)
        {
            var alerts = (await fetcher.FetchUnsilencedAlerts())
                         .Where(a => deviceIds.Contains(a.Labels["device_id"]))
                         .ToList();

            foreach (var a in alerts)
            {
                if (!a.Labels.TryGetValue("port_number", out var portStr) ||
                    !int.TryParse(portStr, out var port))
                    continue;

                Console.WriteLine($"🔄 reboot på port {port}");
                await RunSshCommand(port, "sudo /sbin/reboot");
                await Task.Delay(300);
            }

            Console.WriteLine("⏳ 10 min vent …");
            await Task.Delay(TimeSpan.FromMinutes(10));

            var after = (await fetcher.FetchUnsilencedAlerts())
                        .Where(a => a.Labels.TryGetValue("alertname", out var n) &&
                                    n == AlertDepthMissing &&
                                    deviceIds.Contains(a.Labels["device_id"]))
                        .Select(a => a.Labels["device_id"])
                        .ToHashSet();

            var resolved = deviceIds.Except(after).ToList();

            await File.AppendAllLinesAsync(logPath,
                resolved.Select(id => $"{DateTime.Now},{id},reboot"));

            Console.WriteLine($"🔧 {resolved.Count} fikset av reboot.");
            Console.WriteLine($"⚠️  {after.Count} fortsatt med feil.");
        }

        /* ------------------------------------------------------
         *  SSH helper – timeout 10 s, returnerer true/false
         * ---------------------------------------------------- */
        private static async Task<bool> RunSshCommand(
            int port,
            string command,
            int timeoutSeconds = 10)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh",
                Arguments = $"-p {port} camera \"{command}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };

            try
            {
                proc.Start();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                var exitTask = proc.WaitForExitAsync();            // <─ lagres her

                var completed = await Task.WhenAny(
                                    exitTask,
                                    Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

                if (completed != exitTask)                           // timeout
                {
                    try { proc.Kill(true); } catch { /* ignorer */ }
                    Console.WriteLine($"⏱️  SSH-timeout på port {port}");
                    return false;
                }

                bool ok = proc.ExitCode == 0;
                if (!ok)
                {
                    var err = await stderrTask;
                    Console.WriteLine($"❌ SSH-feil (exit {proc.ExitCode}) på port {port}: {err.Trim()}");
                }
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Feil ved SSH på port {port}: {ex.Message}");
                return false;
            }
        }

    }
}
