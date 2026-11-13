using System;
using System.Text;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using SsmsToolkit.Data.Model;
using SsmsToolkit.Data.Sql;

namespace SsmsToolkit.Data.Metadata
{
    /// <summary>CREATE TABLE scripting through SMO (the same engine SSMS uses for "Script Table as").</summary>
    internal static class SmoScripter
    {
        public static string ScriptTable(ConnectionTarget target, ResolvedObject obj, int timeoutSeconds)
        {
            var connection = new ServerConnection
            {
                ConnectionString = target.ConnectionStringForSmo,
                StatementTimeout = timeoutSeconds,
                NonPooledConnection = false,
            };

            try
            {
                var server = new Server(connection);
                var database = server.Databases[obj.Database]
                               ?? throw new InvalidOperationException("Database not found: " + obj.Database);
                var table = database.Tables[obj.Name, obj.Schema]
                            ?? throw new InvalidOperationException("Table not found: " + obj);

                var options = new ScriptingOptions
                {
                    SchemaQualify = true,
                    DriAll = true,
                    Indexes = true,
                    ExtendedProperties = true,
                    IncludeHeaders = false,
                    Triggers = false,
                    AnsiPadding = false,
                    NoCollation = false,
                    EnforceScriptingOptions = true,
                };

                var sb = new StringBuilder();
                foreach (var line in table.Script(options))
                {
                    sb.Append(line.TrimEnd()).Append("\r\nGO\r\n");
                }
                return sb.ToString();
            }
            finally
            {
                if (connection.IsOpen)
                {
                    connection.Disconnect();
                }
            }
        }
    }
}
