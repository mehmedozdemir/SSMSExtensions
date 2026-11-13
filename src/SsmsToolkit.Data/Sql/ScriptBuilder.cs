using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SsmsToolkit.Data.Model;

namespace SsmsToolkit.Data.Sql
{
    /// <summary>
    /// CREATE / ALTER scripts for single objects (column, key, index, constraint, foreign key), built from the
    /// metadata already loaded — no extra server round trip. Storage options (FILLFACTOR, compression,
    /// filegroup...) are omitted; the DDL tab (SMO) has the complete table script.
    /// </summary>
    public static class ScriptBuilder
    {
        private const string NewLine = "\r\n";

        public static string Column(ResolvedObject table, ColumnInfo c)
        {
            var sb = new StringBuilder("ALTER TABLE ").Append(Table(table)).Append(" ADD ").Append(Q(c.Name)).Append(' ');
            if (c.IsComputed)
            {
                sb.Append("AS ").Append(c.ComputedDefinition);
            }
            else
            {
                sb.Append(c.DataType);
                if (!string.IsNullOrEmpty(c.Collation))
                {
                    sb.Append(" COLLATE ").Append(c.Collation);
                }
                if (c.IsIdentity)
                {
                    sb.Append(" IDENTITY").Append(c.IdentityDefinition?.Replace(" ", string.Empty) ?? string.Empty);
                }
                sb.Append(c.IsNullable ? " NULL" : " NOT NULL");
                if (!string.IsNullOrEmpty(c.DefaultDefinition))
                {
                    sb.Append(" DEFAULT ").Append(c.DefaultDefinition);
                }
            }
            return sb.Append(';').ToString();
        }

        /// <summary>PRIMARY KEY or UNIQUE constraint.</summary>
        public static string Key(ResolvedObject table, IndexInfo key)
        {
            if (!key.IsConstraint)
            {
                throw new ArgumentException("Not a PRIMARY KEY / UNIQUE constraint.", nameof(key));
            }

            var sb = new StringBuilder("ALTER TABLE ").Append(Table(table))
                .Append(" ADD CONSTRAINT ").Append(Q(key.Name))
                .Append(key.IsPrimaryKey ? " PRIMARY KEY" : " UNIQUE");
            var kind = ClusteredKeyword(key.TypeDescription);
            if (kind != null)
            {
                sb.Append(' ').Append(kind);
            }
            sb.Append(' ').Append(KeyList(key.KeyColumnDetails)).Append(';');
            AppendDisabled(sb, table, key);
            return sb.ToString();
        }

        /// <summary>An index that does not back a constraint.</summary>
        public static string Index(ResolvedObject table, IndexInfo index)
        {
            var type = (index.TypeDescription ?? string.Empty).ToUpperInvariant();
            var sb = new StringBuilder();

            switch (type)
            {
                case "CLUSTERED":
                case "NONCLUSTERED":
                    sb.Append("CREATE ").Append(index.IsUnique ? "UNIQUE " : string.Empty).Append(type)
                      .Append(" INDEX ").Append(Q(index.Name)).Append(" ON ").Append(Table(table))
                      .Append(' ').Append(KeyList(index.KeyColumnDetails));
                    if (index.IncludedColumns.Count > 0)
                    {
                        sb.Append(NewLine).Append("    INCLUDE (").Append(string.Join(", ", index.IncludedColumns.Select(Q))).Append(')');
                    }
                    AppendFilter(sb, index);
                    sb.Append(';');
                    break;

                case "CLUSTERED COLUMNSTORE":
                    sb.Append("CREATE CLUSTERED COLUMNSTORE INDEX ").Append(Q(index.Name)).Append(" ON ").Append(Table(table)).Append(';');
                    break;

                case "NONCLUSTERED COLUMNSTORE":
                    var columns = index.KeyColumnDetails.Select(k => k.Name).Concat(index.IncludedColumns).Distinct();
                    sb.Append("CREATE NONCLUSTERED COLUMNSTORE INDEX ").Append(Q(index.Name)).Append(" ON ").Append(Table(table))
                      .Append(" (").Append(string.Join(", ", columns.Select(Q))).Append(')');
                    AppendFilter(sb, index);
                    sb.Append(';');
                    break;

                default:
                    return "-- " + index.TypeDescription + " index " + Q(index.Name) + ": see the DDL tab for the full script.";
            }

            AppendDisabled(sb, table, index);
            return sb.ToString();
        }

