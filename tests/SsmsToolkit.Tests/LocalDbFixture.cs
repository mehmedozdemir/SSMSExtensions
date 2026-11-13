using System;
using Microsoft.Data.SqlClient;
using SsmsToolkit.Data.Sql;
using Xunit;

namespace SsmsToolkit.Tests
{
    /// <summary>[Fact] that is skipped when LocalDB is not available (e.g. on CI).</summary>
    public sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!LocalDbFixture.IsAvailable)
            {
                Skip = "LocalDB (MSSQLLocalDB) is not available.";
            }
        }
    }

    /// <summary>Creates a throw-away database with a small schema and drops it afterwards.</summary>
    public sealed class LocalDbFixture : IDisposable
    {
        public const string Server = @"(localdb)\MSSQLLocalDB";

        private static readonly Lazy<bool> Available = new Lazy<bool>(() =>
        {
            try
            {
                using (var c = new SqlConnection(MasterConnectionString))
                {
                    c.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        });

        public LocalDbFixture()
        {
            Database = "SsmsToolkitTests_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            if (!IsAvailable)
            {
                return;
            }

            Execute(MasterConnectionString, "CREATE DATABASE " + Quote(Database) + ";");
            Execute(ConnectionString, Schema);
        }

        public static bool IsAvailable => Available.Value;

        private static string MasterConnectionString =>
            new SqlConnectionStringBuilder { DataSource = Server, IntegratedSecurity = true, InitialCatalog = "master", ConnectTimeout = 10, Encrypt = false }.ConnectionString;

        public string Database { get; }

        public string ConnectionString =>
            new SqlConnectionStringBuilder(MasterConnectionString) { InitialCatalog = Database }.ConnectionString;

        public ConnectionTarget Target => new ConnectionTarget(ConnectionString, Server, Database);

        public void Dispose()
        {
            if (!IsAvailable)
            {
                return;
            }

            SqlConnection.ClearAllPools();
            Execute(MasterConnectionString,
                "ALTER DATABASE " + Quote(Database) + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + Quote(Database) + ";");
        }

        private static string Quote(string s) => "[" + s.Replace("]", "]]") + "]";

        private static void Execute(string cs, string sql)
        {
            using (var c = new SqlConnection(cs))
            {
                c.Open();
                foreach (var batch in sql.Split(new[] { "\nGO" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.IsNullOrWhiteSpace(batch))
                    {
                        continue;
                    }
                    using (var cmd = new SqlCommand(batch, c))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private const string Schema = @"
CREATE SCHEMA sales;
GO
CREATE TABLE dbo.Customer (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
    Name nvarchar(100) NOT NULL,
    Email varchar(200) NULL CONSTRAINT UQ_Customer_Email UNIQUE,
    Balance decimal(18,2) NOT NULL CONSTRAINT DF_Customer_Balance DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Customer_CreatedAt DEFAULT (SYSUTCDATETIME()),
    NameLength AS LEN(Name),
    Photo varbinary(max) NULL
);
GO
CREATE TABLE sales.[Order] (
    Id int NOT NULL CONSTRAINT PK_Order PRIMARY KEY,
    CustomerId int NOT NULL CONSTRAINT FK_Order_Customer REFERENCES dbo.Customer(Id) ON DELETE CASCADE,
    ParentOrderId int NULL,
    Total money NOT NULL,
    Note nvarchar(max) NULL
);
GO
ALTER TABLE sales.[Order] ADD CONSTRAINT FK_Order_Parent FOREIGN KEY (ParentOrderId) REFERENCES sales.[Order](Id);
ALTER TABLE dbo.Customer ADD CONSTRAINT CK_Customer_Balance CHECK (Balance >= 0);
ALTER TABLE sales.[Order] WITH NOCHECK ADD CONSTRAINT CK_Order_Ids CHECK (Id > 0 AND CustomerId > 0);
CREATE INDEX IX_Order_Customer ON sales.[Order] (CustomerId DESC) INCLUDE (Total) WHERE Total > 0;
EXEC sys.sp_addextendedproperty N'MS_Description', N'Customers table', N'SCHEMA', N'dbo', N'TABLE', N'Customer';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Display name', N'SCHEMA', N'dbo', N'TABLE', N'Customer', N'COLUMN', N'Name';
GO
CREATE VIEW dbo.vCustomer AS SELECT Id, Name FROM dbo.Customer;
GO
CREATE SYNONYM dbo.Cust FOR dbo.Customer;
GO
CREATE PROCEDURE dbo.DoNothing AS RETURN 0;
GO
-- One-part name: caller-dependent reference (referenced_id is NULL in sys.sql_expression_dependencies).
CREATE PROCEDURE dbo.GetCustomers AS SELECT Id, Name FROM Customer;
GO
CREATE TRIGGER sales.trg_Order_Audit ON sales.[Order] AFTER INSERT, UPDATE AS SET NOCOUNT ON;
GO
DISABLE TRIGGER sales.trg_Order_Audit ON sales.[Order];
GO
INSERT dbo.Customer (Name, Email, Balance, Photo) VALUES (N'Ada', 'ada@example.com', 10.5, 0x0102), (N'Linus', NULL, 0, NULL), (N'Grace', 'g@example.com', 99.99, NULL);
INSERT sales.[Order] (Id, CustomerId, ParentOrderId, Total) VALUES (1, 1, NULL, 5), (2, 1, 1, 0), (3, 2, NULL, 7);
";
    }
}
