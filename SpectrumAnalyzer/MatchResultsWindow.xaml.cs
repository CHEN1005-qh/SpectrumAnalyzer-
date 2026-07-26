using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SpectrumAnalyzer
{
    public partial class MatchResultsWindow : Window
    {
        // 声明选中事件，通知主窗口重绘参考线
        public event Action<MatchResult> OnResultSelected;

        public MatchResultsWindow(List<MatchResult> results)
        {
            InitializeComponent();
            DgResults.ItemsSource = results;
        }

        private void DgResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgResults.SelectedItem is MatchResult selected)
            {
                // 触发事件，主界面图表会自动更新比对标题和参考虚线
                OnResultSelected?.Invoke(selected);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}