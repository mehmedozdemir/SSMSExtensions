using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.Threading;
using SsmsToolkit.Core;
using SsmsToolkit.SsmsInterop;

namespace SsmsToolkit.Features.Spike
{
    /// <summary>
    /// Phase 0: reads the active connection and proves we can open our own short-lived, read-only connection.
    /// </summary>
    internal static class SpikeProbe
    {
        private const string ProbeSql =
            "SELECT @@SERVERNAME, DB_NAME(), SUSER_SNAME(), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), " +
            "OBJECT_ID(@name), CASE WHEN OBJECT_ID(@name) IS NULL THEN NULL ELSE OBJECTPROPERTYEX(OBJECT_ID(@name), 'BaseType') END";

        /// <summary>Builds a report. Never throws.</summary>
        public static async Task<string> RunAsync(SsmsConnectionProvider provider, string clickedIdentifier, CancellationToken ct)
        {
            var report = new StringBuilder();
            report.AppendLine("SsmsToolkit Phase 0 spike — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (clickedIdentifier != null)
            {
                report.AppendLine("Ctrl+Click identifier: " + clickedIdentifier);
            }
            report.AppendLine();

            ActiveConnectionInfo info;
            try
            {
                info = await provider.GetActiveAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Reading active connection failed", ex);
                report.AppendLine("Reading active connection FAILED: " + ex.Message);
                return report.ToString();
            }

            if (info == null)
            {
                report.AppendLine("No connected query editor is active.");
                Logger.Info("Probe: no active connection");
                return report.ToString();
            }

            var described = info.Describe();
            report.AppendLine("[Active connection]");
            report.AppendLine(described);
            Logger.Info("Probe: active connection\r\n" + described);

            if (!info.CanOpenOwnConnection)
            {
                report.AppendLine("Own connection: SKIPPED (" + info.AuthenticationDescription + ")");
                return report.ToString();
            }

            // Leave the UI thread before any I/O.
            await TaskScheduler.Default;

            report.AppendLine("[Own connection test]");
            var sw = Stopwatch.StartNew();
            try
            {
                using (var conn = new SqlConnection(info.OwnConnectionString))
                {
                    await conn.OpenAsync(ct).ConfigureAwait(false);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = ProbeSql;
                        cmd.CommandTimeout = 15;
                        cmd.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 776)
                        {
                            Value = (object)clickedIdentifier ?? DBNull.Value,
                        });

                        using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                        {
                            if (await r.ReadAsync(ct).ConfigureAwait(false))
                            {
                                report.AppendLine("@@SERVERNAME: " + r.GetValue(0));
                                report.AppendLine("DB_NAME():    " + r.GetValue(1));
                                report.AppendLine("SUSER_SNAME(): " + r.GetValue(2));
                                report.AppendLine("Version:      " + r.GetValue(3));
                                if (clickedIdentifier != null)
                                {
                                    report.AppendLine("OBJECT_ID('" + clickedIdentifier + "'): " + FormatNull(r.GetValue(4)) +
                                                      "  BaseType: " + FormatNull(r.GetValue(5)));
                                }
                            }
                        }
                    }
                }
                report.AppendLine("Own connection: OK (" + sw.ElapsedMilliseconds + " ms)");
                Logger.Info("Probe: own connection OK in " + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex)
            {
                // SqlException messages do not contain the password.
                report.AppendLine("Own connection: FAILED after " + sw.ElapsedMilliseconds + " ms: " + ex.GetType().Name + ": " + ex.Message);
                Logger.Error("Probe: own connection failed", ex);
            }

            return report.ToString();
        }

        private static string FormatNull(object value) => value == null || value is DBNull ? "NULL" : value.ToString();
    }
}
