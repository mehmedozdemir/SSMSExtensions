using System;
using System.ComponentModel;
using System.Data;
using System.Windows.Controls;

namespace SsmsToolkit.Presentation.TableInspector
{
    public partial class TableInspectorView : UserControl
    {
        public TableInspectorView()
        {
            InitializeComponent();
        }

        /// <summary>Data tab: show the real SQL column name (DataTable columns are named c0, c1, ... for safe bindings).</summary>
        private void OnDataAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (!(sender is DataGrid grid) || !(grid.ItemsSource is DataView view) || !(grid.DataContext is DataTab tab))
            {
                return;
            }

            var column = view.Table.Columns[e.PropertyName];
            var caption = column?.Caption ?? e.PropertyName;
            e.Column.Header = caption;
            e.Column.SortMemberPath = caption;
            if (string.Equals(tab.SortColumn, caption, StringComparison.Ordinal))
            {
                e.Column.SortDirection = tab.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }
        }

        /// <summary>Data tab: sort on the server (TOP N of the sorted table), not just the rows on screen.</summary>
        private void OnDataSorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            if ((sender as DataGrid)?.DataContext is DataTab tab && e.Column?.SortMemberPath != null)
            {
                _ = tab.SortByAsync(e.Column.SortMemberPath);
            }
        }
    }
}
