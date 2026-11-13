using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SsmsToolkit.Data.Model;

namespace SsmsToolkit.Data.Parsing
{
    /// <summary>Result of resolving the identifier under the caret to candidate object names.</summary>
    public sealed class NameResolution
    {
        internal NameResolution(string clickedText, IReadOnlyList<ObjectName> candidates, bool usedParser)
        {
            ClickedText = clickedText;
            Candidates = candidates;
            UsedParser = usedParser;
        }

        public static NameResolution None { get; } = new NameResolution(null, Array.Empty<ObjectName>(), false);

        /// <summary>The multipart identifier text under the caret, e.g. "p.Id".</summary>
        public string ClickedText { get; }

        /// <summary>Names to try in order; the first one that exists in the database wins.</summary>
        public IReadOnlyList<ObjectName> Candidates { get; }

        /// <summary>True when the full ScriptDom parse succeeded; false when the token fallback was used.</summary>
        public bool UsedParser { get; }

        public bool IsEmpty => Candidates.Count == 0;
    }

    /// <summary>
    /// Finds which table/view the user means at a caret offset: plain names, multipart names, and aliases
    /// (e.g. clicking <c>p</c> in <c>FROM PersonelCardDetails p ... p.Id</c>). Uses ScriptDom on the batch
    /// containing the caret; when the batch does not parse (half-typed query), falls back to a token scan.
    /// </summary>
    public static class SqlNameResolver
    {
        public static NameResolution Resolve(string script, int offset)
        {
            if (string.IsNullOrEmpty(script) || offset < 0 || offset >= script.Length)
            {
                return NameResolution.None;
            }

            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            IList<TSqlParserToken> allTokens;
            using (var reader = new StringReader(script))
            {
                allTokens = parser.GetTokenStream(reader, out _);
            }
            if (allTokens == null || allTokens.Count == 0)
            {
                return NameResolution.None;
            }

            // Restrict everything to the GO-delimited batch that contains the caret.
            var (batchStart, batchEnd) = FindBatch(allTokens, offset);
            var tokens = allTokens.Skip(batchStart).Take(batchEnd - batchStart).ToList();

            int clicked = tokens.FindIndex(t => t.Offset <= offset && offset < t.Offset + (t.Text?.Length ?? 0));
            if (clicked < 0 || !IsNameToken(tokens[clicked]))
            {
                return NameResolution.None;
            }

            var multipart = ReadMultipartAround(tokens, clicked);
            var clickedText = string.Join(".", multipart.Parts);

            if (tokens[clicked].TokenType == TSqlTokenType.Variable)
            {
                // Table variables are out of scope for v1; return the name so the caller can say so.
                return new NameResolution(clickedText, new[] { new ObjectName(null, null, null, tokens[clicked].Text) }, false);
            }

            var batchText = BatchText(script, tokens);
            var batchOffset = tokens[0].Offset;

            var fromParser = TryResolveWithParser(batchText, offset - batchOffset);
            if (fromParser != null)
            {
                return new NameResolution(clickedText, fromParser, true);
            }

            return new NameResolution(clickedText, ResolveWithTokens(tokens, multipart), false);
        }

        /// <summary>Parses a standalone multipart name such as "[db].[dbo].[T]" or "dbo.T". Returns null if invalid.</summary>
        public static ObjectName ParseMultipartName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            IList<TSqlParserToken> tokens;
            using (var reader = new StringReader(text.Trim()))
            {
                tokens = new TSql170Parser(initialQuotedIdentifiers: true).GetTokenStream(reader, out var errors);
                if (errors.Count > 0)
                {
                    return null;
                }
            }

            var significant = tokens.Where(t => t.TokenType != TSqlTokenType.EndOfFile && !IsTrivia(t)).ToList();
            if (significant.Count == 0 || !IsNameToken(significant[0]))
            {
                return null;
            }

            var mp = ReadMultipartAround(significant, 0);
            int consumed = mp.Parts.Count + mp.Parts.Count - 1;
            if (consumed != significant.Count || mp.Parts.Count > 4 || mp.Parts[mp.Parts.Count - 1].Length == 0)
            {
                return null;
            }
            return ObjectName.FromParts(mp.Parts);
        }

        /// <summary>
        /// Tables and views read by the SELECT statements of a script (FROM / JOIN), in order of appearance, without
        /// CTE names, temp tables or table variables. Empty when the script does not parse.
        /// Used to suggest a target table for "INSERT statements" generated from a result grid.
        /// </summary>
        public static IReadOnlyList<ObjectName> FindSelectSources(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return Array.Empty<ObjectName>();
            }

