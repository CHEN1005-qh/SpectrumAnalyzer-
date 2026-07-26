using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SpectrumAnalyzer
{
    public partial class CsvImportWindow : Window
    {
        private string _filePath;
        public double[] ParsedX { get; private set; }
        public double[] ParsedY { get; private set; }

        public CsvImportWindow(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
            LoadPreview();
        }

        private void LoadPreview()
        {
            try
            {
                // 读取文件前20行作为预览
                var lines = File.ReadLines(_filePath).Take(20);
                TxtPreview.Text = string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                TxtPreview.Text = "文件预览失败: " + ex.Message;
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtSkipLines.Text, out int skip) ||
                !int.TryParse(TxtXColumn.Text, out int xCol) ||
                !int.TryParse(TxtYColumn.Text, out int yCol))
            {
                MessageBox.Show("请输入有效的数字！", "错误");
                return;
            }

            char delimiter;
            if (((ComboBoxItem)CmbDelimiter.SelectedItem).Content.ToString() == "Tab")
                delimiter = '\t';
            else
                delimiter = ((ComboBoxItem)CmbDelimiter.SelectedItem).Content.ToString()[0];

            List<double> xList = new List<double>();
            List<double> yList = new List<double>();

            try
            {
                var lines = File.ReadLines(_filePath).Skip(skip);
                foreach (var line in lines)
                {
                    var parts = line.Split(delimiter);
                    if (parts.Length > xCol && parts.Length > yCol)
                    {
                        if (double.TryParse(parts[xCol], out double x) && double.TryParse(parts[yCol], out double y))
                        {
                            xList.Add(x);
                            yList.Add(y);
                        }
                    }
                }

                if (xList.Count > 0)
                {
                    ParsedX = xList.ToArray();
                    ParsedY = yList.ToArray();
                    this.DialogResult = true;
                }
                else
                {
                    MessageBox.Show("未能从文件中解析出有效的光谱数据，请检查参数设置。", "提示");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("文件解析失败: " + ex.Message, "错误");
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}