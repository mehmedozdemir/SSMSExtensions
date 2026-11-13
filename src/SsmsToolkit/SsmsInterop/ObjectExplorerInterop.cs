using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio.Shell;
using SsmsToolkit.Core;
using SsmsToolkit.Data.Model;

namespace SsmsToolkit.SsmsInterop
{
    /// <summary>A table or view selected in Object Explorer, with a connection we can use.</summary>
    internal sealed class ObjectExplorerTarget
    {
        public EditorConnection Connection { get; set; }
        public ObjectName Name { get; set; }
    }

    /// <summary>
    /// Object Explorer integration (undocumented SSMS internals — kept here, fails safe):
    /// reading the selected table/view, and adding "Inspect Table" to its context menu.
    /// </summary>
    internal static class ObjectExplorerInterop
    {
        private const string MenuItemKey = "SsmsToolkit.InspectTable";
        private const string DashboardMenuItemKey = "SsmsToolkit.DatabaseDashboard";

        // SQL Server Database Engine server type (RegSvrEnum), used to open a connected query window.
        private static readonly Guid SqlServerType = new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907");

        private static readonly ConditionalWeakTable<object, object> MenusWithItem = new ConditionalWeakTable<object, object>();
        private static readonly ConditionalWeakTable<object, object> MenusWithDashboard = new ConditionalWeakTable<object, object>();
        private static Action<ObjectExplorerTarget> _onInspect;
        private static Action<EditorConnection> _onDashboard;
        private static bool _menuFailed;