        public static string Constraint(ResolvedObject table, ConstraintInfo c)
        {
            var sb = new StringBuilder("ALTER TABLE ").Append(Table(table));
            if (c.Kind == ConstraintKind.Default)
            {
                return sb.Append(" ADD CONSTRAINT ").Append(Q(c.Name)).Append(" DEFAULT ").Append(c.Definition)
                         .Append(" FOR ").Append(Q(c.Column)).Append(';').ToString();
            }

            sb.Append(c.IsNotTrusted ? " WITH NOCHECK" : " WITH CHECK")
              .Append(" ADD CONSTRAINT ").Append(Q(c.Name)).Append(" CHECK ").Append(c.Definition).Append(';');
            if (c.IsDisabled)
            {
                sb.Append(NewLine).Append("ALTER TABLE ").Append(Table(table)).Append(" NOCHECK CONSTRAINT ").Append(Q(c.Name)).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>The trigger's own CREATE TRIGGER text, plus DISABLE TRIGGER when it is disabled.</summary>
        public static string Trigger(ResolvedObject table, TriggerInfo t)
        {
            if (string.IsNullOrEmpty(t.Definition))
            {
                return "-- The definition of " + Q(t.Name) + " is not available (encrypted, or no VIEW DEFINITION permission).";
            }

            // Module text often starts with blank lines (whatever preceded CREATE in the original batch).
            var sb = new StringBuilder(t.Definition.Trim());
            if (t.IsDisabled)
            {
                sb.Append(NewLine).Append("GO").Append(NewLine)
                  .Append("DISABLE TRIGGER ").Append(Q(table.Schema)).Append('.').Append(Q(t.Name)).Append(" ON ").Append(Table(table)).Append(';');
            }
            return sb.ToString();
        }

        public static string ForeignKey(ForeignKeyInfo fk)
        {
            var parent = TwoPart(fk.ParentTable);
            var sb = new StringBuilder("ALTER TABLE ").Append(parent)
                .Append(fk.IsNotTrusted ? " WITH NOCHECK" : " WITH CHECK")
                .Append(" ADD CONSTRAINT ").Append(Q(fk.Name))
                .Append(NewLine).Append("    FOREIGN KEY (").Append(string.Join(", ", fk.ParentColumns.Select(Q))).Append(')')
                .Append(NewLine).Append("    REFERENCES ").Append(TwoPart(fk.ReferencedTable))
                .Append(" (").Append(string.Join(", ", fk.ReferencedColumns.Select(Q))).Append(')');
            AppendAction(sb, "DELETE", fk.OnDelete);
            AppendAction(sb, "UPDATE", fk.OnUpdate);
            sb.Append(';');
            if (fk.IsDisabled)
            {
                sb.Append(NewLine).Append("ALTER TABLE ").Append(parent).Append(" NOCHECK CONSTRAINT ").Append(Q(fk.Name)).Append(';');
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- helpers

        private static string Q(string identifier) => ObjectName.QuoteIdentifier(identifier);

        private static string Table(ResolvedObject t) => Q(t.Schema) + "." + Q(t.Name);

        private static string TwoPart(ObjectName n) => (n.Schema == null ? string.Empty : Q(n.Schema) + ".") + Q(n.Name);

        private static string KeyList(IEnumerable<IndexKeyColumn> keys) =>
            "(" + string.Join(", ", keys.Select(k => Q(k.Name) + (k.Descending ? " DESC" : " ASC"))) + ")";

        private static string ClusteredKeyword(string typeDescription)
        {
            switch ((typeDescription ?? string.Empty).ToUpperInvariant())
            {
                case "CLUSTERED": return "CLUSTERED";
                case "NONCLUSTERED": return "NONCLUSTERED";
                default: return null;
            }
        }

        private static void AppendFilter(StringBuilder sb, IndexInfo index)
        {
            if (!string.IsNullOrEmpty(index.FilterDefinition))
            {
                sb.Append(NewLine).Append("    WHERE ").Append(index.FilterDefinition);
            }
        }

        private static void AppendDisabled(StringBuilder sb, ResolvedObject table, IndexInfo index)
        {
            if (index.IsDisabled)
            {
                sb.Append(NewLine).Append("ALTER INDEX ").Append(Q(index.Name)).Append(" ON ").Append(Table(table)).Append(" DISABLE;");
            }
        }

        private static void AppendAction(StringBuilder sb, string verb, string action)
        {
            if (!string.IsNullOrEmpty(action) && !string.Equals(action, "NO_ACTION", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(NewLine).Append("    ON ").Append(verb).Append(' ').Append(action.Replace('_', ' '));
            }
        }
    }
}
