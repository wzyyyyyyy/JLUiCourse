using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
            batchCodeTextBlock.Text = selectedItem.batchCode;
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

        }
    }
}
