using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiveSplit.VoxSplitter {
    public partial class SplitsGenerator : Form {

        private ListViewItem dragItem;

        public SplitsGenerator() {
            InitializeComponent();
        }

        private void ListView_Click(object sender, EventArgs e) {
            ListView.SelectedListViewItemCollection items = ((ListView)sender).SelectedItems;
            if(items.Count > 0) {
                items[0].BeginEdit();
            }
        }

        private void ListView_ItemDrag(object sender, ItemDragEventArgs e) {
            dragItem = (ListViewItem)e.Item;
            DoDragDrop(dragItem, DragDropEffects.Move);
        }

        private void ListView_DragEnter(object sender, DragEventArgs e) {
            e.Effect = DragDropEffects.Move;
        }

        private void ListView_DragOver(object sender, DragEventArgs e) {
            Point point = ListView.PointToClient(new Point(e.X, e.Y));
            ListViewItem dragToItem = ListView.GetItemAt(point.X, point.Y);
            if(dragToItem == dragItem) {
                return;
            }
            int dropIndex = dragToItem.Index;
            ListView.Items.Remove(dragItem);
            ListView.Items.Insert(dropIndex, dragItem);
            dragItem.Focused = true;
        }

        private void Button_Click(object sender, EventArgs e) {
            DialogResult = DialogResult.OK;
        }
    }

    public class NewListView : ListView {
        public NewListView() : base() {
            DoubleBuffered = true;
        }
    }
}