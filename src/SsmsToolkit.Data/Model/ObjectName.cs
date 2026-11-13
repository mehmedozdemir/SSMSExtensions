using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SsmsToolkit.Data.Model
{
    /// <summary>A (possibly partial) multipart SQL object name: [server].[database].[schema].[name].</summary>
    public sealed class ObjectName : IEquatable<ObjectName>
    {
        public ObjectName(string server, string database, string schema, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Name is required.", nameof(name));
            }

            Server = NullIfEmpty(server);
            Database = NullIfEmpty(database);
            Schema = NullIfEmpty(schema);
            Name = name;
        }

        public string Server { get; }
        public string Database { get; }
        public string Schema { get; }
        public string Name { get; }

        /// <summary>Local temp table (#x) or global temp table (##x).</summary>
        public bool IsTemporary => Name.StartsWith("#", StringComparison.Ordinal);

        public bool IsTableVariable => Name.StartsWith("@", StringComparison.Ordinal);

        public static ObjectName FromParts(IReadOnlyList<string> parts)
        {
            if (parts == null || parts.Count == 0 || parts.Count > 4)
            {
                throw new ArgumentException("A name has 1 to 4 parts.", nameof(parts));
            }

            string Part(int fromEnd) => parts.Count > fromEnd ? parts[parts.Count - 1 - fromEnd] : null;
            return new ObjectName(Part(3), Part(2), Part(1), Part(0));
        }

        public ObjectName WithDatabase(string database) => new ObjectName(Server, database, Schema, Name);

        /// <summary>Bracket-quotes an identifier, escaping ']' (same as QUOTENAME).</summary>
        public static string QuoteIdentifier(string identifier)
        {
            if (identifier == null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }
            return "[" + identifier.Replace("]", "]]") + "]";
        }

        /// <summary>Quoted name without server and database: [schema].[name] or [name].</summary>
        public string ToQuotedSchemaName()
        {
            return Schema == null ? QuoteIdentifier(Name) : QuoteIdentifier(Schema) + "." + QuoteIdentifier(Name);
        }

        /// <summary>Fully quoted name with every known part, e.g. [db].[dbo].[t] or [db]..[t].</summary>
        public string ToQuotedString()
        {
            var sb = new StringBuilder();
            if (Server != null)
            {
                sb.Append(QuoteIdentifier(Server)).Append('.');
            }
            if (Server != null || Database != null)
            {
                sb.Append(Database == null ? string.Empty : QuoteIdentifier(Database)).Append('.');
            }
            if (Schema != null)
            {
                sb.Append(QuoteIdentifier(Schema));
            }
            if (Server != null || Database != null || Schema != null)
            {
                sb.Append('.');
            }
            sb.Append(QuoteIdentifier(Name));
            return sb.ToString();
        }

        public override string ToString()
        {
            return string.Join(".", new[] { Server, Database, Schema, Name }.SkipWhile(p => p == null).Select(p => p ?? string.Empty));
        }

        public bool Equals(ObjectName other)
        {
            if (other is null)
            {
                return false;
            }

            return Eq(Server, other.Server) && Eq(Database, other.Database) && Eq(Schema, other.Schema) && Eq(Name, other.Name);
        }

        public override bool Equals(object obj) => Equals(obj as ObjectName);

        public override int GetHashCode()
        {
            var c = StringComparer.OrdinalIgnoreCase;
            unchecked
            {
                int h = c.GetHashCode(Name);
                h = (h * 397) ^ (Schema == null ? 0 : c.GetHashCode(Schema));
                h = (h * 397) ^ (Database == null ? 0 : c.GetHashCode(Database));
                return (h * 397) ^ (Server == null ? 0 : c.GetHashCode(Server));
            }
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
