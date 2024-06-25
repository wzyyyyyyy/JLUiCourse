using System.Windows;
using System.Windows.Controls;

namespace iCourse
{
    /// <summary>
    /// SelectBatchWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SelectBatchWindow : Window
    {
        public SelectBatchWindow(List<BatchInfo> batchList)
        {
            InitializeComponent();
            objectListBox.ItemsSource = batchList;
        }

        private void objectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            var selectedItem = listBox.SelectedItem as BatchInfo;
            batchCodeTextBlock.Text = selectedItem.batchId;
            batchNameTextBlock.Text = selectedItem.batchName;
            beginTimeTextBlock.Text = selectedItem.beginTime;
            endTimeTextBlock.Text = selectedItem.endTime;
            tacticNameTextBlock.Text = selectedItem.tacticName;
            noSelectReasonTextBlock.Text = selectedItem.noSelectReason;
            typeNameTextBlock.Text = selectedItem.typeName;
            canSelectTextBlock.Text = selectedItem.canSelect ? "是" : "否";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = objectListBox.SelectedItem as BatchInfo;
            if (selectedItem == null)
            {
                MessageBox.Show("请选择一个批次");
                return;
            }
            MainWindow.Instance.StartSelectClass(selectedItem);
            this.Close();
        }
    }
}
