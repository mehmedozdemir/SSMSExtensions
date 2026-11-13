using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SsmsToolkit.Data.Grid;
using SsmsToolkit.Presentation.ResultGrid;
using Xunit;

namespace SsmsToolkit.Tests
{
    public class InsertScriptViewModelTests
    {
        private static InsertScriptViewModel Create(long selectionRows, long allRows, out Rows all, params string[] candidates)
        {
            all = new Rows(allRows);
            return new InsertScriptViewModel(new Rows(selectionRows), all, candidates, new NullHost());
        }

        [Fact]
        public void Defaults_to_first_candidate_and_previews_selection()
        {
            var vm = Create(3, 500, out _, "sales.Orders", "dbo.Other");
            Assert.Equal("sales.Orders", vm.TableName);
            Assert.Equal(3, vm.Preview.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.StartsWith("INSERT INTO [sales].[Orders] ([Name]) VALUES (N'row 0');", vm.Preview);
            Assert.Null(vm.PreviewNote);
        }

        [Fact]
        public void Preview_is_limited_and_reads_only_preview_rows()
        {
            var vm = Create(3, 5000, out var all);
            vm.UseAllRows = true;
            Assert.Equal(InsertScriptViewModel.PreviewRows, vm.Preview.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.NotNull(vm.PreviewNote);
            Assert.Equal(InsertScriptViewModel.PreviewRows, all.ReadCount);
            Assert.Contains("[TableName]", vm.Preview);
        }

        [Fact]
        public void Identity_options_follow_the_columns()
        {
            var vm = Create(1, 1, out _, "T");
            Assert.True(vm.HasIdentityColumn);
            Assert.False(vm.CanUseIdentityInsert);
            vm.ExcludeGeneratedColumns = false;
            vm.IdentityInsert = true;
            Assert.True(vm.CanUseIdentityInsert);
            Assert.StartsWith("SET IDENTITY_INSERT [T] ON;", vm.Preview);
        }

        [Fact]
        public async Task Generates_all_rows_with_progress()
        {
            var vm = Create(3, 1000, out _, "T");
            vm.UseAllRows = true;
            vm.RowsPerStatement = 100;
            int yields = 0;
            var sql = await vm.GenerateAsync(CancellationToken.None, () => { yields++; return Task.CompletedTask; });

            Assert.Equal(10, sql.Split(new[] { "INSERT INTO" }, StringSplitOptions.None).Length - 1);
            Assert.Contains("(N'row 999');", sql);
            Assert.Equal(4, yields);
            Assert.Equal(100, vm.Progress);
        }

        [Fact]
        public async Task Generation_can_be_cancelled()
        {
            var vm = Create(3, 10_000, out _, "T");
            vm.UseAllRows = true;
            using (var cts = new CancellationTokenSource())
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.GenerateAsync(cts.Token, () =>
                {
                    cts.Cancel();
                    return Task.CompletedTask;
                }));
            }
        }

        private sealed class Rows : IGridRowSource
        {
            public Rows(long count)
            {
                RowCount = count;
            }

            public int ReadCount { get; private set; }

            public IReadOnlyList<ResultColumn> Columns { get; } = new[]
            {
                new ResultColumn("Id", "int") { IsIdentity = true },
                new ResultColumn("Name", "nvarchar"),
            };

            public long RowCount { get; }

            public object[] ReadRow(long index)
            {
                ReadCount++;
                return new object[] { (int)index, "row " + index };
            }
        }

        private sealed class NullHost : IInsertScriptHost
        {
            public void CopyToClipboard(string text)
            {
            }

            public Task OpenInNewQueryAsync(string text) => Task.CompletedTask;

            public string AskSaveFileName(string suggestedName) => null;

            public void Close()
            {
            }
        }
    }
}