            TSqlFragment fragment;
            IList<ParseError> errors;
            using (var reader = new StringReader(script))
            {
                fragment = new TSql170Parser(initialQuotedIdentifiers: true).Parse(reader, out errors);
            }
            if (fragment == null || errors.Count > 0)
            {
                return Array.Empty<ObjectName>();
            }

            var selects = new SelectSourceVisitor();
            fragment.Accept(selects);
            var result = new List<ObjectName>();
            foreach (var table in selects.Tables.OrderBy(t => t.StartOffset))
            {
                var name = ToObjectName(table.SchemaObject);
                bool single = name.Schema == null && name.Database == null && name.Server == null;
                if (!name.IsTemporary && !(single && selects.CteNames.Contains(name.Name)))
                {
                    Add(result, name);
                }
            }
            return result;
        }

        private sealed class SelectSourceVisitor : TSqlFragmentVisitor
        {
            public List<NamedTableReference> Tables { get; } = new List<NamedTableReference>();

            public HashSet<string> CteNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public override void Visit(SelectStatement node)
            {
                var inner = new CollectingVisitor();
                node.Accept(inner);
                Tables.AddRange(inner.Tables);
                CteNames.UnionWith(inner.CteNames);
            }
        }

        // ---------------------------------------------------------------- parser path

        /// <returns>Candidates, or null if the batch does not parse.</returns>
        private static IReadOnlyList<ObjectName> TryResolveWithParser(string batchText, int offset)
        {
            TSqlFragment fragment;
            IList<ParseError> errors;
            using (var reader = new StringReader(batchText))
            {
                fragment = new TSql170Parser(initialQuotedIdentifiers: true).Parse(reader, out errors);
            }
            if (fragment == null || errors.Count > 0)
            {
                return null;
            }

            var v = new CollectingVisitor();
            fragment.Accept(v);

            var result = new List<ObjectName>();

            // 1) A table alias definition: FROM Cards c  -> click on "c".
            foreach (var t in v.Tables)
            {
                if (t.Alias != null && Covers(t.Alias, offset))
                {
                    Add(result, ToObjectName(t.SchemaObject));
                    return result;
                }
            }

            // 2) A schema object name anywhere (FROM, JOIN, UPDATE, INSERT INTO, EXEC, DROP ...).
            var son = v.SchemaObjectNames.Where(s => Covers(s, offset)).OrderBy(s => s.FragmentLength).FirstOrDefault();
            if (son != null)
            {
                bool singlePart = son.SchemaIdentifier == null && son.DatabaseIdentifier == null && son.ServerIdentifier == null;
                if (singlePart)
                {
                    var name = son.BaseIdentifier.Value;
                    // UPDATE p SET ... FROM Cards p : "p" is an alias, not a table.
                    var aliased = FindAliasTarget(v, name, offset);
                    if (aliased != null)
                    {
                        Add(result, aliased);
                    }
                    if (v.CteNames.Contains(name) || v.DerivedAliases.Contains(name))
                    {
                        return result;
                    }
                }

                Add(result, ToObjectName(son));
                return result;
            }

            // 3) A qualified column reference: p.Id, dbo.Cards.Id -> the table behind the qualifier.
            var col = v.Columns
                .Where(c => c.MultiPartIdentifier != null && Covers(c, offset))
                .OrderBy(c => c.FragmentLength)
                .FirstOrDefault();
            if (col != null && col.MultiPartIdentifier.Identifiers.Count >= 2)
            {
                var ids = col.MultiPartIdentifier.Identifiers;
                var qualifier = ids.Take(ids.Count - 1).Select(i => i.Value).ToList();
                if (qualifier.Count == 1)
                {
                    var aliased = FindAliasTarget(v, qualifier[0], offset);
                    if (aliased != null)
                    {
                        Add(result, aliased);
                        return result;
                    }
                    if (v.CteNames.Contains(qualifier[0]) || v.DerivedAliases.Contains(qualifier[0]))
                    {
                        return result;
                    }
                }
                if (qualifier.Count <= 4)
                {
                    Add(result, ObjectName.FromParts(qualifier));
                }
            }

            return result;
        }

        /// <summary>
        /// Finds the table whose alias (or exposed name, when it has no alias) is <paramref name="alias"/>,
        /// preferring the innermost query scope that contains the caret.
        /// </summary>
        private static ObjectName FindAliasTarget(CollectingVisitor v, string alias, int offset)
        {
            var matches = v.Tables
                .Where(t => t.Alias != null
                    ? Eq(t.Alias.Value, alias)
                    : t.SchemaObject?.BaseIdentifier != null && Eq(t.SchemaObject.BaseIdentifier.Value, alias) && t.SchemaObject.SchemaIdentifier != null)
                .ToList();
            if (matches.Count == 0)
            {
                return null;
            }

            NamedTableReference best = null;
            int bestScope = int.MaxValue;
            foreach (var m in matches)
            {
                var scope = v.Scopes
                    .Where(s => Covers(s, m.StartOffset) && Covers(s, offset))
                    .Select(s => s.FragmentLength)
                    .DefaultIfEmpty(int.MaxValue - 1)
                    .Min();
                if (scope < bestScope)
                {
                    bestScope = scope;
                    best = m;
                }
            }

            return ToObjectName((best ?? matches[0]).SchemaObject);
        }

        private sealed class CollectingVisitor : TSqlFragmentVisitor
        {
            public List<NamedTableReference> Tables { get; } = new List<NamedTableReference>();
            public List<SchemaObjectName> SchemaObjectNames { get; } = new List<SchemaObjectName>();
            public List<ColumnReferenceExpression> Columns { get; } = new List<ColumnReferenceExpression>();
            public List<TSqlFragment> Scopes { get; } = new List<TSqlFragment>();
            public HashSet<string> CteNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> DerivedAliases { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public override void Visit(NamedTableReference node) => Tables.Add(node);
            public override void Visit(SchemaObjectName node) => SchemaObjectNames.Add(node);
            public override void Visit(ColumnReferenceExpression node) => Columns.Add(node);
            public override void Visit(QuerySpecification node) => Scopes.Add(node);
            public override void Visit(UpdateSpecification node) => Scopes.Add(node);
            public override void Visit(DeleteSpecification node) => Scopes.Add(node);
            public override void Visit(TSqlStatement node) => Scopes.Add(node);

            public override void Visit(CommonTableExpression node)
            {
                if (node.ExpressionName != null)
                {
                    CteNames.Add(node.ExpressionName.Value);
                }
            }

            public override void Visit(TableReferenceWithAlias node)
            {
                if (!(node is NamedTableReference) && node.Alias != null)
                {
                    DerivedAliases.Add(node.Alias.Value);
                }
            }
        }

        // ---------------------------------------------------------------- token fallback

        private static IReadOnlyList<ObjectName> ResolveWithTokens(List<TSqlParserToken> tokens, Multipart multipart)
        {
            var result = new List<ObjectName>();
            var parts = multipart.Parts;

            // The single clicked word, or the qualifier of alias.column, may be an alias: "p" in p.Id.
            if (parts.Count <= 2)
            {
                var aliased = FindAliasWithTokens(tokens, parts[0], multipart.StartToken);
                if (aliased != null)
                {
                    Add(result, aliased);
                }
            }

            if (parts.Count <= 4 && parts[parts.Count - 1].Length > 0)
            {
                Add(result, ObjectName.FromParts(parts));
            }

            // schema.table.column -> schema.table (also offered for two parts; a wrong guess is simply not found).
            if (parts.Count >= 2 && parts.Count <= 5)
            {
                var prefix = parts.Take(parts.Count - 1).ToList();
                if (prefix[prefix.Count - 1].Length > 0)
                {
                    Add(result, ObjectName.FromParts(prefix));
                }
            }

            return result;
        }

        /// <summary>Looks for "FROM|JOIN|UPDATE|INTO|APPLY|, name [AS] alias" in the batch, nearest to the caret.</summary>
        private static ObjectName FindAliasWithTokens(List<TSqlParserToken> tokens, string alias, int caretToken)
        {
            var significant = tokens.Select((t, i) => (t, i)).Where(x => !IsTrivia(x.t)).ToList();
            ObjectName best = null;
            int bestDistance = int.MaxValue;

            for (int k = 0; k < significant.Count; k++)
            {
                if (!IsTableIntroducer(significant[k].t))
                {
                    continue;
                }

                int p = k + 1;
                var parts = new List<string>();
                while (p < significant.Count && IsNameToken(significant[p].t) && significant[p].t.TokenType != TSqlTokenType.Variable)
                {
                    parts.Add(Unquote(significant[p].t.Text));
                    if (p + 1 < significant.Count && significant[p + 1].t.TokenType == TSqlTokenType.Dot)
                    {
                        p += 2;
                        // db..table
                        while (p < significant.Count && significant[p].t.TokenType == TSqlTokenType.Dot)
                        {
                            parts.Add(string.Empty);
                            p++;
                        }
                        continue;
                    }
                    p++;
                    break;
                }
                if (parts.Count == 0 || parts.Count > 4)
                {
                    continue;
                }

                if (p < significant.Count && significant[p].t.TokenType == TSqlTokenType.As)
                {
                    p++;
                }
                if (p < significant.Count && IsNameToken(significant[p].t) && Eq(Unquote(significant[p].t.Text), alias))
                {
                    int distance = Math.Abs(significant[p].i - caretToken);
                    if (significant[p].i > caretToken)
                    {
                        distance += 1; // prefer definitions before the caret on ties
                    }
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = ObjectName.FromParts(parts);
                    }
                }
            }

            return best;
        }

        private static bool IsTableIntroducer(TSqlParserToken t)
        {
            switch (t.TokenType)
            {
                case TSqlTokenType.From:
                case TSqlTokenType.Join:
                case TSqlTokenType.Update:
                case TSqlTokenType.Into:
                case TSqlTokenType.Comma:
                    return true;
                case TSqlTokenType.Identifier:
                    return Eq(t.Text, "APPLY");
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------- token helpers

        private sealed class Multipart
        {
            public List<string> Parts { get; } = new List<string>();
            public int ClickedIndex { get; set; }
            public int StartToken { get; set; }
        }

        private static Multipart ReadMultipartAround(List<TSqlParserToken> tokens, int clicked)
        {
            // Walk left over "name ." pairs (and empty parts in db..table).
            int start = clicked;
            while (true)
            {
                int dot = start - 1;
                if (dot < 0 || tokens[dot].TokenType != TSqlTokenType.Dot)
                {
                    break;
                }
                int prev = dot - 1;
                if (prev >= 0 && tokens[prev].TokenType == TSqlTokenType.Dot)
                {
                    start = prev; // empty part; continue from the other dot
                    continue;
                }
                if (prev >= 0 && IsNameToken(tokens[prev]))
                {
                    start = prev;
                    continue;
                }
                break;
            }

            var mp = new Multipart { StartToken = start };
            int i = start;
            bool expectName = true;
            for (; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (expectName)
                {
                    if (IsNameToken(t))
                    {
                        if (i == clicked)
                        {
                            mp.ClickedIndex = mp.Parts.Count;
                        }
                        mp.Parts.Add(Unquote(t.Text));
                        expectName = false;
                    }
                    else if (t.TokenType == TSqlTokenType.Dot && mp.Parts.Count > 0)
                    {
                        mp.Parts.Add(string.Empty);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    if (t.TokenType != TSqlTokenType.Dot)
                    {
                        break;
                    }
                    expectName = true;
                }
            }

            return mp;
        }

        private static (int Start, int End) FindBatch(IList<TSqlParserToken> tokens, int offset)
        {
            int start = 0;
            int end = tokens.Count;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].TokenType != TSqlTokenType.Go)
                {
                    continue;
                }
                if (tokens[i].Offset < offset)
                {
                    start = i + 1;
                }
                else
                {
                    end = i;
                    break;
                }
            }
            return (start, Math.Max(start, end));
        }

        private static string BatchText(string script, List<TSqlParserToken> tokens)
        {
            var first = tokens[0];
            var last = tokens[tokens.Count - 1];
            int start = first.Offset;
            int end = Math.Min(script.Length, last.Offset + (last.Text?.Length ?? 0));
            return script.Substring(start, end - start);
        }

        private static bool IsNameToken(TSqlParserToken t)
        {
            return t.TokenType == TSqlTokenType.Identifier
                   || t.TokenType == TSqlTokenType.QuotedIdentifier
                   || t.TokenType == TSqlTokenType.Variable;
        }

        private static bool IsTrivia(TSqlParserToken t)
        {
            return t.TokenType == TSqlTokenType.WhiteSpace
                   || t.TokenType == TSqlTokenType.SingleLineComment
                   || t.TokenType == TSqlTokenType.MultilineComment;
        }

        internal static string Unquote(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2)
            {
                return text;
            }
            if (text[0] == '[' && text[text.Length - 1] == ']')
            {
                return text.Substring(1, text.Length - 2).Replace("]]", "]");
            }
            if (text[0] == '"' && text[text.Length - 1] == '"')
            {
                return text.Substring(1, text.Length - 2).Replace("\"\"", "\"");
            }
            return text;
        }

        // ---------------------------------------------------------------- misc

        private static ObjectName ToObjectName(SchemaObjectName son)
        {
            return new ObjectName(son.ServerIdentifier?.Value, son.DatabaseIdentifier?.Value, son.SchemaIdentifier?.Value, son.BaseIdentifier.Value);
        }

        private static void Add(List<ObjectName> list, ObjectName name)
        {
            if (name != null && !list.Contains(name))
            {
                list.Add(name);
            }
        }

        private static bool Covers(TSqlFragment f, int offset) =>
            f != null && f.StartOffset >= 0 && f.StartOffset <= offset && offset < f.StartOffset + f.FragmentLength;

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
