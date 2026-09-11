using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ScottPlot;
using MathNet.Numerics;
using MathNet.Numerics.Statistics;
using SpectrumAnalyzer.ViewModels;
using SpectrumAnalyzer.Services;
using SpectrumAnalyzer.Core;
using SpectrumAnalyzer.Data;
using Newtonsoft.Json;

namespace SpectrumAnalyzer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// 职责：UI呈现、绘图、鼠标交互、命令转发
    /// 业务逻辑已移到 MainViewModel 和 SpectrumProcessingService
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        // ViewModel 引用
        private MainViewModel _viewModel;

        // 绘图相关变量（保留在Code-Behind）
        private ScottPlot.Plottable.Crosshair FreeCrosshair;
        private ScottPlot.Plottable.Crosshair SnapCrosshair;

        // 多光谱叠加显示的颜色调色板
        private readonly System.Drawing.Color[] OverlayColors = new[]
        {
            System.Drawing.Color.Blue,
            System.Drawing.Color.Red,
            System.Drawing.Color.Green,
            System.Drawing.Color.DarkOrange,
            System.Drawing.Color.Purple,
            System.Drawing.Color.DeepSkyBlue,
            System.Drawing.Color.Magenta,
            System.Drawing.Color.ForestGreen,
            System.Drawing.Color.Crimson,
            System.Drawing.Color.DarkSlateGray
        };

        // 记录当前叠加的参考峰 plottable 以便清理
        private readonly System.Collections.Generic.List<ScottPlot.Plottable.IPlottable> _refPlottables = new System.Collections.Generic.List<ScottPlot.Plottable.IPlottable>();
        // 记录当前多谱叠加用的 plottable 以便清理
        private readonly System.Collections.Generic.List<ScottPlot.Plottable.IPlottable> _overlayPlottables = new System.Collections.Generic.List<ScottPlot.Plottable.IPlottable>();

        public MainWindow()
        {
            InitializeComponent();

            // 创建并初始化 ViewModel
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            // UI 初始化
            InitPlotStyle();

            // 数据加载（由ViewModel负责，但需要在此触发）
            RefreshMainList();

            // 订阅 ViewModel 事件（用于同步绘图）
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        /// <summary>
        /// 全选头部复选框 - 选中
        /// </summary>
        private void HeaderCheckBox_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (_viewModel == null) return;

                if (RbExperimental.IsChecked == true)
                {
                    if (_viewModel.RamanSpectraList != null)
                    {
                        foreach (var it in _viewModel.RamanSpectraList) it.IsChecked = true;
                    }
                }
                else
                {
                    if (_viewModel.ReferenceSpectraList != null)
                    {
                        foreach (var it in _viewModel.ReferenceSpectraList) it.IsChecked = true;
                    }
                }

                DgMain.Items.Refresh();
            }
            catch { }
        }

        /// <summary>
        /// 全选头部复选框 - 取消选中
        /// </summary>
        private void HeaderCheckBox_Unchecked(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (_viewModel == null) return;

                if (RbExperimental.IsChecked == true)
                {
                    if (_viewModel.RamanSpectraList != null)
                    {
                        foreach (var it in _viewModel.RamanSpectraList) it.IsChecked = false;
                    }
                }
                else
                {
                    if (_viewModel.ReferenceSpectraList != null)
                    {
                        foreach (var it in _viewModel.ReferenceSpectraList) it.IsChecked = false;
                    }
                }

                DgMain.Items.Refresh();
            }
            catch { }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = "CSV 或 文本文件 (*.csv;*.txt)|*.csv;*.txt|所有文件 (*.*)|*.*",
                Multiselect = true
            };

            bool? ok = dlg.ShowDialog(this);
            if (ok == true)
            {
                int total = dlg.FileNames.Length;
                int successCount = 0;
                var db = new DatabaseService();

                foreach (var file in dlg.FileNames)
                {
                    try
                    {
                        bool success = _viewModel.ImportSpectrumFromFile(file);
                        if (!success)
                        {
                            // 跳过无法解析的文件，但继续处理其它文件
                            continue;
                        }

                        // 导入成功后触发绘图刷新（仅显示最后一个导入的文件）
                        TxtFileInfo.Text = file;
                        TxtPreprocessStatus.Text = _viewModel.PreprocessStatus;
                        RefreshPlot();

                        // 自动保存到实测光谱库（RamanSpectrum）
                        try
                        {
                            var px = _viewModel.CurrentX;
                            var py = _viewModel.CurrentYRaw;
                            if (py != null && py.Length > 0)
                            {
                                var model = new RamanSpectrumModel();
                                model.Name = _viewModel.CurrentSubstanceName ?? System.IO.Path.GetFileNameWithoutExtension(file);
                                model.NameAuto = "";
                                model.AcquiredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                model.Points = py.Length;

                                // 序列化为二进制 BLOB
                                model.X_cm = SpectrumProcessingService.SerializeSpectrumData(px ?? new double[0]);
                                model.Y_cm = SpectrumProcessingService.SerializeSpectrumData(py);
                                // 为兼容旧表结构，将纳米字段也写入（使用相同数据）
                                model.X_nm = model.X_cm ?? new byte[0];
                                model.Y_nm = model.Y_cm ?? new byte[0];

                                db.SaveSubstance(model, "RamanSpectrum");
                                successCount++;
                            }
                        }
                        catch { }
                    }
                    catch { }
                }

                // 批量完成后从数据库重新加载 ViewModel 缓存并刷新 UI 列表
                try
                {
                    _viewModel?.ReloadDataList();
                }
                catch { }
                RefreshMainList();
                TxtFileInfo.Text = $"已导入 {successCount}/{total} 个文件";
                if (successCount == 0)
                {
                    MessageBox.Show("未能导入任何文件，请确认文件格式为两列 (X,Y) 或单列 Y。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"批量导入完成：成功 {successCount}，共 {total} 个文件。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }


















































        }

        /// <summary>
        /// 左侧工具栏：导出选中光谱为 CSV
        /// </summary>
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            // 导出整个标准参考库为 JSON 文件
            try
            {
                if (RbReference.IsChecked != true)
                {
                    MessageBox.Show("该导出功能仅用于标准参考库，请先切换到“标准参考库”选项卡。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 获取参考库数据（优先使用 ViewModel 缓存）
                var list = _viewModel?.ReferenceSpectraList != null ? new System.Collections.Generic.List<ReferenceSpectrumModel>(_viewModel.ReferenceSpectraList) : new DatabaseService().GetReferenceLibrary();

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
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(list, Newtonsoft.Json.Formatting.Indented);
                    System.IO.File.WriteAllText(saveDialog.FileName, json, System.Text.Encoding.UTF8);
                    MessageBox.Show("标准参考库已导出为 JSON。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 左侧工具栏：使用 CSV 智能导入向导导入文件
        /// </summary>
        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            // 导入整个标准参考库（JSON 格式）
            try
            {
                if (RbReference.IsChecked != true)
                {
                    MessageBox.Show("该导入功能仅用于标准参考库，请先切换到“标准参考库”选项卡。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new Microsoft.Win32.OpenFileDialog()
                {
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Multiselect = false
                };

                bool? ok = dlg.ShowDialog(this);
                if (ok != true) return;

                string txt = System.IO.File.ReadAllText(dlg.FileName);

                System.Collections.Generic.List<ReferenceSpectrumModel> list = null;
                try
                {
                    list = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<ReferenceSpectrumModel>>(txt);
                }
                catch
                {
                    try
                    {
                        var single = Newtonsoft.Json.JsonConvert.DeserializeObject<ReferenceSpectrumModel>(txt);
                        if (single != null)
                            list = new System.Collections.Generic.List<ReferenceSpectrumModel> { single };
                    }
                    catch { }
                }

                if (list == null || list.Count == 0)
                {
                    MessageBox.Show("未能从 JSON 文件中解析出标准库数据。请确认文件格式为导出的参考库 JSON。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 补充 CreatedAt 字段及默认值
                foreach (var item in list)
                {
                    if (string.IsNullOrEmpty(item.CreatedAt)) item.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (item.PeaksJson == null) item.PeaksJson = "[]";
                    if (item.X_cm == null) item.X_cm = "[]";
                    if (item.Y_cm == null) item.Y_cm = "[]";
                }

                var db = new DatabaseService();
                db.ImportReferenceSpectra(list);

                // 刷新 ViewModel 缓存并 UI 列表
                if (_viewModel != null)
                {
                    var refreshed = db.GetReferenceLibrary();
                    _viewModel.ReferenceSpectraList = new System.Collections.ObjectModel.ObservableCollection<ReferenceSpectrumModel>(refreshed);
                    _viewModel.PreprocessStatus = $"已导入 {list.Count} 条参考谱到标准库";
                }

                RefreshMainList();
                MessageBox.Show($"成功导入 {list.Count} 条参考谱到标准库。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ViewModel 属性变化处理
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 监听关键属性变化，触发UI更新
            if (e.PropertyName == nameof(MainViewModel.CurrentYProcessed))
            {
                // 预处理完成，更新绘图（可选，如果需要自动刷新）
                // RefreshPlot();
            }
        }

        #region UI 事件处理 - 转发到 ViewModel 命令

        /// <summary>
        /// 重置按钮 → 转发到 ResetCommand
        /// </summary>
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.ResetCommand.CanExecute(null) == true)
                _viewModel.ResetCommand.Execute(null);

            // 清空绘图
            ShowRawDataOnly();
        }

        /// <summary>
        /// 删除按钮（批量删除）
        /// 支持在实测库或参考库视图中勾选多条记录后一次性删除
        /// </summary>
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel == null)
                {
                    MessageBox.Show("未找到 ViewModel 实例。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 根据当前视图选择对应集合
                if (RbExperimental.IsChecked == true)
                {
                    var toDelete = _viewModel.RamanSpectraList?.Where(x => x.IsChecked).ToList() ?? new System.Collections.Generic.List<RamanSpectrumModel>();
                    if (toDelete.Count == 0)
                    {
                        MessageBox.Show("请先在列表中勾选要删除的实测光谱记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (MessageBox.Show($"确认删除选中的 {toDelete.Count} 条实测光谱？", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;

                    var db = new DatabaseService();
                    foreach (var item in toDelete)
                    {
                        try { db.DeleteSubstance(item.Id, "RamanSpectrum"); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"删除实测光谱 {item.Id} 失败: {ex.Message}"); }
                    }

                    RefreshMainList();
                    MessageBox.Show($"已删除 {toDelete.Count} 条实测光谱。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var toDelete = _viewModel.ReferenceSpectraList?.Where(x => x.IsChecked).ToList() ?? new System.Collections.Generic.List<ReferenceSpectrumModel>();
                    if (toDelete.Count == 0)
                    {
                        MessageBox.Show("请先在列表中勾选要删除的参考光谱记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (MessageBox.Show($"确认删除选中的 {toDelete.Count} 条参考光谱？", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;

                    var db = new DatabaseService();
                    foreach (var item in toDelete)
                    {
                        try { db.DeleteSubstance(item.Id, "ReferenceSpectrum"); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"删除参考光谱 {item.Id} 失败: {ex.Message}"); }
                    }

                    RefreshMainList();
                    MessageBox.Show($"已删除 {toDelete.Count} 条参考光谱。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 预处理按钮 → 转发到 PreprocessCommand
        /// </summary>
        private void BtnDoPreprocess_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.PreprocessCommand.CanExecute(null) == true)
                _viewModel.PreprocessCommand.Execute(null);

            // 触发绘图刷新
            RefreshPlot();
        }

        /// <summary>
        /// 将当前预处理后的光谱录入到标准库（弹出 SaveWindow 供用户编辑元数据）
        /// </summary>
        private void BtnAddToLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 检查是否存在已处理数据
                var px = _viewModel?.CurrentXProcessed;
                var py = _viewModel?.CurrentYProcessed;
                var peaks = _viewModel?.CurrentPeaks;
                if (px == null || py == null || px.Length == 0 || py.Length == 0)
                {
                    MessageBox.Show("当前没有可入库的已处理光谱，请先导入并预处理光谱。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 打开录入对话框，传入默认名称和检测到的峰位与全谱缓存
                var saveWin = new SaveWindow(_viewModel.CurrentSubstanceName ?? "新物质", peaks ?? new System.Collections.Generic.List<PeakInfo>(), px, py);
                saveWin.Owner = this;
                bool? ok = saveWin.ShowDialog();
                if (ok == true && saveWin.ResultModel != null)
                {
                    var model = saveWin.ResultModel;

                    // 直接使用新的 DatabaseService 保存到 ReferenceSpectrum 表
                    try
                    {
                        var db = new DatabaseService();
                        db.SaveSubstance(model, "ReferenceSpectrum");

                        // 将新记录加入 ViewModel 的集合以立即更新 UI
                        if (_viewModel.ReferenceSpectraList != null)
                        {
                            _viewModel.ReferenceSpectraList.Insert(0, model);
                        }

                        if (_viewModel != null)
                            _viewModel.PreprocessStatus = $"已将 {model.SubstanceName} 存入标准库";
                        RefreshMainList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"入库失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"入库流程出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 算法分类按钮 → 转发到 ClassifyCommand
        /// </summary>
        private void BtnMlClassify_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.ClassifyCommand.CanExecute(null) == true)
                _viewModel.ClassifyCommand.Execute(null);
        }

        /// <summary>
        /// 数据表格选择变化
        /// </summary>
        private void DgMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // 清理旧的叠加曲线
                if (_overlayPlottables.Count > 0)
                {
                    foreach (var p in _overlayPlottables)
                    {
                        try { MainPlot.Plot.Remove(p); } catch { }
                    }
                    _overlayPlottables.Clear();
                }

                // 单选行为：保持原有逻辑（加载到 ViewModel 并显示）
                if (DgMain.SelectedItems != null && DgMain.SelectedItems.Count == 1 && DgMain.SelectedItem is RamanSpectrumModel single)
                {
                    _viewModel.SelectedRamanSpectrum = single;
                    if (_viewModel?.LoadRamanDataCommand.CanExecute(null) == true)
                        _viewModel.LoadRamanDataCommand.Execute(null);

                    RefreshPlot();
                    return;
                }

                // 多选行为：将所有选中的实测光谱解包并在同一张图上叠加用于比对
                if (DgMain.SelectedItems != null && DgMain.SelectedItems.Count > 1)
                {
                    int colorIdx = 0;
                    bool anyPlotted = false;

                    foreach (var obj in DgMain.SelectedItems)
                    {
                        if (obj is RamanSpectrumModel r)
                        {
                            try
                            {
                                var x = SpectrumProcessingService.DeserializeSpectrumData(r.X_cm);
                                var y = SpectrumProcessingService.DeserializeSpectrumData(r.Y_cm);
                                if (x == null || y == null || x.Length == 0 || y.Length == 0) continue;

                                int n = Math.Min(x.Length, y.Length);
                                if (n <= 0) continue;

                                if (x.Length != n) { var tx = new double[n]; Array.Copy(x, tx, n); x = tx; }
                                if (y.Length != n) { var ty = new double[n]; Array.Copy(y, ty, n); y = ty; }

                                var col = OverlayColors[colorIdx % OverlayColors.Length];
                                var pl = MainPlot.Plot.AddScatter(x, y, label: r.Name ?? $"Id:{r.Id}", color: col);
                                pl.LineWidth = 2;
                                _overlayPlottables.Add(pl);
                                colorIdx++;
                                anyPlotted = true;
                            }
                            catch { }
                        }
                    }

                    if (anyPlotted)
                    {
                        InitCrosshairs();
                        MainPlot.Plot.AxisAuto();
                        var legend = MainPlot.Plot.Legend(true, Alignment.UpperRight);
                        legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);
                        MainPlot.Plot.Title("多谱比较");
                        MainPlot.Refresh();
                    }
                }
                else if (DgMain.SelectedItem is ReferenceSpectrumModel refSpectrum)
                {
                    _viewModel.SelectedReferenceSpectrum = refSpectrum;
                    // 加载参考光谱预览并在右侧绘图区显示
                    try
                    {
                        ShowReferenceSpectrum(refSpectrum);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// 数据表格双击 - 显示详情窗口
        /// </summary>
        private void DgMain_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // 仅针对实测库的行双击打开详情编辑窗口
                if (DgMain.SelectedItem is RamanSpectrumModel model)
                {
                    var win = new SpectrumDetailWindow(model) { Owner = this };
                    bool? ok = win.ShowDialog();
                    if (ok == true)
                    {
                        try
                        {
                            var db = new DatabaseService();
                            db.UpdateRamanSpectrum(model);
                            RefreshMainList();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"保存实测光谱元数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else if (DgMain.SelectedItem is ReferenceSpectrumModel refModel)
                {
                    try
                    {
                        var win = new SaveWindow(refModel) { Owner = this };
                        bool? ok = win.ShowDialog();
                        if (ok == true && win.ResultModel != null)
                        {
                            try
                            {
                                var db = new DatabaseService();
                                db.UpdateReference(win.ResultModel);
                                try { _viewModel?.ReloadDataList(); } catch { }
                                RefreshMainList();
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"保存参考光谱元数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// 导出报告按钮：将当前已处理光谱（含特征峰、最近一次分类结果与算法参数）导出为 JSON
        /// </summary>
        private void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "导出光谱报告",
                DefaultExt = ".json",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (saveDialog.ShowDialog() != true) return;

            try
            {
                var payload = new
                {
                    ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    SubstanceName = _viewModel?.CurrentSubstanceName,
                    Classification = _viewModel?.ClassificationResult,
                    ClassificationConfidence = _viewModel?.ClassificationConfidence,
                    X = _viewModel?.CurrentXProcessed,
                    Y = _viewModel?.CurrentYProcessed,
                    Peaks = _viewModel?.CurrentPeaks?
                        .Select(p => new { X = p.X, Y = p.Y }),
                    AlgorithmConfig = _viewModel?.CurrentConfig
                };

                string json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                File.WriteAllText(saveDialog.FileName, json, System.Text.Encoding.UTF8);

                MessageBox.Show(
                    $"光谱报告已导出到:\n{saveDialog.FileName}\n\n共 {( _viewModel?.CurrentYProcessed?.Length ?? 0)} 个数据点。",
                    "导出成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 智能比对按钮
        /// </summary>
        private void BtnMatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel == null)
                {
                    MessageBox.Show("未找到 ViewModel 实例。");
                    return;
                }

                var results = _viewModel.MatchCurrentSpectrum();
                if (results == null || results.Count == 0)
                {
                    MessageBox.Show("未获得比对结果或当前光谱为空。");
                    return;
                }

                var win = new MatchResultsWindow(results)
                {
                    Owner = this
                };

                win.OnResultSelected += (selected) =>
                {
                    // 在主图上叠加参考峰虚线
                    OverlayReferencePeaks(selected);
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"智能比对失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在主图上叠加参考峰位虚线（来自 MatchResult.PeaksJson）
        /// </summary>
        private void OverlayReferencePeaks(MatchResult selected)
        {
            try
            {
                // 清除旧的参考峰线（通过跟踪列表清理）
                if (_refPlottables.Count > 0)
                {
                    foreach (var p in _refPlottables)
                    {
                        try { MainPlot.Plot.Remove(p); } catch { }
                    }
                    _refPlottables.Clear();
                }

                if (selected == null || string.IsNullOrEmpty(selected.PeaksJson))
                {
                    MainPlot.Refresh();
                    return;
                }

                List<double> peaks;
                try
                {
                    peaks = JsonConvert.DeserializeObject<List<double>>(selected.PeaksJson);
                }
                catch
                {
                    peaks = new List<double>();
                }

                if (peaks == null || peaks.Count == 0)
                {
                    MainPlot.Refresh();
                    return;
                }

                // 取当前 Y 轴最大值用于线高
                var yLimits = MainPlot.Plot.GetAxisLimits();
                double yMax = yLimits.YMax;
                if (double.IsNaN(yMax) || double.IsInfinity(yMax)) yMax = 1.0;

                foreach (var px in peaks)
                {
                    double[] xs = new[] { px, px };
                    double[] ys = new[] { 0.0, yMax };
                    var pl = MainPlot.Plot.AddScatter(xs, ys, label: null, color: System.Drawing.Color.Orange);
                    pl.LineStyle = ScottPlot.LineStyle.Dash;
                    pl.LineWidth = 1;
                    _refPlottables.Add(pl);
                }

                MainPlot.Plot.Title($"光谱图: {_viewModel.CurrentSubstanceName} | 比对: {selected.Name} ({selected.CombinedScore:F1})");
                MainPlot.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OverlayReferencePeaks error: " + ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// 在右侧绘图区显示选中的参考光谱
        /// </summary>
        private void ShowReferenceSpectrum(ReferenceSpectrumModel refModel)
        {
            if (refModel == null) return;

            // 清理之前的参考 plottable
            if (_refPlottables.Count > 0)
            {
                foreach (var p in _refPlottables)
                {
                    try { MainPlot.Plot.Remove(p); } catch { }
                }
                _refPlottables.Clear();
            }

            double[] x = null;
            double[] y = null;

            // 尝试从 ReferenceSpectrumModel 的 X_cm/Y_cm (JSON) 解析
            try
            {
                if (!string.IsNullOrEmpty(refModel.X_cm))
                {
                    x = Newtonsoft.Json.JsonConvert.DeserializeObject<double[]>(refModel.X_cm);
                }
            }
            catch { x = null; }

            try
            {
                if (!string.IsNullOrEmpty(refModel.Y_cm))
                {
                    y = Newtonsoft.Json.JsonConvert.DeserializeObject<double[]>(refModel.Y_cm);
                }
            }
            catch { y = null; }

            // 如果解析失败但字段可能存为二进制（兼容旧结构），尝试使用 DeserializeSpectrumData
            try
            {
                if ((x == null || x.Length == 0) && refModel.X_cm != null)
                {
                    // 尝试将 string 转为 byte[] 再反序列化（若内容为base64或直接二进制文本会失败，但保守尝试）
                    try
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(refModel.X_cm);
                        x = SpectrumProcessingService.DeserializeSpectrumData(bytes);
                    }
                    catch { }
                }

                if ((y == null || y.Length == 0) && refModel.Y_cm != null)
                {
                    try
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(refModel.Y_cm);
                        y = SpectrumProcessingService.DeserializeSpectrumData(bytes);
                    }
                    catch { }
                }
            }
            catch { }

            if (x == null || y == null || x.Length == 0 || y.Length == 0)
            {
                // 无法获取数据，显示提示并返回
                TxtPreprocessStatus.Text = "未找到参考谱的全谱数据可用于预览";
                return;
            }

            try
            {
                int n = Math.Min(x.Length, y.Length);
                if (x.Length != n) { var tx = new double[n]; Array.Copy(x, tx, n); x = tx; }
                if (y.Length != n) { var ty = new double[n]; Array.Copy(y, ty, n); y = ty; }

                var pl = MainPlot.Plot.AddScatter(x, y, label: refModel.SubstanceName ?? $"Ref:{refModel.Id}", color: System.Drawing.Color.DarkGreen);
                pl.LineWidth = 2;
                _refPlottables.Add(pl);

                InitCrosshairs();
                MainPlot.Plot.AxisAuto();
                MainPlot.Plot.Title($"参考谱: {refModel.SubstanceName}");
                var legend = MainPlot.Plot.Legend(true, Alignment.UpperRight);
                legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);
                MainPlot.Refresh();
            }
            catch (Exception ex)
            {
                TxtPreprocessStatus.Text = $"参考谱绘制失败: {ex.Message}";
            }
        }

        #region 算法参数窗口

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel == null)
                {
                    MessageBox.Show("未找到 ViewModel 实例。");
                    return;
                }

                var settingsWindow = new SettingsWindow(_viewModel.CurrentConfig)
                {
                    Owner = this
                };

                bool? dlg = settingsWindow.ShowDialog();
                if (dlg == true)
                {
                    // SettingsWindow 已经直接修改了传入的 Config 引用，但仍显式设置以触发绑定通知
                    _viewModel.CurrentConfig = settingsWindow.Config;
                    TxtPreprocessStatus.Text = "算法参数已更新";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开算法参数窗口失败: {ex.Message}");
            }
        }

        #endregion

        #region 左侧库视图切换事件

        private void RbReference_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
                    _viewModel.IsRamanMode = false;
                    RefreshMainList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"切换到参考库失败: {ex.Message}");
            }
        }

        private void RbExperimental_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
                    _viewModel.IsRamanMode = true;
                    RefreshMainList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"切换到实测库失败: {ex.Message}");
            }
        }

        #endregion

        #region UI 初始化和数据列表刷新

        /// <summary>
        /// 初始化绘图样式
        /// </summary>
        private void InitPlotStyle()
        {
            MainPlot.Plot.XLabel("拉曼位移 (cm⁻¹)");
            MainPlot.Plot.YLabel("强度");
            MainPlot.Plot.Title("拉曼光谱分析系统");
            MainPlot.Plot.Style(ScottPlot.Style.Control);
        }

        /// <summary>
        /// 刷新主数据列表
        /// </summary>
        private void RefreshMainList()
        {
            try
            {
                DgMain.Columns.Clear();
                DgMain.ItemsSource = null;

                // 确保DataGrid可编辑，使用户能点击Checkbox
                DgMain.IsReadOnly = false;

                // 1. 添加复选框列
                var checkColumn = new DataGridTemplateColumn
                {
                    Width = 50
                };

                // Header 全选复选框
                var headerChk = new CheckBox
                {
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    ToolTip = "全选/取消全选"
                };
                headerChk.Checked += HeaderCheckBox_Checked;
                headerChk.Unchecked += HeaderCheckBox_Unchecked;
                checkColumn.Header = headerChk;

                var factory = new FrameworkElementFactory(typeof(CheckBox));
                factory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                });
                factory.SetValue(CheckBox.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
                factory.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);

                checkColumn.CellTemplate = new DataTemplate { VisualTree = factory };
                DgMain.Columns.Add(checkColumn);

                // 2. 根据模式加载数据
                if (RbExperimental.IsChecked == true)
                {
                    // 实测光谱模式
                    if (_viewModel != null)
                    {
                        DgMain.ItemsSource = _viewModel.RamanSpectraList;
                        AddColumn("ID", "Id", 50);
                        AddColumn("样品名称", "Name", 120);
                        AddColumn("分子式", "NameAuto", 100);
                        AddColumn("采集时间", "AcquiredAt", 140);
                        AddColumn("数据点数", "Points", 80);
                        AddColumn("波数数据(cm⁻¹)", "X_cm_Display", 120);
                        AddColumn("强度数据", "Y_cm_Display", 120);
                        AddColumn("积分时间(s)", "IntegrationTime", 90);
                        AddColumn("光栅", "Grating", 100);
                        AddColumn("激光功率(mW)", "LaserPower", 100);
                        AddColumn("激发波长", "ExcitationWavelength", 100);
                        AddColumn("处理方法", "ProcessMethod_Display", 120);
                        AddColumn("备注/实测成分", "Note_Display", 150);
                    }
                }
                else
                {
                    // 参考光谱模式
                    if (_viewModel != null)
                    {
                        DgMain.ItemsSource = _viewModel.ReferenceSpectraList;
                        AddColumn("ID", "Id", 50);
                        AddColumn("物质名称", "SubstanceName", 130);
                        AddColumn("化学式", "ChemicalFormula", 110);
                        AddColumn("CAS号", "CasNumber", 110);
                        AddColumn("激发波长", "ExcitationWavelength", 100);
                        AddColumn("光栅", "Grating", 100);
                        AddColumn("特征指纹峰位", "PeaksJson_Display", 150);
                        AddColumn("创建时间", "CreatedAt", 140);
                        AddColumn("标准全谱X", "X_cm_Display", 120);
                        AddColumn("标准全谱Y", "Y_cm_Display", 120);
                        AddColumn("备注", "Note_Display", 150);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加数据列
        /// </summary>
        private void AddColumn(string header, string bindingPath, double width)
        {
            DgMain.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(bindingPath),
                Width = width,
                IsReadOnly = true
            });
        }

        #endregion

        #region 绘图逻辑 - 保留在 Code-Behind

        /// <summary>
        /// 显示原始数据（无处理）
        /// </summary>
        private void ShowRawDataOnly()
        {
            if (_viewModel?.CurrentYRaw == null || _viewModel?.CurrentX == null)
                return;

            MainPlot.Plot.Clear();

            var displayX = _viewModel.CurrentX;
            var displayY = _viewModel.CurrentYRaw;

            if (_viewModel?.CurrentConfig?.CropBelow200 == true)
            {
                var cropped = ApplyLowFrequencyCutoff(displayX, displayY, 200.0);
                displayX = cropped.croppedX;
                displayY = cropped.croppedY;
            }

            var rawPlot = MainPlot.Plot.AddScatter(displayX, displayY, label: "原始信号");
            rawPlot.Color = System.Drawing.Color.FromArgb(120, System.Drawing.Color.Black);
            rawPlot.LineWidth = 1;
            rawPlot.MarkerSize = 0;

            InitCrosshairs();
            MainPlot.Plot.Title($"光谱图: {_viewModel.CurrentSubstanceName} (原始数据)");
            MainPlot.Plot.AxisAuto();

            var legend = MainPlot.Plot.Legend(true, Alignment.UpperRight);
            legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);

            MainPlot.Refresh();
        }

        /// <summary>
        /// 刷新绘图（显示预处理结果）
        /// </summary>
        private void RefreshPlot()
        {
            if (_viewModel?.CurrentYProcessed == null || _viewModel?.CurrentXProcessed == null)
            {
                ShowRawDataOnly();
                return;
            }

            MainPlot.Plot.Clear();

            var activeX = _viewModel.CurrentXProcessed;
            var activeYRaw = _viewModel.CurrentYRaw;
            var currentY = _viewModel.CurrentYProcessed;
            var peaks = _viewModel.CurrentPeaks ?? new List<PeakInfo>();

            // 绘制背景原始信号（若长度不一致则截断至最小长度以避免异常）
            if (activeX.Length != activeYRaw.Length)
            {
                int n = Math.Min(activeX.Length, activeYRaw.Length);
                var xForRaw = new double[n];
                var yRawToPlot = new double[n];
                Array.Copy(activeX, xForRaw, n);
                Array.Copy(activeYRaw, yRawToPlot, n);
                MainPlot.Plot.AddScatter(xForRaw, yRawToPlot, label: "原始信号",
                    color: System.Drawing.Color.FromArgb(80, System.Drawing.Color.Gray));
            }
            else
            {
                MainPlot.Plot.AddScatter(activeX, activeYRaw, label: "原始信号",
                    color: System.Drawing.Color.FromArgb(80, System.Drawing.Color.Gray));
            }

            // 绘制处理后的主曲线
            var processedPlot = MainPlot.Plot.AddScatter(activeX, currentY, label: "处理结果", 
                color: System.Drawing.Color.Blue);
            processedPlot.LineWidth = 2;

            // 标记峰值
            MarkPeaksOnPlot(peaks);

            InitCrosshairs();
            MainPlot.Plot.AxisAuto();

            if (_viewModel?.CurrentConfig?.EnableNormalization == true)
            {
                MainPlot.Plot.SetAxisLimits(yMin: -0.05, yMax: 1.2);
            }

            var legend = MainPlot.Plot.Legend(true, Alignment.UpperRight);
            legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);

            MainPlot.Plot.Title($"光谱图: {_viewModel.CurrentSubstanceName}");
            MainPlot.Refresh();
        }

        /// <summary>
        /// 在绘图上标记峰值
        /// </summary>
        private void MarkPeaksOnPlot(List<PeakInfo> peaks)
        {
            if (peaks == null || peaks.Count == 0)
                return;

            foreach (var peak in peaks)
            {
                // 绘制垂直线标记峰位
                double[] xs = new[] { peak.X, peak.X };
                double[] ys = new[] { 0, peak.Y };
                MainPlot.Plot.AddScatter(xs, ys, label: null, 
                    color: System.Drawing.Color.FromArgb(100, System.Drawing.Color.Red));

                // 标记文本（可选）
                // MainPlot.Plot.AddText($"{peak.X:F1}", peak.X, peak.Y, size: 10);
            }
        }

        /// <summary>
        /// 初始化十字光标
        /// </summary>
        private void InitCrosshairs()
        {
            MainPlot.Plot.Remove(FreeCrosshair);
            MainPlot.Plot.Remove(SnapCrosshair);

            FreeCrosshair = MainPlot.Plot.AddCrosshair(0, 0);
            FreeCrosshair.LineStyle = ScottPlot.LineStyle.Dash;
            FreeCrosshair.Color = System.Drawing.Color.FromArgb(150, System.Drawing.Color.Red);
            FreeCrosshair.IsVisible = false;

            SnapCrosshair = MainPlot.Plot.AddCrosshair(0, 0);
            SnapCrosshair.LineStyle = ScottPlot.LineStyle.Dash;
            SnapCrosshair.Color = System.Drawing.Color.FromArgb(150, System.Drawing.Color.Green);
            SnapCrosshair.IsVisible = false;

            MainPlot.MouseMove -= MainPlot_MouseMove;
            MainPlot.MouseMove += MainPlot_MouseMove;
            MainPlot.MouseDown -= MainPlot_MouseDown;
            MainPlot.MouseDown += MainPlot_MouseDown;
        }

        /// <summary>
        /// 低频区间裁剪
        /// </summary>
        private (double[] croppedX, double[] croppedY) ApplyLowFrequencyCutoff(double[] x, double[] y, double cutoffWavenumber)
        {
            int startIndex = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] >= cutoffWavenumber)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex == 0)
                return (x, y);

            int count = x.Length - startIndex;
            double[] croppedX = new double[count];
            double[] croppedY = new double[count];

            Array.Copy(x, startIndex, croppedX, 0, count);
            Array.Copy(y, startIndex, croppedY, 0, count);

            return (croppedX, croppedY);
        }

        /// <summary>
        /// 鼠标移动 - 显示坐标信息
        /// </summary>
        private void MainPlot_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(MainPlot);
            (double px, double py) = MainPlot.GetMouseCoordinates();

            FreeCrosshair.X = px;
            FreeCrosshair.Y = py;
            FreeCrosshair.IsVisible = true;
            MainPlot.Refresh();
        }

        /// <summary>
        /// 鼠标点击 - 吸附到最近的峰值
        /// </summary>
        private void MainPlot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var peaks = _viewModel?.CurrentPeaks;
            if (peaks == null || peaks.Count == 0)
                return;

            (double px, double py) = MainPlot.GetMouseCoordinates();

            // 找到最近的峰
            var closestPeak = peaks.OrderBy(p => Math.Abs(p.X - px)).FirstOrDefault();
            if (closestPeak != null)
            {
                SnapCrosshair.X = closestPeak.X;
                SnapCrosshair.Y = closestPeak.Y;
                SnapCrosshair.IsVisible = true;
                MainPlot.Refresh();
            }
        }

        #endregion

        #region 窗口事件

        /// <summary>
        /// 窗口关闭事件
        /// </summary>
        private void Window_Closed(object sender, EventArgs e)
        {
            // 可选：保存配置
            if (_viewModel?.CurrentConfig != null)
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(_viewModel.CurrentConfig, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(path, json);
                }
                catch { /* 忽略保存错误 */ }
            }
        }

        #endregion

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
