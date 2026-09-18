using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using SpectrumAnalyzer.Core;
using SpectrumAnalyzer.ViewModels;

namespace SpectrumAnalyzer
{
    /// <summary>
    /// 标准参考库独立窗口：以单独窗口展示与维护标准参考光谱库，
    /// 支持列表展示、分类/关键字筛选、谱图预览、导入/导出/删除/编辑及分类管理。
    /// </summary>
    public partial class ReferenceLibraryWindow : Window
    {
        private readonly MainViewModel _vm;

        // 当前展示（经分类+关键字筛选后）的参考谱集合
        private ObservableCollection<ReferenceSpectrumModel> _filtered;

        public ReferenceLibraryWindow()
            : this(null)
        {
        }

        public ReferenceLibraryWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            if (_vm == null)
                throw new InvalidOperationException("标准参考库窗口需要传入 MainViewModel 实例。");
            DataContext = _vm;

            InitPreviewPlot();
            RefreshData();
        }

        #region 数据加载与筛选

        /// <summary>
        /// 重新从 ViewModel 拉取最新数据并重建筛选条件
        /// </summary>
        private void RefreshData()
        {
            try
            {
                // 确保分类显示已填充（其它窗口可能改过分类）
                _vm.ReloadCategories();
                _vm.ReloadDataList();

                // 重建分类下拉（仅保留已有数据中出现过的分类路径）
                var selected = CmbCategory.SelectedItem as string;
                var currentList = _vm.ReferenceSpectraList ?? new ObservableCollection<ReferenceSpectrumModel>();

                var categories = new List<string> { "全部" };
                categories.AddRange(currentList
                    .Select(x => string.IsNullOrEmpty(x.CategoryDisplay) ? "未分类" : x.CategoryDisplay)
                    .Distinct()
                    .OrderBy(x => x, StringComparer.CurrentCulture));

                CmbCategory.ItemsSource = categories;
                if (selected != null && categories.Contains(selected))
                    CmbCategory.SelectedItem = selected;
                else
                    CmbCategory.SelectedItem = "全部";

                TxtStatus.Text = $"标准库共 {currentList.Count} 条记录";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载标准库失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 按分类 + 搜索关键字生成展示列表
        /// </summary>
        private void ApplyFilter()
        {
            if (_vm == null) return;

            IEnumerable<ReferenceSpectrumModel> source = _vm.ReferenceSpectraList ??
                new ObservableCollection<ReferenceSpectrumModel>();

            string catSel = CmbCategory.SelectedItem as string;
            if (!string.IsNullOrEmpty(catSel) && catSel != "全部")
            {
                source = source.Where(x =>
                {
                    var disp = string.IsNullOrEmpty(x.CategoryDisplay) ? "未分类" : x.CategoryDisplay;
                    return disp == catSel;
                });
            }

            string keyword = TxtSearch.Text?.Trim();
            if (!string.IsNullOrEmpty(keyword))
            {
                source = source.Where(x =>
                    (x.SubstanceName != null && x.SubstanceName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.ChemicalFormula != null && x.ChemicalFormula.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.CasNumber != null && x.CasNumber.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            _filtered = new ObservableCollection<ReferenceSpectrumModel>(source);
            DgRef.ItemsSource = _filtered;
            TxtCount.Text = $"显示 {_filtered.Count} / {(_vm.ReferenceSpectraList?.Count ?? 0)} 条";
        }

        private void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        #endregion

        #region 谱图预览

        private void InitPreviewPlot()
        {
            PreviewPlot.Plot.XLabel("拉曼位移 (cm⁻¹)");
            PreviewPlot.Plot.YLabel("强度");
            PreviewPlot.Plot.Style(ScottPlot.Style.Control);
        }

        private void PreviewReferenceSpectrum(ReferenceSpectrumModel item)
        {
            try
            {
                PreviewPlot.Plot.Clear();

                if (item == null)
                {
                    TxtPreviewTitle.Text = "选择参考谱以预览";
                    TxtPreviewInfo.Text = "";
                    PreviewPlot.Refresh();
                    return;
                }

                // 列表项可能为轻量数据，按需拉取含全谱的完整记录
                var full = _vm.ReferenceRepository.GetById(item.Id) ?? item;

                double[] x = null, y = null;
                try { if (!string.IsNullOrEmpty(full.X_cm)) x = JsonConvert.DeserializeObject<double[]>(full.X_cm); } catch { x = null; }
                try { if (!string.IsNullOrEmpty(full.Y_cm)) y = JsonConvert.DeserializeObject<double[]>(full.Y_cm); } catch { y = null; }

                if (x == null || y == null || x.Length == 0 || y.Length == 0)
                {
                    TxtPreviewTitle.Text = full.SubstanceName ?? $"参考谱 Id:{full.Id}";
                    TxtPreviewInfo.Text = "该记录没有可预览的全谱数据。";
                    PreviewPlot.Refresh();
                    return;
                }

                int n = Math.Min(x.Length, y.Length);
                if (x.Length != n) { var t = new double[n]; Array.Copy(x, t, n); x = t; }
                if (y.Length != n) { var t = new double[n]; Array.Copy(y, t, n); y = t; }

                var pl = PreviewPlot.Plot.AddScatter(x, y, label: full.SubstanceName, color: System.Drawing.Color.DarkGreen);
                pl.LineWidth = 2;

                PreviewPlot.Plot.AxisAuto();
                PreviewPlot.Plot.Title(full.SubstanceName ?? $"参考谱 Id:{full.Id}");
                var legend = PreviewPlot.Plot.Legend(true, ScottPlot.Alignment.UpperRight);
                legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);

                TxtPreviewTitle.Text = (full.SubstanceName ?? $"参考谱 Id:{full.Id}") +
                    (string.IsNullOrEmpty(full.CategoryDisplay) ? "" : $"  [ {full.CategoryDisplay} ]");
                TxtPreviewInfo.Text = $"采样点: {n}";

                PreviewPlot.Refresh();
            }
            catch (Exception ex)
            {
                TxtPreviewInfo.Text = "预览绘制失败: " + ex.Message;
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        #endregion

        #region 工具按钮

        /// <summary>
        /// 刷新按钮：从数据库重新加载
        /// </summary>
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
        }

        /// <summary>
        /// 导出整个标准参考库为 JSON
        /// </summary>
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var list = _vm.ReferenceSpectraList != null
                    ? new List<ReferenceSpectrumModel>(_vm.ReferenceSpectraList)
                    : _vm.ReferenceRepository.GetAll();

                if (list == null || list.Count == 0)
                {
                    MessageBox.Show("标准参考库为空，无法导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    DefaultExt = ".json",
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    FileName = "reference_library.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var json = JsonConvert.SerializeObject(list, Formatting.Indented);
                    File.WriteAllText(saveDialog.FileName, json, System.Text.Encoding.UTF8);
                    MessageBox.Show($"标准参考库已导出 {list.Count} 条为 JSON。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 从 JSON 文件导入标准参考库
        /// </summary>
        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Multiselect = false
                };

                if (dlg.ShowDialog() != true) return;

                string txt = File.ReadAllText(dlg.FileName);
                List<ReferenceSpectrumModel> list = null;
                try
                {
                    list = JsonConvert.DeserializeObject<List<ReferenceSpectrumModel>>(txt);
                }
                catch
                {
                    try
                    {
                        var single = JsonConvert.DeserializeObject<ReferenceSpectrumModel>(txt);
                        if (single != null)
                            list = new List<ReferenceSpectrumModel> { single };
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                }

                if (list == null || list.Count == 0)
                {
                    MessageBox.Show("未能从 JSON 文件中解析出标准库数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var item in list)
                {
                    if (string.IsNullOrEmpty(item.CreatedAt)) item.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (item.PeaksJson == null) item.PeaksJson = "[]";
                    if (item.X_cm == null) item.X_cm = "[]";
                    if (item.Y_cm == null) item.Y_cm = "[]";
                }

                _vm.ReferenceRepository.ImportReferenceSpectra(list);
                RefreshData();
                MessageBox.Show($"成功导入 {list.Count} 条参考谱到标准库。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除勾选的参考谱
        /// </summary>
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var toDelete = _filtered?.Where(x => x.IsChecked).ToList() ??
                    new List<ReferenceSpectrumModel>();
                if (toDelete.Count == 0)
                {
                    MessageBox.Show("请先在列表中勾选要删除的参考光谱记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (MessageBox.Show($"确认删除选中的 {toDelete.Count} 条参考光谱？", "删除确认",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                foreach (var item in toDelete)
                {
                    try { _vm.ReferenceRepository.Delete(item.Id); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"删除参考光谱 {item.Id} 失败: {ex.Message}"); }
                }

                RefreshData();
                MessageBox.Show($"已删除 {toDelete.Count} 条参考光谱。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 打开分类管理窗口
        /// </summary>
        private void BtnCategory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mgr = new CategoryManagerWindow(_vm.CategoryRepository) { Owner = this };
                mgr.ShowDialog();
                RefreshData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开分类管理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 列表交互

        private void DgRef_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgRef.SelectedItem is ReferenceSpectrumModel item)
                PreviewReferenceSpectrum(item);
        }

        private void DgRef_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(DgRef.SelectedItem is ReferenceSpectrumModel refModel)) return;
            try
            {
                var fullRef = _vm.ReferenceRepository.GetById(refModel.Id) ?? refModel;
                var win = new SaveWindow(fullRef) { Owner = this };
                win.CategorySource = _vm.CategoryRepository;
                bool? ok = win.ShowDialog();
                if (ok == true && win.ResultModel != null)
                {
                    _vm.ReferenceRepository.Update(win.ResultModel);
                    RefreshData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                MessageBox.Show($"编辑参考谱失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}