        /// <summary>True when a tool window (e.g. Object Explorer), not a document, has focus.</summary>
        public static bool IsToolWindowActive()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var window = ServiceCache.ExtensibilityModel?.ActiveWindow;
                return window != null && window.Document == null;
            }
            catch (COMException)
            {
                return false;
            }
        }

        /// <summary>The selected Object Explorer node, if it is a table or view; otherwise null.</summary>
        public static async Task<ObjectExplorerTarget> GetSelectedTableAsync(IAsyncServiceProvider services)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!(await services.GetServiceAsync(typeof(IObjectExplorerService)) is IObjectExplorerService explorer))
            {
                return null;
            }

            explorer.GetSelectedNodes(out int count, out INodeInformation[] nodes);
            return count == 1 && nodes != null && nodes.Length > 0 ? ToTarget(nodes[0]) : null;
        }

        /// <summary>
        /// The database of the selected Object Explorer node (the database itself or any object under it), with a
        /// connection to it; null when the node is not inside a database.
        /// </summary>
        public static async Task<EditorConnection> GetSelectedDatabaseAsync(IAsyncServiceProvider services)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!(await services.GetServiceAsync(typeof(IObjectExplorerService)) is IObjectExplorerService explorer))
            {
                return null;
            }

            explorer.GetSelectedNodes(out int count, out INodeInformation[] nodes);
            return count == 1 && nodes != null && nodes.Length > 0 ? ToDatabaseConnection(nodes[0]) : null;
        }

        /// <summary>
        /// Adds "Inspect Table" to table/view context menus and "Database Dashboard" to database context menus.
        /// Safe to call once; failures only disable the menu items.
        /// </summary>
        public static void RegisterContextMenu(Action<ObjectExplorerTarget> onInspect, Action<EditorConnection> onDashboard)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _onInspect = onInspect;
            _onDashboard = onDashboard;
            try
            {
                if (!(ServiceCache.ServiceProvider?.GetService(typeof(IContextService)) is IContextService contextService)
                    || contextService.ObjectExplorerContext == null)
                {
                    Logger.Warn("Object Explorer context service not available; context menu item not added.");
                    return;
                }

                contextService.ObjectExplorerContext.CurrentContextChanged += OnCurrentContextChanged;
                Logger.Info("Object Explorer context menu integration registered");
            }
            catch (Exception ex)
            {
                Logger.Warn("Object Explorer context menu integration failed", ex);
            }
        }

        private static void OnCurrentContextChanged(object sender, NodesChangedEventArgs e)
        {
            if (_menuFailed || e?.ChangedNodes == null)
            {
                return;
            }

            try
            {
                foreach (var node in e.ChangedNodes.OfType<INodeInformation>())
                {
                    if (IsTableOrView(node.UrnPath))
                    {
                        if (node.GetService(typeof(IMenuHandler)) is HierarchyObject menu && !MenusWithItem.TryGetValue(menu, out _))
                        {
                            menu.AddChild(MenuItemKey, new InspectTableMenuItem());
                            MenusWithItem.Add(menu, null);
                        }
                    }
                    else if (IsDatabase(node.UrnPath) && _onDashboard != null)
                    {
                        if (node.GetService(typeof(IMenuHandler)) is HierarchyObject menu && !MenusWithDashboard.TryGetValue(menu, out _))
                        {
                            menu.AddChild(DashboardMenuItemKey, new DashboardMenuItem());
                            MenusWithDashboard.Add(menu, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Never retry on every selection change: one log entry, then the menu item is simply absent.
                _menuFailed = true;
                Logger.Warn("Adding the Object Explorer menu item failed; the item is disabled for this session", ex);
            }
        }

        private static bool IsTableOrView(string urnPath) =>
            urnPath != null && (urnPath.EndsWith("/Table", StringComparison.Ordinal) || urnPath.EndsWith("/View", StringComparison.Ordinal));

        private static bool IsDatabase(string urnPath) =>
            urnPath != null && urnPath.EndsWith("Server/Database", StringComparison.Ordinal);

        private static EditorConnection ToDatabaseConnection(INodeInformation node)
        {
            if (string.IsNullOrEmpty(node?.Context) || node.UrnPath == null || node.UrnPath.IndexOf("/Database", StringComparison.Ordinal) < 0)
            {
                return null;
            }

            var database = new Urn(node.Context).GetAttribute("Name", "Database");
            return string.IsNullOrEmpty(database) ? null : ToConnection(node.Connection as SqlConnectionInfo, database);
        }

        private static ObjectExplorerTarget ToTarget(INodeInformation node)
        {
            if (!IsTableOrView(node?.UrnPath) || string.IsNullOrEmpty(node.Context))
            {
                return null;
            }

            var urn = new Urn(node.Context);
            var type = urn.Type; // "Table" or "View"
            var name = urn.GetAttribute("Name", type);
            var schema = urn.GetAttribute("Schema", type);
            var database = urn.GetAttribute("Name", "Database");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(database))
            {
                return null;
            }

            return new ObjectExplorerTarget
            {
                Name = new ObjectName(null, database, schema, name),
                Connection = ToConnection(node.Connection as SqlConnectionInfo, database),
            };
        }

        private static EditorConnection ToConnection(SqlConnectionInfo sci, string database)
        {
            var info = new EditorConnection { Source = "ObjectExplorer", Database = database };
            if (sci == null)
            {
                info.AuthenticationDescription = "no SQL Server connection on the node";
                return info;
            }

            info.ServerName = sci.ServerName;
            try
            {
                var builder = new SqlConnectionStringBuilder(sci.ConnectionString) { InitialCatalog = database };
                if (!builder.IntegratedSecurity && string.IsNullOrEmpty(builder.Password) && sci.SecurePassword != null && sci.SecurePassword.Length > 0)
                {
                    builder.Password = ToPlain(sci.SecurePassword);
                }

                info.OwnConnectionString = SsmsConnectionProvider.BuildOwnConnectionString(info, null, builder);
                info.SsmsHandle = ToUiConnectionInfo(builder, sci.ServerName, database);
            }
            catch (Exception ex)
            {
                info.Diagnostics.Add("OE connection: " + ex.GetType().Name);
                Logger.Warn("Reading the Object Explorer connection failed", ex);
            }

            Logger.Debug("Object Explorer connection: " + info);
            return info;
        }

        /// <summary>Lets "Open in New Query" open a connected window for inspectors started from Object Explorer.</summary>
        private static UIConnectionInfo ToUiConnectionInfo(SqlConnectionStringBuilder b, string server, string database)
        {
            if (b.Authentication != SqlAuthenticationMethod.NotSpecified && b.Authentication != SqlAuthenticationMethod.SqlPassword)
            {
                return null;
            }

            var ui = new UIConnectionInfo
            {
                ServerName = server,
                ServerType = SqlServerType,
                AuthenticationType = b.IntegratedSecurity ? 0 : 1,
                UserName = b.IntegratedSecurity ? string.Empty : b.UserID,
                Password = b.IntegratedSecurity ? string.Empty : b.Password,
            };
            ui.AdvancedOptions["DATABASE"] = database;
            if (b.Encrypt == SqlConnectionEncryptOption.Mandatory || b.Encrypt == SqlConnectionEncryptOption.Strict)
            {
                ui.AdvancedOptions["ENCRYPT_CONNECTION"] = "True";
            }
            if (b.TrustServerCertificate)
            {
                ui.AdvancedOptions["TRUST_SERVER_CERTIFICATE"] = "True";
            }
            return ui;
        }

        private static string ToPlain(System.Security.SecureString secure)
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

        /// <summary>The Object Explorer context menu item. SSMS clones it per node; Parent is the clicked node.</summary>
        private sealed class InspectTableMenuItem : ToolsMenuItemBase
        {
            public InspectTableMenuItem()
            {
                Text = "Inspect Table";
            }

            protected override void Invoke()
            {
                try
                {
                    var target = ToTarget(Parent);
                    if (target != null)
                    {
                        _onInspect?.Invoke(target);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Object Explorer 'Inspect Table' failed", ex);
                }
            }

            public override object Clone() => new InspectTableMenuItem();
        }

        /// <summary>"Database Dashboard" on database nodes.</summary>
        private sealed class DashboardMenuItem : ToolsMenuItemBase
        {
            public DashboardMenuItem()
            {
                Text = "Database Dashboard";
            }

            protected override void Invoke()
            {
                try
                {
                    var connection = ToDatabaseConnection(Parent);
                    if (connection != null)
                    {
                        _onDashboard?.Invoke(connection);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Object Explorer 'Database Dashboard' failed", ex);
                }
            }

            public override object Clone() => new DashboardMenuItem();
        }
    }
}
