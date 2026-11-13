using System;
using System.Data;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Core;

namespace SsmsToolkit.SsmsInterop
{
    /// <summary>
    /// The only place that reads query editor connections from SSMS internals.
    /// Primary path: ISqlEditorService (SSMS 21+), for a specific editor when its moniker is known.
    /// Fallback: ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo (legacy).
    /// </summary>
    internal sealed class SsmsConnectionProvider
    {
        private readonly IAsyncServiceProvider _serviceProvider;

        public SsmsConnectionProvider(IAsyncServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <param name="documentMoniker">The editor's document path (ITextDocument.FilePath), or null for the active editor.</param>
        /// <returns>The connection, or null if the editor is not connected.</returns>
        public async Task<EditorConnection> GetAsync(string documentMoniker)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var info = new EditorConnection();
            UIConnectionInfo ui = null;
            IDbConnection ssmsConnection = null;

            try
            {
                if (await _serviceProvider.GetServiceAsync(typeof(SSqlEditorService)) is ISqlEditorService editorService)
                {
                    if (!string.IsNullOrEmpty(documentMoniker))
                    {
                        ui = TryGet(() => editorService.GetUIConnectionInfoForSpecificQueryEditor(documentMoniker));
                        ssmsConnection = TryGet(() => editorService.GetConnectionForSpecificQueryEditor(documentMoniker));
                        info.Diagnostics.Add("by-moniker ui=" + (ui != null) + " conn=" + (ssmsConnection != null));
                    }

                    if (ui == null && ssmsConnection == null)
                    {
                        var moniker = TryGet(() => editorService.GetActiveEditorDetails()?.Moniker);
                        ssmsConnection = TryGet(() => editorService.GetCurrentConnection());
                        if (!string.IsNullOrEmpty(moniker))
                        {
                            ui = TryGet(() => editorService.GetUIConnectionInfoForSpecificQueryEditor(moniker));
                        }
                        info.Diagnostics.Add("active-editor ui=" + (ui != null) + " conn=" + (ssmsConnection != null));
                    }
                    info.Source = "ISqlEditorService";
                }
                else
                {
                    info.Diagnostics.Add("ISqlEditorService unavailable");
                }
            }
            catch (Exception ex)
            {
                info.Diagnostics.Add("ISqlEditorService failed: " + ex.GetType().Name);
                Logger.Warn("ISqlEditorService failed", ex);
            }

            if (ui == null)
            {
                try
                {
                    ui = ServiceCache.ScriptFactory?.CurrentlyActiveWndConnectionInfo?.UIConnectionInfo;
                    if (ui != null)
                    {
                        info.Source = info.Source == null ? "ScriptFactory" : info.Source + "+ScriptFactory";
                    }
                }
                catch (Exception ex)
                {
                    info.Diagnostics.Add("ScriptFactory failed: " + ex.GetType().Name);
                    Logger.Warn("ScriptFactory failed", ex);
                }
            }

            if (ui == null && ssmsConnection == null)
            {
                Logger.Debug("No editor connection. " + info);
                return null;
            }

            Fill(info, ui, ssmsConnection);
            Logger.Debug("Editor connection: " + info);
            return info;
        }

        private static void Fill(EditorConnection info, UIConnectionInfo ui, IDbConnection ssmsConnection)
        {
            SqlConnectionStringBuilder ssmsBuilder = null;
            info.SsmsHandle = ui;

            if (ssmsConnection != null)
            {
                info.Database = TryGet(() => ssmsConnection.Database);
                try
                {
                    // SSMS's string includes the password for SQL logins: never log it.
                    ssmsBuilder = new SqlConnectionStringBuilder(ssmsConnection.ConnectionString);
                }
                catch (Exception ex)
                {
                    info.Diagnostics.Add("unparsable SSMS connection string: " + ex.GetType().Name);
                }
            }

            if (ui != null)
            {
                info.ServerName = ui.ServerName;
                info.AuthenticationType = ui.AuthenticationType;
                if (string.IsNullOrEmpty(info.Database))
                {
                    info.Database = TryGet(() => ui.AdvancedOptions?["DATABASE"]);
                }
            }

            if (string.IsNullOrEmpty(info.ServerName))
            {
                info.ServerName = ssmsBuilder?.DataSource;
            }
            if (string.IsNullOrEmpty(info.Database))
            {
                info.Database = ssmsBuilder?.InitialCatalog;
            }

            info.OwnConnectionString = BuildOwnConnectionString(info, ui, ssmsBuilder);
        }

        private static string BuildOwnConnectionString(EditorConnection info, UIConnectionInfo ui, SqlConnectionStringBuilder ssmsBuilder)
        {
            try
            {
                // Start from SSMS's own string so Encrypt / TrustServerCertificate / HostNameInCertificate match.
                var b = ssmsBuilder != null
                    ? new SqlConnectionStringBuilder(ssmsBuilder.ConnectionString)
                    : new SqlConnectionStringBuilder { DataSource = info.ServerName };

                if (string.IsNullOrEmpty(b.DataSource))
                {
                    b.DataSource = info.ServerName;
                }
                if (!string.IsNullOrEmpty(info.Database))
                {
                    b.InitialCatalog = info.Database;
                }
                b.ApplicationName = "SsmsToolkit";
                b.ConnectTimeout = 15;
                b.PersistSecurityInfo = false;
                b.MultipleActiveResultSets = false;

                if (ssmsBuilder != null && ssmsBuilder.Authentication != SqlAuthenticationMethod.NotSpecified
                    && ssmsBuilder.Authentication != SqlAuthenticationMethod.SqlPassword)
                {
                    // Entra ID / MFA: our own connection could pop an interactive prompt. Not in v1.
                    info.AuthenticationDescription = ssmsBuilder.Authentication.ToString();
                    return null;
                }

                bool integrated = (ssmsBuilder != null && ssmsBuilder.IntegratedSecurity)
                                  || (ssmsBuilder == null && ui != null && ui.AuthenticationType == 0);
                if (integrated)
                {
                    info.AuthenticationDescription = "Windows";
                    b.IntegratedSecurity = true;
                    b.Remove("User ID");
                    b.Remove("Password");
                    return b.ConnectionString;
                }

                var user = !string.IsNullOrEmpty(ui?.UserName) ? ui.UserName : ssmsBuilder?.UserID;
                var password = !string.IsNullOrEmpty(ssmsBuilder?.Password) ? ssmsBuilder.Password : GetPassword(ui);
                if (string.IsNullOrEmpty(user) || password == null)
                {
                    info.AuthenticationDescription = "SQL login without available password";
                    return null;
                }

                info.AuthenticationDescription = "SQL Server";
                b.IntegratedSecurity = false;
                b.UserID = user;
                b.Password = password;
                return b.ConnectionString;
            }
            catch (Exception ex)
            {
                info.Diagnostics.Add("building own connection string failed: " + ex.GetType().Name);
                return null;
            }
        }

        private static string GetPassword(UIConnectionInfo ui)
        {
            if (ui == null)
            {
                return null;
            }

            var plain = TryGet(() => ui.Password);
            if (!string.IsNullOrEmpty(plain))
            {
                return plain;
            }

            var secure = TryGet(() => ui.InMemoryPassword);
            return secure != null && secure.Length > 0 ? ToPlain(secure) : null;
        }

        private static string ToPlain(SecureString secure)
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                }
            }
        }

        private static T TryGet<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return default; }
        }
    }
}
