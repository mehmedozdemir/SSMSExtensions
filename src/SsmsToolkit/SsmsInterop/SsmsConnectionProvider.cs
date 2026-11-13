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
    /// The only place that reads the active query editor's connection from SSMS internals.
    /// Primary path: ISqlEditorService (SSMS 21+). Fallback: ServiceCache.ScriptFactory (legacy, used by SSMSPlus).
    /// </summary>
    internal sealed class SsmsConnectionProvider
    {
        private readonly IAsyncServiceProvider _serviceProvider;

        public SsmsConnectionProvider(IAsyncServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>Returns the active editor's connection, or null if no connected query editor is active.</summary>
        public async Task<ActiveConnectionInfo> GetActiveAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var info = new ActiveConnectionInfo();
            UIConnectionInfo ui = null;
            IDbConnection ssmsConnection = null;

            // 1) ISqlEditorService
            try
            {
                var editorService = await _serviceProvider.GetServiceAsync(typeof(SSqlEditorService)) as ISqlEditorService;
                if (editorService == null)
                {
                    info.Diagnostics.Add("ISqlEditorService: service not available");
                }
                else
                {
                    var details = editorService.GetActiveEditorDetails();
                    info.EditorCaption = details?.Caption;
                    ssmsConnection = editorService.GetCurrentConnection();
                    if (!string.IsNullOrEmpty(details?.Moniker))
                    {
                        ui = editorService.GetUIConnectionInfoForSpecificQueryEditor(details.Moniker);
                    }
                    info.Source = "ISqlEditorService";
                    info.Diagnostics.Add("ISqlEditorService: connection=" + (ssmsConnection != null) + ", uiConnectionInfo=" + (ui != null));
                }
            }
            catch (Exception ex)
            {
                info.Diagnostics.Add("ISqlEditorService failed: " + ex.GetType().Name + ": " + ex.Message);
                Logger.Warn("ISqlEditorService failed", ex);
            }

            // 2) Legacy ScriptFactory fallback
            if (ui == null)
            {
                try
                {
                    var active = ServiceCache.ScriptFactory?.CurrentlyActiveWndConnectionInfo;
                    ui = active?.UIConnectionInfo;
                    info.Diagnostics.Add("ScriptFactory: activeWnd=" + (active != null) + ", uiConnectionInfo=" + (ui != null));
                    if (ui != null && info.Source == null)
                    {
                        info.Source = "ServiceCache.ScriptFactory";
                    }
                }
                catch (Exception ex)
                {
                    info.Diagnostics.Add("ScriptFactory failed: " + ex.GetType().Name + ": " + ex.Message);
                    Logger.Warn("ScriptFactory failed", ex);
                }
            }

            if (ui == null && ssmsConnection == null)
            {
                return null;
            }

            Fill(info, ui, ssmsConnection);
            return info;
        }

        private static void Fill(ActiveConnectionInfo info, UIConnectionInfo ui, IDbConnection ssmsConnection)
        {
            SqlConnectionStringBuilder ssmsBuilder = null;

            if (ssmsConnection != null)
            {
                info.SsmsConnectionTypeName = ssmsConnection.GetType().FullName;
                info.SsmsConnectionState = SafeGet(() => ssmsConnection.State.ToString());
                info.Database = SafeGet(() => ssmsConnection.Database);
                try
                {
                    // SqlClient strips the password from ConnectionString unless Persist Security Info=true.
                    ssmsBuilder = new SqlConnectionStringBuilder(ssmsConnection.ConnectionString);
                    foreach (string key in ssmsBuilder.Keys)
                    {
                        if (ssmsBuilder.ShouldSerialize(key))
                        {
                            info.ConnectionStringKeys.Add(key);
                        }
                    }
                }
                catch (Exception ex)
                {
                    info.Diagnostics.Add("Could not parse SSMS connection string: " + ex.GetType().Name);
                }
            }

            if (ui != null)
            {
                info.ServerName = ui.ServerName;
                info.UserName = ui.UserName;
                info.AuthenticationType = ui.AuthenticationType;
                try
                {
                    if (ui.AdvancedOptions != null)
                    {
                        foreach (string key in ui.AdvancedOptions.AllKeys)
                        {
                            info.AdvancedOptionKeys.Add(key);
                        }
                        if (string.IsNullOrEmpty(info.Database))
                        {
                            info.Database = ui.AdvancedOptions["DATABASE"];
                        }
                    }
                }
                catch (Exception ex)
                {
                    info.Diagnostics.Add("AdvancedOptions failed: " + ex.GetType().Name);
                }
            }

            if (string.IsNullOrEmpty(info.ServerName) && ssmsBuilder != null)
            {
                info.ServerName = ssmsBuilder.DataSource;
            }

            info.OwnConnectionString = BuildOwnConnectionString(info, ui, ssmsBuilder);
        }

        private static string BuildOwnConnectionString(ActiveConnectionInfo info, UIConnectionInfo ui, SqlConnectionStringBuilder ssmsBuilder)
        {
            try
            {
                // Start from SSMS's own string so Encrypt / TrustServerCertificate / HostNameInCertificate etc. match.
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

                if (ssmsBuilder != null && ssmsBuilder.Authentication != SqlAuthenticationMethod.NotSpecified
                    && ssmsBuilder.Authentication != SqlAuthenticationMethod.SqlPassword)
                {
                    // Entra ID / MFA etc. Opening our own connection could pop an interactive prompt: not in v1.
                    info.AuthenticationDescription = "Unsupported: " + ssmsBuilder.Authentication;
                    return null;
                }

                bool integrated = (ssmsBuilder != null && ssmsBuilder.IntegratedSecurity)
                                  || (ssmsBuilder == null && ui != null && ui.AuthenticationType == 0);
                if (integrated)
                {
                    info.AuthenticationDescription = "Windows authentication";
                    b.IntegratedSecurity = true;
                    b.Remove("User ID");
                    b.Remove("Password");
                    return b.ConnectionString;
                }

                var user = !string.IsNullOrEmpty(ui?.UserName) ? ui.UserName : ssmsBuilder?.UserID;
                var password = GetPassword(ui);
                if (string.IsNullOrEmpty(user) || password == null)
                {
                    info.AuthenticationDescription = "SQL login, but user/password not available";
                    return null;
                }

                info.AuthenticationDescription = "SQL Server authentication";
                b.IntegratedSecurity = false;
                b.UserID = user;
                b.Password = password;
                return b.ConnectionString;
            }
            catch (Exception ex)
            {
                info.Diagnostics.Add("Building own connection string failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static string GetPassword(UIConnectionInfo ui)
        {
            if (ui == null)
            {
                return null;
            }

            var plain = SafeGet(() => ui.Password);
            if (!string.IsNullOrEmpty(plain))
            {
                return plain;
            }

            var secure = SafeGet(() => ui.InMemoryPassword);
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

        private static T SafeGet<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return default; }
        }
    }
}
