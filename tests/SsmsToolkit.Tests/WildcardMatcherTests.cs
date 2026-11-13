using SsmsToolkit.Data.Text;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class WildcardMatcherTests
    {
        [Theory]
        [InlineData("SQL-PROD-01", "*prod*", true)]
        [InlineData("sqlprod", "*Prod*", true)]
        [InlineData("svasistestdb11", "*prod*", false)]
        [InlineData("db-prd-2", "*prod*; *-prd-*", true)]
        [InlineData("db-prd-2", "*prod*,*-prd-?", true)]
        [InlineData("tcp:server.database.windows.net,1433", "*.database.windows.net*", true)]
        [InlineData("anything", "", false)]
        [InlineData("anything", " ; ", false)]
        [InlineData("a.b", "a?b", true)]
        [InlineData("a+b", "a+b", true)]
        public void Matches(string server, string patterns, bool expected)
        {
            Assert.Equal(expected, WildcardMatcher.MatchesAny(server, patterns));
        }
    }
}
