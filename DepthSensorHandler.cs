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
        // Begge alert‐navnene støttes
        private const string AlertDepthMissing = "depth sensor missing";
        private const string AlertDepthMalfunctioning = "Depth sensor is malfunctioning";

        // HashSet for rask sjekk
        private static readonly HashSet<string> DepthSensorAlerts =
            new() { AlertDepthMissing, AlertDepthMalfunctioning };

        /* -------------------------------------------------------
         *  Tast "d" i hovedmenyen  (batch-fix for BEGGE typer)
         * ----------------------------------------------------- */
        public static async Task HandleBatchFixAsync(AlertFetcher fetcher)
        {
            var depthAlerts = (await fetcher.FetchUnsilencedAlerts())
                              .Where(a => a.Labels.TryGetValue("alertname", out var n) &&
                                          DepthSensorAlerts.Contains(n))
                              .ToList();

            if (depthAlerts.Count == 0)
            {
                Console.WriteLine("✅ Ingen depth-sensor-alarmer (missing / malfunctioning) funnet.");
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var logPath = $"depth_fix_{timestamp}.csv";
            await File.WriteAllTextAsync(logPath, "timestamp,device_id,fix_method\n");

            var failedIds = new List<string>();

            foreach (var alert in depthAlerts)
            {
                if (!alert.Labels.TryGetValue("device_id", out var deviceId) ||
                    !alert.Labels.TryGetValue("port_number", out var portStr) ||
                    !int.TryParse(portStr, out var port))
                    continue;

                var fixedOk = await SmoothRestartAsync(fetcher, deviceId, port);

                var method = fixedOk ? "restart_or_fallback" : "failed";
                await File.AppendAllTextAsync(logPath,
                    $"{DateTime.Now},{deviceId},{method}\n");

                if (!fixedOk)
                    failedIds.Add(deviceId);
            }

            if (failedIds.Count == 0)
            {
                Console.WriteLine("🎉 Alle sensorer ble fikset av smooth/fallback!");
                Console.WriteLine($"📄 Logg: {Path.GetFullPath(logPath)}");
                return;
            }

            await RebootAsync(fetcher, failedIds, logPath);

            Console.WriteLine("🏁 Batch-fix ferdig.");
            Console.WriteLine($"📄 Loggfil: {Path.GetFullPath(logPath)}");
        }

        /* ------------------------------------------------------
         *  Manuell enkelt-alert (interaktiv SSH)
         * ---------------------------------------------------- */
        public static void HandleDepthSensorAlert(Alert alert)
        {
            string portStr = alert.Labels.TryGetValue("port_number", out var p) ? p : "22";
            int port = int.TryParse(portStr, out var pn) ? pn : 22;

            string alertName = alert.Labels.TryGetValue("alertname", out var n) ? n : "(ukjent alert)";
            Console.WriteLine($"🔍 {alertName} – port {port}");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ssh -t -p {port} camera",
                UseShellExecute = true
            });
        }

        /* ------------------------------------------------------
         *  SMOOTH-RESTART + to alternative fallback-stier
         * ---------------------------------------------------- */
        private static async Task<bool> SmoothRestartAsync(
            AlertFetcher fetcher,
            string deviceId,
            int port)
        {
            /* ----------  STI 1: smooth-operator ---------- */
            Console.WriteLine($"⚙️  Prøver restart av smooth-operator på {deviceId} …");
            bool opRestartOk = await RunSshCommand(
                                    port,
                                    "sudo systemctl restart smooth-operator",
                                    10);

            if (opRestartOk)
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                if (!await SensorStillAlerting(fetcher, deviceId))
                {
                    Console.WriteLine($"✅ Alarm løst etter restart av smooth-operator ({deviceId})");
                    return true;
                }

                Console.WriteLine($"🔁 Alarm fortsatt aktiv – kjører fallback på smooth-operator …");
                bool opStop = await RunSshCommand(port, "sudo systemctl stop smooth-operator", 10);
                bool opUpload = await RunSshCommand(port, "sudo smooth-operator aquaino upload --force", 20);
                bool opStart = await RunSshCommand(port, "sudo systemctl start smooth-operator", 10);

                if (opStop && opUpload && opStart)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60));
                    if (!await SensorStillAlerting(fetcher, deviceId))
                    {
                        Console.WriteLine($"✅ Alarm løst etter fallback (smooth-operator) på {deviceId}");
                        return true;
                    }
                }
                Console.WriteLine($"❌ Fallback smooth-operator feilet på {deviceId}");
            }
            else
            {
                Console.WriteLine($"⚠️  smooth-operator restart feilet – prøver smooth på {deviceId}");
            }

            /* ----------  STI 2: smooth ---------- */
            Console.WriteLine($"⚙️  Prøver restart av smooth på {deviceId} …");
            bool smoothRestartOk = await RunSshCommand(
                                        port,
                                        "sudo systemctl restart smooth",
                                        10);

            if (smoothRestartOk)
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                if (!await SensorStillAlerting(fetcher, deviceId))
                {
                    Console.WriteLine($"✅ Alarm løst etter restart av smooth ({deviceId})");
                    return true;
                }

                Console.WriteLine($"🔁 Alarm fortsatt aktiv – kjører fallback på smooth …");
                bool smStop = await RunSshCommand(port, "sudo systemctl stop smooth", 10);
                bool smUpload = await RunSshCommand(port, "sudo /opt/smooth aquaino upload --force", 20);
                bool smStart = await RunSshCommand(port, "sudo systemctl start smooth", 10);

                if (smStop && smUpload && smStart)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60));
                    if (!await SensorStillAlerting(fetcher, deviceId))
                    {
                        Console.WriteLine($"✅ Alarm løst etter fallback (smooth) på {deviceId}");
                        return true;
                    }
                }
                Console.WriteLine($"❌ Fallback smooth feilet på {deviceId}");
            }
            else
            {
                Console.WriteLine($"❌ smooth restart feilet på {deviceId}");
            }

            /* Begge stier feilet */
            return false;
        }

        private static async Task<bool> SensorStillAlerting(AlertFetcher fetcher, string deviceId)
        {
            var alerts = await fetcher.FetchUnsilencedAlerts();
            return alerts.Any(a =>
                   a.Labels.TryGetValue("device_id", out var id) && id == deviceId &&
                   a.Labels.TryGetValue("alertname", out var an) && DepthSensorAlerts.Contains(an));
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
                         .Where(a =>
                                a.Labels.TryGetValue("device_id", out var id) &&
                                deviceIds.Contains(id))
                         .ToList();

            foreach (var a in alerts)
            {
                if (!a.Labels.TryGetValue("port_number", out var portStr) ||
                    !int.TryParse(portStr, out var port))
                    continue;

                Console.WriteLine($"🔄 Reboot på port {port}");
                await RunSshCommand(port, "sudo /sbin/reboot");
                await Task.Delay(300);
            }

            Console.WriteLine("⏳ 10 min vent …");
            await Task.Delay(TimeSpan.FromMinutes(10));

            var after = (await fetcher.FetchUnsilencedAlerts())
                        .Where(a =>
                               a.Labels.TryGetValue("alertname", out var n) &&
                               DepthSensorAlerts.Contains(n) &&
                               a.Labels.TryGetValue("device_id", out var id) &&
                               deviceIds.Contains(id))
                        .Select(a => a.Labels["device_id"])
                        .ToHashSet();

            var resolved = deviceIds.Except(after).ToList();

            await File.AppendAllLinesAsync(logPath,
                resolved.Select(id => $"{DateTime.Now},{id},reboot"));

            Console.WriteLine($"🔧 {resolved.Count} fikset av reboot.");
            Console.WriteLine($"⚠️  {after.Count} fortsatt med feil.");
        }

        /* ------------------------------------------------------
         *  SSH-helper – timeout, logging
         * ---------------------------------------------------- */
        private static async Task<bool> RunSshCommand(
            int port,
            string command,
            int timeoutSeconds = 10)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "ssh.exe"),
                Arguments = $"-t -p {port} camera \"{command}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };

            try
            {
                proc.Start();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                var exitTask = proc.WaitForExitAsync();

                var completed = await Task.WhenAny(
                                    exitTask,
                                    Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

                if (completed != exitTask)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    Console.WriteLine($"⏱️  SSH-timeout på port {port}");
                    return false;
                }

                var stdout = (await stdoutTask).Trim();
                var stderr = (await stderrTask).Trim();

                if (!string.IsNullOrEmpty(stdout))
                    Console.WriteLine($"📤 STDOUT [{port}]: {stdout}");
                if (!string.IsNullOrEmpty(stderr))
                    Console.WriteLine($"📥 STDERR [{port}]: {stderr}");

                if (proc.ExitCode != 0)
                {
                    Console.WriteLine($"❌ SSH-exit {proc.ExitCode} på port {port} (cmd: {command})");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 SSH-exception på port {port}: {ex.Message}");
                return false;
            }
        }
    }
}
