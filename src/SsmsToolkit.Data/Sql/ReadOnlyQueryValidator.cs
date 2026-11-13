using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsmsToolkit.Data.Sql
{
    /// <summary>
    /// Guards user-typed fragments (the Data tab's WHERE filter): the final batch must be exactly one plain
    /// SELECT, with no SELECT INTO and no pass-through/ad-hoc remote access (which could run DML elsewhere).
    /// </summary>
    public static class ReadOnlyQueryValidator
    {
        /// <returns>null when valid, otherwise a message describing the problem.</returns>
        public static string Validate(string sql)
        {
            IList<ParseError> errors;
            TSqlFragment fragment;
            using (var reader = new StringReader(sql))
            {
                fragment = new TSql170Parser(initialQuotedIdentifiers: true).Parse(reader, out errors);
            }

            if (errors.Count > 0)
            {
                var e = errors[0];
                return "Syntax error: " + e.Message;
            }

            if (!(fragment is TSqlScript script) || script.Batches.Count != 1 || script.Batches[0].Statements.Count != 1)
            {
                return "Only a single SELECT statement is allowed.";
            }

            if (!(script.Batches[0].Statements[0] is SelectStatement select) || select.Into != null)
            {
                return "Only a single SELECT statement is allowed.";
            }

            var v = new ForbiddenVisitor();
            fragment.Accept(v);
            return v.Problem;
        }

        private sealed class ForbiddenVisitor : TSqlFragmentVisitor
        {
            public string Problem { get; private set; }

            private void Forbid(string what)
            {
                if (Problem == null)
                {
                    Problem = what + " is not allowed in a filter.";
                }
            }

            public override void Visit(OpenQueryTableReference node) => Forbid("OPENQUERY");
            public override void Visit(OpenRowsetTableReference node) => Forbid("OPENROWSET");
            public override void Visit(InternalOpenRowset node) => Forbid("OPENROWSET");
            public override void Visit(BulkOpenRowset node) => Forbid("OPENROWSET(BULK)");
            public override void Visit(AdHocTableReference node) => Forbid("OPENDATASOURCE");
            public override void Visit(OpenXmlTableReference node) => Forbid("OPENXML");
            public override void Visit(SelectStatement node)
            {
                if (node.Into != null)
                {
                    Forbid("SELECT INTO");
                }
            }

            public override void Visit(SchemaObjectName node)
            {
                if (node.ServerIdentifier != null)
                {
                    Forbid("A linked server reference");
                }
            }
        }
    }
}
