using System;
using Microsoft.Data.SqlClient;

namespace SsmsToolkit.Data.Sql
{
    /// <summary>
    /// Where SsmsToolkit opens its own short-lived connections. The connection string may contain a password:
    /// never log, display or persist it (use <see cref="ToString"/> for diagnostics).
    /// </summary>
    public sealed class ConnectionTarget
    {
        private readonly string _connectionString;

        public ConnectionTarget(string connectionString, string serverName, string database)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException("Connection string is required.", nameof(connectionString));
            }

            var b = new SqlConnectionStringBuilder(connectionString)
            {
                ApplicationName = "SsmsToolkit",
                PersistSecurityInfo = false,
            };
            if (!string.IsNullOrEmpty(database))
            {
                b.InitialCatalog = database;
            }

            _connectionString = b.ConnectionString;
            ServerName = string.IsNullOrEmpty(serverName) ? b.DataSource : serverName;
            Database = b.InitialCatalog;
        }

        public string ServerName { get; }
        public string Database { get; }

        public ConnectionTarget ForDatabase(string database)
        {
            if (string.IsNullOrEmpty(database) || string.Equals(database, Database, StringComparison.Ordinal))
            {
                return this;
            }
            return new ConnectionTarget(_connectionString, ServerName, database);
        }

        public SqlConnection CreateConnection() => new SqlConnection(_connectionString);

        /// <summary>For SMO, which may open extra connections and therefore needs the full string.</summary>
        internal string ConnectionStringForSmo => _connectionString;

        public override string ToString() => ServerName + " / " + Database;
    }
}
