using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ScottPlot;
using MathNet.Numerics;
using MathNet.Numerics.Statistics;
using Newtonsoft.Json;

namespace SpectrumAnalyzer
{
    public partial class MainWindow : System.Windows.Window
    {
        // --- 全局变量 ---
        private ScottPlot.Plottable.Crosshair FreeCrosshair;
        private ScottPlot.Plottable.Crosshair SnapCrosshair;

        private string currentSubstanceName = "未知物质";
        private DatabaseService dbService = new DatabaseService();

        private double[] currentX;
        private double[] currentYRaw;
        private double[] currentY;
        private double[] currentXProcessed; // === 保存与当前 currentY 长度严格同步的 X 轴 ===

        // 当前计算出的特征峰缓存，用以保证双向交互与存库时数据一致性
        private List<PeakInfo> currentPeaks = new List<PeakInfo>();

        // 缓存当前光谱的理想化学式和实测成分，用于传递给存库窗口
        private string currentIdealChemistry = "";
        private string currentMeasuredChemistry = "";

        private AlgorithmConfig currentConfig = new AlgorithmConfig();

        // === 多谱同屏叠加色彩调色板 ===
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

        public MainWindow()
        {
            InitializeComponent();
            LoadConfigFromLocal(); // 在构造函数中引入读取外部配置逻辑
            InitPlotStyle();
            RefreshMainList();
        }

        // === 在系统加载时，自动尝试从本地 config.json 恢复算法参数模型 ===
        private void LoadConfigFromLocal()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var savedConfig = JsonConvert.DeserializeObject<AlgorithmConfig>(json);
                    if (savedConfig != null)
                    {
                        this.currentConfig = savedConfig;
                    }
                }
            }
            catch
            {
                // 容错：如果读取或解析配置文件异常，系统降级采用 AlgorithmConfig.cs 默认值运行
                this.currentConfig = new AlgorithmConfig();
            }
        }

        private void InitPlotStyle()
        {
            MainPlot.Plot.XLabel("拉曼位移 (cm⁻¹)");
            MainPlot.Plot.YLabel("强度");
            MainPlot.Plot.Title("拉曼光谱分析系统");
            MainPlot.Plot.Style(ScottPlot.Style.Control);
        }

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

        // MainWindow.xaml.cs -> ProcessAndPlot 重置方法：
        private void ProcessAndPlot(double[] xs, double[] ysRaw)
        {
            this.currentX = xs;
            this.currentYRaw = ysRaw;
            this.currentY = null;
            this.currentXProcessed = null; // 重置当前计算状态的 X 轴
            this.currentPeaks = null; // 清空特征峰缓存
            ShowRawDataOnly();
        }

        private void ShowRawDataOnly()
        {
            if (currentX == null || currentYRaw == null) return;

            var checkedSpectra = GetSelectedSpectra();
            if (checkedSpectra.Count > 0)
            {
                RenderOverlayPlot(checkedSpectra, currentConfig);
                return;
            }

            MainPlot.Plot.Clear();

            // 使原始信号查看模式也同步支持低频动态过滤
            double[] displayX = currentX;
            double[] displayY = currentYRaw;

            if (currentConfig.CropBelow200)
            {
                var cropped = ApplyLowFrequencyCutoff(currentX, currentYRaw, 200.0);
                displayX = cropped.croppedX;
                displayY = cropped.croppedY;
            }

            var rawPlot = MainPlot.Plot.AddScatter(displayX, displayY, label: "原始信号");
            rawPlot.Color = System.Drawing.Color.FromArgb(120, System.Drawing.Color.Black);
            rawPlot.LineWidth = 1;
            rawPlot.MarkerSize = 0;

            InitCrosshairs();
            MainPlot.Plot.Title($"光谱图: {currentSubstanceName} (原始数据)");
            MainPlot.Plot.AxisAuto();

            var legend = MainPlot.Plot.Legend(true, Alignment.UpperRight);
            legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);

            MainPlot.Refresh();
            TxtPreprocessStatus.Text = "状态: 原始信号已加载";
        }

        // 将基础预处理数学校正管线提取为独立方法，便于单谱与多谱叠加复用
        private double[] PreprocessData(double[] rawY, AlgorithmConfig activeConfig)
        {
            double[] y = (double[])rawY.Clone();

            // 1. 去毛刺 (宇宙射线)
            if (activeConfig.EnableDespike)
                y = PreprocessingService.RemoveSpikes(y, activeConfig.DespikeWindow, activeConfig.DespikeZ);

            // 2. SG平滑
            if (activeConfig.EnableSmoothing)
                y = PreprocessingService.SavitzkyGolaySmooth(y, activeConfig.SgWindowSize, activeConfig.SgOrder);

            // 3. 基线扣除
            if (activeConfig.EnableBaseline)
            {
                double[] baseline;

                // 分支选择基线算法，支持随时切换比对效果
                if (activeConfig.BaselineMethod == "Snip")
                {
                    // 调用极速 SNIP 算法
                    baseline = PreprocessingService.SnipBaseline(y, activeConfig.SnipIterations);
                }
                else
                {
                    // 调用原滚动分位数基线算法并执行 SG 平滑
                    baseline = PreprocessingService.QuantileBaseline(y, activeConfig.BaselineWindow, activeConfig.BaselineQuantile);
                    baseline = PreprocessingService.SavitzkyGolaySmooth(baseline, activeConfig.BaselineSmoothWindow, 2);
                }

                // 扣除计算
                for (int i = 0; i < y.Length; i++)
                {
                    y[i] = Math.Max(0, y[i] - baseline[i]);
                }
            }

            // 4. 归一化 (0.0 - 1.0)
            if (activeConfig.EnableNormalization)
                y = PreprocessingService.Normalize(y);

            return y;
        }

        // --- 核心预处理流程 ---
        private void ApplyAlgorithmAndRefresh(AlgorithmConfig config = null)
        {
            AlgorithmConfig activeConfig = config ?? currentConfig;

            try
            {
                var checkedSpectra = GetSelectedSpectra();
                if (checkedSpectra.Count > 0)
                {
                    RenderOverlayPlot(checkedSpectra, activeConfig);
                    return;
                }

                if (currentX == null || currentYRaw == null) return;

                // 建立临时工作数据副本，避免污染全局原始记录
                double[] activeX = (double[])currentX.Clone();
                double[] activeYRaw = (double[])currentYRaw.Clone();

                // 执行低频区间裁剪
                if (activeConfig.CropBelow200)
                {
                    var cropped = ApplyLowFrequencyCutoff(activeX, activeYRaw, 200.0);
                    activeX = cropped.croppedX;
                    activeYRaw = cropped.croppedY;
                }

                // 使用过滤后的 activeYRaw 进行基线扣除与平滑
                double[] y = PreprocessData(activeYRaw, activeConfig);
                this.currentY = y;
                this.currentXProcessed = activeX; // 将当前对齐且裁剪后的 X 轴写入计算缓存，保障长度严格一致
                MainPlot.Plot.Clear();

                // 绘制背景原始信号 (使用对齐后的 activeX)
                MainPlot.Plot.AddScatter(activeX, activeYRaw, label: "原始信号", color: System.Drawing.Color.FromArgb(80, System.Drawing.Color.Gray));

                // 绘制处理后的主曲线
                var processedPlot = MainPlot.Plot.AddScatter(activeX, currentY, label: "处理结果", color: System.Drawing.Color.Blue);
                processedPlot.LineWidth = 2;

                // 寻峰与标注 (传入对齐后的 activeX)
                var peaks = PreprocessingService.FindPeaksAdvanced(activeX, currentY, activeConfig);
                this.currentPeaks = peaks;
                MarkPeaksOnPlot(peaks);

                InitCrosshairs();
                MainPlot.Plot.AxisAuto();

                if (activeConfig.EnableNormalization)
                {
                    MainPlot.Plot.SetAxisLimits(yMin: -0.05, yMax: 1.2);
                }

                var legend = MainPlot.Plot.Legend(true, Alignment.UpperRight);
                legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);

                MainPlot.Plot.Title($"光谱图: {currentSubstanceName}");
                MainPlot.Refresh();

                string mode = (config == null) ? "手动配置" : "智能匹配";
                TxtPreprocessStatus.Text = $"状态: [{mode}] 处理完成，识别到 {peaks.Count} 个特征峰";
            }
            catch (Exception ex)
            {
                MessageBox.Show("预处理失败: " + ex.Message);
            }
        }

        // 从 DataGrid 数据源中过滤并反序列化所有处于勾选状态的光谱数据
        private List<(string Name, double[] X, double[] YRaw)> GetSelectedSpectra()
        {
            var selectedList = new List<(string Name, double[] X, double[] YRaw)>();
            if (DgMain.ItemsSource == null) return selectedList;

            if (RbExperimental.IsChecked == true)
            {
                if (DgMain.ItemsSource is IEnumerable<RamanSpectrumModel> expList)
                {
                    foreach (var item in expList)
                    {
                        if (item.IsChecked)
                        {
                            try
                            {
                                // 核心修改：不经过 JSON，直接将二进制 byte[] 块复制还原为 double[] 数组
                                double[] xs = new double[item.X_cm.Length / sizeof(double)];
                                Buffer.BlockCopy(item.X_cm, 0, xs, 0, item.X_cm.Length);

                                double[] ys = new double[item.Y_cm.Length / sizeof(double)];
                                Buffer.BlockCopy(item.Y_cm, 0, ys, 0, item.Y_cm.Length);
                                if (xs != null && ys != null)
                                {
                                    selectedList.Add((item.Name, xs, ys));
                                }
                            }
                            catch { /* 忽略数据序列化异常 */ }
                        }
                    }
                }
            }
            else
            {
                if (DgMain.ItemsSource is IEnumerable<ReferenceSpectrumModel> refList)
                {
                    foreach (var item in refList)
                    {
                        if (item.IsChecked)
                        {
                            try
                            {
                                var xs = JsonConvert.DeserializeObject<double[]>(item.X_cm);
                                var ys = JsonConvert.DeserializeObject<double[]>(item.Y_cm);
                                if (xs != null && ys != null)
                                {
                                    selectedList.Add((item.SubstanceName, xs, ys));
                                }
                            }
                            catch { /* 忽略数据序列化异常 */ }
                        }
                    }
                }
            }
            return selectedList;
        }

        // 多谱同屏对比与叠加绘制逻辑
        private void RenderOverlayPlot(List<(string Name, double[] X, double[] YRaw)> selected, AlgorithmConfig activeConfig)
        {
            MainPlot.Plot.Clear();

            for (int i = 0; i < selected.Count; i++)
            {
                var spectrum = selected[i];

                double[] localX = (double[])spectrum.X.Clone();
                double[] localYRaw = (double[])spectrum.YRaw.Clone();

                // 多谱叠加对比下同样执行低频裁剪
                if (activeConfig.CropBelow200)
                {
                    var cropped = ApplyLowFrequencyCutoff(localX, localYRaw, 200.0);
                    localX = cropped.croppedX;
                    localYRaw = cropped.croppedY;
                }

                double[] processedY = PreprocessData(localYRaw, activeConfig);
                var color = OverlayColors[i % OverlayColors.Length];

                var plot = MainPlot.Plot.AddScatter(localX, processedY, label: spectrum.Name, color: color);
                plot.LineWidth = 1.8;
                plot.MarkerSize = 0;
            }

            InitCrosshairs();

            // 多谱线同屏时，禁用捕捉十字线以免引起逻辑指向混乱，保留自由移动十字线读取坐标
            if (SnapCrosshair != null) SnapCrosshair.IsVisible = false;

            MainPlot.Plot.AxisAuto();
            if (activeConfig.EnableNormalization)
            {
                MainPlot.Plot.SetAxisLimits(yMin: -0.05, yMax: 1.2);
            }

            var legend = MainPlot.Plot.Legend(true, Alignment.UpperRight);
            legend.FillColor = System.Drawing.Color.FromArgb(180, System.Drawing.Color.White);

            MainPlot.Plot.Title($"拉曼光谱同屏对比 (已叠加 {selected.Count} 条曲线)");
            MainPlot.Refresh();

            TxtPreprocessStatus.Text = $"状态: 同屏对比模式，已成功叠加 {selected.Count} 条数据";
        }

        // Checkbox 被勾选/取消勾选时的实时事件响应
        private void OnSpectrumCheckboxClicked(object sender, RoutedEventArgs e)
        {
            var checkedSpectra = GetSelectedSpectra();

            if (checkedSpectra.Count > 0)
            {
                RenderOverlayPlot(checkedSpectra, currentConfig);
            }
            else
            {
                // 如果用户全部取消勾选，则退回到普通的单条光谱展示模式
                if (DgMain.SelectedItem != null)
                {
                    DgMain_SelectionChanged(DgMain, null);
                }
                else
                {
                    MainPlot.Plot.Clear();
                    MainPlot.Refresh();
                    TxtPreprocessStatus.Text = "状态: 就绪";
                }
            }
        }

        // --- 峰位与相对强度显示逻辑 ---
        private void MarkPeaksOnPlot(List<PeakInfo> peaks)
        {
            foreach (var peak in peaks)
            {
                // 倒三角标记
                MainPlot.Plot.AddMarker(peak.X, peak.Y, MarkerShape.filledTriangleDown, 10, System.Drawing.Color.Red);

                // 显示格式：第一行波数，第二行强度(归一化)
                string labelText = currentConfig.EnableNormalization
                    ? $"{peak.X:F1}\n({peak.Y:F2})"  // 位移细化为1位小数
                    : $"{peak.X:F1}";

                var txt = MainPlot.Plot.AddText(labelText, peak.X, peak.Y);
                txt.Alignment = Alignment.LowerCenter;
                txt.PixelOffsetY = -18; // 向上偏移不挡三角
                txt.Font.Bold = true;
                txt.Font.Size = 13;
                txt.Font.Color = System.Drawing.Color.DarkRed;

                // 垂直指引虚线
                var vline = MainPlot.Plot.AddVerticalLine(peak.X);
                vline.Color = System.Drawing.Color.FromArgb(40, System.Drawing.Color.Red);
                vline.LineStyle = LineStyle.Dot;
            }
        }

        // --- 数据列表刷新 (支持列首复选框机制) ---
        private void RefreshMainList()
        {
            try
            {
                DgMain.Columns.Clear();
                DgMain.ItemsSource = null;

                // 确保 DataGrid 可编辑，从而使用户能点击 Checkbox
                DgMain.IsReadOnly = false;

                // 1. 添加复选框列（使用 DataGridTemplateColumn 并指定 FrameworkElementFactory 实现即时更新）
                var checkColumn = new DataGridTemplateColumn
                {
                    Header = "选择",
                    Width = 50
                };
                var factory = new FrameworkElementFactory(typeof(CheckBox));
                // 双向绑定到 IsChecked 属性，设置 UpdateSourceTrigger 确保勾选后数据立即回写到 Model
                factory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                });
                // 修改后：使用完整的命名空间修饰枚举，解决与 Window 实例属性的冲突
                factory.SetValue(CheckBox.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
                factory.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);

                // 监听 Click 路由事件，使用户在点选复选框的第一时间重新绘制多谱重叠图
                factory.AddHandler(CheckBox.ClickEvent, new RoutedEventHandler(OnSpectrumCheckboxClicked));

                checkColumn.CellTemplate = new DataTemplate { VisualTree = factory };
                DgMain.Columns.Add(checkColumn);

                if (RbExperimental.IsChecked == true)
                {
                    var data = dbService.GetRamanLibrary();
                    // 绑定当前 RamanSpectrum 的完整表结构（与 Model 属性完全一致）
                    AddColumn("ID", "Id", 50);
                    AddColumn("样品名称", "Name", 120);
                    AddColumn("分子式", "NameAuto", 100);
                    AddColumn("采集时间", "AcquiredAt", 140);
                    AddColumn("数据点数", "Points", 80);
                    AddColumn("波数数据(cm⁻¹)", "X_cm_Display", 120);  // 绑定截断显示属性
                    AddColumn("强度数据", "Y_cm_Display", 120);         // 绑定截断显示属性
                    AddColumn("积分时间(s)", "IntegrationTime", 90);
                    AddColumn("光栅", "Grating", 100);
                    AddColumn("激光功率(mW)", "LaserPower", 100);
                    AddColumn("激发波长", "ExcitationWavelength", 100);
                    AddColumn("处理方法", "ProcessMethod_Display", 120); // 绑定截断显示属性
                    AddColumn("备注/实测成分", "Note_Display", 150);      // 绑定截断显示属性
                    DgMain.ItemsSource = data;
                }
                else
                {
                    var data = dbService.GetReferenceLibrary();
                    // 绑定当前 ReferenceSpectrum 的完整表结构（与 Model 属性完全一致）
                    AddColumn("ID", "Id", 50);
                    AddColumn("物质名称", "SubstanceName", 130);
                    AddColumn("化学式", "ChemicalFormula", 110);
                    AddColumn("CAS号", "CasNumber", 110);
                    AddColumn("激发波长", "ExcitationWavelength", 100);
                    AddColumn("光栅", "Grating", 100);
                    AddColumn("特征指纹峰位", "PeaksJson_Display", 150);  // 绑定截断显示属性
                    AddColumn("创建时间", "CreatedAt", 140);
                    AddColumn("标准全谱X", "X_cm_Display", 120);         // 绑定截断显示属性
                    AddColumn("标准全谱Y", "Y_cm_Display", 120);         // 绑定截断显示字段
                    AddColumn("备注", "Note_Display", 150);
                    DgMain.ItemsSource = data;
                }
            }
            catch (Exception ex) { MessageBox.Show("刷新失败: " + ex.Message); }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (currentYRaw == null || currentX == null)
            {
                MessageBox.Show("当前未加载任何光谱数据。", "提示");
                return;
            }

            // 重置时清除当前列表内的所有勾选状态
            if (DgMain.ItemsSource != null)
            {
                if (RbExperimental.IsChecked == true && DgMain.ItemsSource is IEnumerable<RamanSpectrumModel> expList)
                {
                    foreach (var item in expList) item.IsChecked = false;
                }
                else if (DgMain.ItemsSource is IEnumerable<ReferenceSpectrumModel> refList)
                {
                    foreach (var item in refList) item.IsChecked = false;
                }
            }

            currentY = null;
            this.currentPeaks = null; // 重置时清空特征峰缓存
            this.currentIdealChemistry = "";
            this.currentMeasuredChemistry = "";
            ShowRawDataOnly();
            TxtPreprocessStatus.Text = "状态: 已恢复原始信号，重置了多谱选择与预处理效果";

            if (SnapCrosshair != null) SnapCrosshair.IsVisible = false;
        }

        private void AddColumn(string header, string bindingPath, double width)
        {
            DgMain.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(bindingPath),
                Width = width,
                IsReadOnly = true, // 设为只读以保障表格其余单元格文本不被误编辑
                ElementStyle = new System.Windows.Style(typeof(TextBlock))
                {
                    Setters = {
                        new Setter(TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center),
                        new Setter(TextBlock.MarginProperty, new Thickness(10, 0, 10, 0))
                    }
                }
            });
        }

        // --- RRUFF TXT 导入逻辑 (提取多维元数据学术注释版) ---
        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "拉曼光谱数据 (*.txt, *.csv)|*.txt;*.csv|所有文件 (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string fileName in openFileDialog.FileNames)
                {
                    try
                    {
                        string subName = Path.GetFileNameWithoutExtension(fileName);
                        double[] xs = null, ys = null;

                        string idealChemistry = "";
                        string measuredChemistry = "";
                        string statusText = "";
                        string descriptionText = "";
                        string commentText = "";
                        string laserWavelen = "532";

                        string fileExtension = Path.GetExtension(fileName).ToLower();

                        if (fileExtension == ".txt")
                        {
                            // --- 分支 A：解析 RRUFF 标准 TXT 文件 ---
                            List<double> xList = new List<double>();
                            List<double> yList = new List<double>();

                            foreach (var line in File.ReadLines(fileName))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("##NAMES=")) subName = trimmed.Replace("##NAMES=", "").Trim();
                                if (trimmed.StartsWith("##RAMAN WAVELENGTH=")) laserWavelen = trimmed.Replace("##RAMAN WAVELENGTH=", "").Trim();
                                if (trimmed.Contains("##IDEAL CHEMISTRY"))
                                {
                                    int eqIdx = trimmed.IndexOf('=');
                                    if (eqIdx >= 0) idealChemistry = trimmed.Substring(eqIdx + 1).Trim().Replace("_", "");
                                }
                                if (trimmed.Contains("##MEASURED CHEMISTRY"))
                                {
                                    int eqIdx = trimmed.IndexOf('=');
                                    if (eqIdx >= 0) measuredChemistry = trimmed.Substring(eqIdx + 1).Trim();
                                }

                                // === 提取学术验证和警告描述 ===
                                if (trimmed.StartsWith("##STATUS="))
                                {
                                    statusText = trimmed.Replace("##STATUS=", "").Trim();
                                }
                                if (trimmed.StartsWith("##DESCRIPTION="))
                                {
                                    descriptionText = trimmed.Replace("##DESCRIPTION=", "").Trim();
                                }
                                if (trimmed.Contains("##COMMENT"))
                                {
                                    int eqIdx = trimmed.IndexOf('=');
                                    if (eqIdx >= 0) commentText = trimmed.Substring(eqIdx + 1).Trim();
                                }

                                if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed)) continue;
                                var parts = trimmed.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
                                {
                                    xList.Add(x); yList.Add(y);
                                }
                            }
                            if (xList.Count > 0)
                            {
                                xs = xList.ToArray();
                                ys = yList.ToArray();
                            }
                        }
                        else if (fileExtension == ".csv")
                        {
                            // --- 分支 B：调用 CSV 智能导入向导窗口 ---
                            CsvImportWindow importWin = new CsvImportWindow(fileName) { Owner = this };
                            if (importWin.ShowDialog() == true)
                            {
                                xs = importWin.ParsedX;
                                ys = importWin.ParsedY;
                            }
                        }

                        // === 整合学术备注信息 ===
                        List<string> noteParts = new List<string>();
                        if (!string.IsNullOrEmpty(measuredChemistry)) noteParts.Add($"化学组成: {measuredChemistry}");
                        if (!string.IsNullOrEmpty(statusText)) noteParts.Add($"XRD鉴定状态: {statusText}");
                        if (!string.IsNullOrEmpty(descriptionText)) noteParts.Add($"外观描述: {descriptionText}");
                        if (!string.IsNullOrEmpty(commentText)) noteParts.Add($"警告/备注: {commentText}");
                        string mergedNote = string.Join(" | ", noteParts);

                        // === 公共处理逻辑：将解析出的数据存库并绘图 ===
                        if (xs != null && ys != null && xs.Length > 0)
                        {
                            // 1. 【在创建模型之前】先将双精度数组转换为二进制字节数组
                            byte[] xBytes = new byte[xs.Length * sizeof(double)];
                            Buffer.BlockCopy(xs, 0, xBytes, 0, xBytes.Length);

                            byte[] yBytes = new byte[ys.Length * sizeof(double)];
                            Buffer.BlockCopy(ys, 0, yBytes, 0, yBytes.Length);

                            // 2. 【然后】再创建并初始化 RamanSpectrumModel 实例
                            var ramanModel = new RamanSpectrumModel
                            {
                                Name = subName,
                                NameAuto = idealChemistry,
                                Note = mergedNote,
                                Points = xs.Length,
                                X_cm = xBytes,      // 直接赋已转换好的二进制数据
                                Y_cm = yBytes,      // 直接赋已转换好的二进制数据
                                ExcitationWavelength = laserWavelen,
                                ProcessMethod = "原始导入"
                            };
                            dbService.SaveSubstance(ramanModel, "RamanSpectrum");

                            if (fileName == openFileDialog.FileNames.Last())
                            {
                                currentSubstanceName = subName;
                                this.currentIdealChemistry = idealChemistry;
                                this.currentMeasuredChemistry = mergedNote;
                                ProcessAndPlot(xs, ys);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"处理文件 {Path.GetFileName(fileName)} 时发生错误: {ex.Message}");
                    }
                }
                RbExperimental.IsChecked = true;
                RefreshMainList();
            }
        }

        private void DgMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgMain.SelectedItem == null) return;
            try
            {
                // 优化处理：如果用户正在进行同屏多选叠加对比，更改选中行时不破坏勾选树状，仅更改辅助信息显示并同步重绘当前对比图
                var checkedSpectra = GetSelectedSpectra();
                if (checkedSpectra.Count > 0)
                {
                    RenderOverlayPlot(checkedSpectra, currentConfig);

                    if (DgMain.SelectedItem is RamanSpectrumModel rModel)
                    {
                        TxtFileInfo.Text = $"对比中 (高亮: {rModel.Name} | {rModel.AcquiredAt})";
                    }
                    else if (DgMain.SelectedItem is ReferenceSpectrumModel sModel)
                    {
                        TxtFileInfo.Text = $"对比中 (高亮: {sModel.SubstanceName} | {sModel.CreatedAt})";
                    }
                    return;
                }

                // --- 普通单选查看模式 ---
                if (DgMain.SelectedItem is RamanSpectrumModel experimental)
                {
                    // 核心修改：不经过 JSON，点击行时直接将二进制 byte[] 解析为绘图所需的 double[] 数组
                    double[] xs = new double[experimental.X_cm.Length / sizeof(double)];
                    Buffer.BlockCopy(experimental.X_cm, 0, xs, 0, experimental.X_cm.Length);

                    double[] ys = new double[experimental.Y_cm.Length / sizeof(double)];
                    Buffer.BlockCopy(experimental.Y_cm, 0, ys, 0, experimental.Y_cm.Length);

                    currentX = xs;
                    currentYRaw = ys;
                    currentSubstanceName = experimental.Name;

                    this.currentIdealChemistry = experimental.NameAuto;
                    this.currentMeasuredChemistry = experimental.Note;

                    ProcessAndPlot(currentX, currentYRaw);
                    TxtFileInfo.Text = $"查看: {experimental.Name} | {experimental.AcquiredAt}";
                }
                // === 支持直接点击标准参考表列表行读取谱图展示 ===
                else if (DgMain.SelectedItem is ReferenceSpectrumModel reference)
                {
                    if (!string.IsNullOrEmpty(reference.X_cm) && !string.IsNullOrEmpty(reference.Y_cm))
                    {
                        currentX = JsonConvert.DeserializeObject<double[]>(reference.X_cm);
                        currentYRaw = JsonConvert.DeserializeObject<double[]>(reference.Y_cm);
                        currentSubstanceName = reference.SubstanceName;

                        this.currentIdealChemistry = reference.ChemicalFormula;
                        this.currentMeasuredChemistry = reference.Note;

                        ProcessAndPlot(currentX, currentYRaw);
                        TxtFileInfo.Text = $"查看标准谱: {reference.SubstanceName} | {reference.CreatedAt}";
                    }
                    else
                    {
                        TxtFileInfo.Text = $"选中标准数据: {reference.SubstanceName} (全谱缺失)";
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("数据解析失败: " + ex.Message); }
        }

        // --- 保存到参考库时，带入当前绘制在图上的全谱曲线 X 和 Y 坐标与特征峰缓存 ---
        private void BtnSaveToLib_Click(object sender, RoutedEventArgs e)
        {
            double[] dataToSave = currentY ?? currentYRaw;
            if (currentX == null || dataToSave == null) { MessageBox.Show("请先执行预处理！"); return; }

            // 1. 数据开场验证，直接取当前画面上绘制好的特征峰缓存
            if (this.currentPeaks == null || this.currentPeaks.Count == 0)
            {
                MessageBox.Show("当前未提取到有效的特征峰。请先执行“预处理”或“智能处理”！", "提示");
                return;
            }

            // 2. 弹出窗口时传入当前画面特征峰，同时传入识别到的理想化学式与实测化学成分
            SaveWindow saveWin = new SaveWindow(
                currentSubstanceName,
                this.currentPeaks,
                currentX,
                dataToSave,
                this.currentIdealChemistry,       // 传入自动填充的化学式
                this.currentMeasuredChemistry     // 传入自动填充的实测备注
            )
            { Owner = this };

            if (saveWin.ShowDialog() == true)
            {
                dbService.SaveSubstance(saveWin.ResultModel, "ReferenceSpectrum");
                MessageBox.Show("保存成功，标准参考库已添加全谱参考数据！");
                if (RbReference.IsChecked == true) RefreshMainList();
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (DgMain.SelectedItem == null) return;
            dynamic selected = DgMain.SelectedItem;
            int id = selected.Id;
            string table = (RbExperimental.IsChecked == true) ? "RamanSpectrum" : "ReferenceSpectrum";
            if (MessageBox.Show("确定删除该记录吗？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                dbService.DeleteSubstance(id, table);
                RefreshMainList();
            }
        }

        private void BtnAutoTune_Click(object sender, RoutedEventArgs e)
        {
            if (currentYRaw == null)
            {
                MessageBox.Show("请先加载光谱数据！");
                return;
            }

            AlgorithmConfig tempConfig = currentConfig.Clone();
            PreprocessingService.AutoTuneParameters(currentYRaw, tempConfig);
            ApplyAlgorithmAndRefresh(tempConfig);

            TxtPreprocessStatus.Text = $"智能匹配预览 (SNR门槛: {tempConfig.MinSnr:F1}) - 未保存到算法设置";
        }

        // --- 双引擎比对算法入口 ---
        private void BtnMatch_Click(object sender, RoutedEventArgs e)
        {
            // 1. 基础数据校验
            if (currentY == null || currentX == null)
            {
                MessageBox.Show("请先执行“预处理”！", "提示");
                return;
            }

            // 直接获取当前画面上显示的特征峰信息 (无重复计算，无前后配置不一致问题)
            if (this.currentPeaks == null || this.currentPeaks.Count == 0)
            {
                MessageBox.Show("当前未提取到有效的特征峰。请先执行“预处理”或“智能处理”！", "提示");
                return;
            }

            // 2. 获取标准库
            var library = dbService.GetReferenceLibrary();
            if (library.Count == 0)
            {
                MessageBox.Show("标准参考库为空！", "提示");
                return;
            }

            // === 新增：从 UI 的 CmbSearchMode 下拉框中，动态提取检索模式选项 ===
            // 默认 SelectedIndex == 0 是 "混合物检索 (Mixture)"
            // SelectedIndex == 1 是 "纯相单组分 (Pure)" 模式
            bool enforcePure = CmbSearchMode != null && CmbSearchMode.SelectedIndex == 1;

            List<MatchResult> allResults = new List<MatchResult>();

            // 3. 单次循环遍历库，双引擎并行计算
            foreach (var item in library)
            {
                try
                {
                    double peakScore = 0;
                    int hitCount = 0;
                    int totalRefPeaks = 0;

                    // 引擎 A：特征峰匹配
                    if (!string.IsNullOrEmpty(item.PeaksJson))
                    {
                        var refPeaks = JsonConvert.DeserializeObject<List<dynamic>>(item.PeaksJson);
                        if (refPeaks != null && refPeaks.Count > 0)
                        {
                            totalRefPeaks = refPeaks.Count;

                            // === 修改：调用升级后的 CalculateSimilarity 方法，传递模式参数 enforcePure ===
                            (peakScore, hitCount) = PreprocessingService.CalculateSimilarity(
                                this.currentPeaks,
                                refPeaks,
                                tolerance: 8.0,
                                enforcePureComponent: enforcePure);
                        }
                    }

                    // 引擎 B：全谱重采样匹配 (HQI)
                    double hqiScore = 0;
                    if (!string.IsNullOrEmpty(item.X_cm) && !string.IsNullOrEmpty(item.Y_cm))
                    {
                        double[] refX = JsonConvert.DeserializeObject<double[]>(item.X_cm);
                        double[] refY = JsonConvert.DeserializeObject<double[]>(item.Y_cm);
                        if (refX != null && refY != null && refX.Length >= 10)
                        {
                            hqiScore = PreprocessingService.CalculateHQI(
                                currentXProcessed ?? currentX,
                                currentY ?? currentYRaw,
                                refX,
                                refY);
                        }
                    }

                    // === 新增：在“纯相筛选”模式下，若特征峰匹配完全没有命中任何峰，则对全谱 HQI 相似度也执行安全重置 ===
                    if (enforcePure && hitCount == 0)
                    {
                        hqiScore *= 0.15; // 限制宽包伪匹配的 HQI 误碰，将其大幅折减 85%
                    }

                    // 4. 过滤机制：只要有任意一种匹配得分大于 5.0%，即视为有效候选物质
                    if (peakScore > 5.0 || hqiScore > 5.0)
                    {
                        allResults.Add(new MatchResult
                        {
                            Name = item.SubstanceName,
                            Formula = item.ChemicalFormula,
                            PeakScore = peakScore,
                            HqiScore = hqiScore,
                            HitCount = hitCount,
                            TotalRefPeaks = totalRefPeaks,
                            PeaksJson = item.PeaksJson
                        });
                    }
                }
                catch
                {
                    continue; // 容错处理
                }
            }

            // 5. 排序与输出
            if (allResults.Count > 0)
            {
                // 默认按照两者的“综合得分 (CombinedScore)”从高到低排序
                var sortedResults = allResults.OrderByDescending(r => r.CombinedScore).ToList();

                // 弹出公用结果展示窗口
                MatchResultsWindow resultsWin = new MatchResultsWindow(sortedResults) { Owner = this };

                // 联动主图：选中某行时自动在 ScottPlot 上叠加画出标准峰参考线
                resultsWin.OnResultSelected += (selected) => {
                    DrawReferenceOverlay(selected);
                };

                resultsWin.ShowDialog();
            }
            else
            {
                MessageBox.Show("未匹配到任何相似的物质，请检查标准库数据或调整预处理参数。", "提示");
            }
        }

        // 双击列表行响应事件
        private void DgMain_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DgMain.SelectedItem == null) return;

            if (RbReference.IsChecked == true)
            {
                // === 1. 标准库双击编辑逻辑 ===
                if (DgMain.SelectedItem is ReferenceSpectrumModel selectedRef)
                {
                    SaveWindow editWin = new SaveWindow(selectedRef) { Owner = this };

                    if (editWin.ShowDialog() == true)
                    {
                        try
                        {
                            dbService.UpdateReference(editWin.ResultModel);
                            MessageBox.Show("更新成功！");
                            RefreshMainList();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("更新失败: " + ex.Message);
                        }
                    }
                }
            }
            else if (RbExperimental.IsChecked == true)
            {
                // === 2. 新增：实测库双击详情与编辑逻辑 ===
                if (DgMain.SelectedItem is RamanSpectrumModel selectedRaman)
                {
                    SpectrumDetailWindow detailWin = new SpectrumDetailWindow(selectedRaman) { Owner = this };

                    if (detailWin.ShowDialog() == true)
                    {
                        try
                        {
                            dbService.UpdateRamanSpectrum(detailWin.Model);
                            MessageBox.Show("实测光谱元数据更新成功！");
                            RefreshMainList();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("实测光谱更新失败: " + ex.Message);
                        }
                    }
                }
            }
        }

        private void DrawReferenceOverlay(MatchResult result)
        {
            if (currentX == null) return;

            try
            {
                var refPeaks = JsonConvert.DeserializeObject<List<dynamic>>(result.PeaksJson);

                ApplyAlgorithmAndRefresh(); // 重新画预处理后的图

                foreach (var p in refPeaks)
                {
                    double w = (double)p.W;
                    var vline = MainPlot.Plot.AddVerticalLine(w);
                    vline.Color = System.Drawing.Color.FromArgb(180, System.Drawing.Color.Blue);
                    vline.LineStyle = ScottPlot.LineStyle.Dash;
                    vline.LineWidth = 2;
                }

                MainPlot.Plot.Title($"比对中：{currentSubstanceName} VS {result.Name} (相似度: {result.Score:F1}%)");
                MainPlot.Refresh();
            }
            catch (Exception ex) { MessageBox.Show("叠加显示失败: " + ex.Message); }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow setWin = new SettingsWindow(currentConfig) { Owner = this };
            if (setWin.ShowDialog() == true) currentConfig = setWin.Config;
        }

        private void BtnDoPreprocess_Click(object sender, RoutedEventArgs e)
        {
            ApplyAlgorithmAndRefresh();
        }

        // === 新增：点击执行机器学习算法预测物相类别 ===
        // MainWindow.xaml.cs -> BtnMlClassify_Click 升级版

        private void BtnMlClassify_Click(object sender, RoutedEventArgs e)
        {
            // 1. 获取当前实测数据库数据
            var testSet = dbService.GetRamanLibrary();

            // ==========================================================
            // 💡 算法优化分支：按住键盘 Ctrl 键点击此按钮，启动实测数据集“留一法交叉验证 (LOOCV)”自跑分自评估！
            // ==========================================================
            if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftCtrl) ||
                System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightCtrl))
            {
                if (testSet == null || testSet.Count < 3)
                {
                    MessageBox.Show("请确保您的“实测光谱表”中含有足够数量（>= 3条）的数据以启动内部交叉验证！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"[实测数据集留一法交叉验证 (LOOCV) 自跑分启动]\n\n" +
                    $"系统将完全脱离外部人工标准参考库，直接利用当前实测库内共 {testSet.Count} 条数据进行闭环迭代验证：\n\n" +
                    $"每次抽取 1 个样本作为未知测试谱，使用其余 {testSet.Count - 1} 个样本进行主成分投影（PCA）与自适应特征向量提取，循环进行 {testSet.Count} 轮测试。\n\n" +
                    $"【算法物理配置】：计算时将自动在内存中应用当前的去噪、平滑、SNIP基线扣除与200 cm⁻¹裁剪设置。\n\n" +
                    $"是否开始执行高精度的 LOOCV 自跑分并导出实验混淆矩阵 CSV？",
                    "算法内部交叉自验证跑分",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    this.Cursor = System.Windows.Input.Cursors.Wait;
                    string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "evaluation_results.csv");

                    // 调用已剔除标准库参数的纯净版自验证接口
                    string reportText = KnnClassifier.RunIndependentValidation(testSet, csvPath, this.currentConfig);

                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                    MessageBox.Show(reportText, "交叉自验证跑分完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            // ==========================================================

            // --- 正常的单张拉曼光谱 KNN 预测流程 (保持不变) ---
            var library = dbService.GetReferenceLibrary();
            if (library.Count == 0)
            {
                MessageBox.Show("标准参考库为空，单点预测无法进行，请先导入标准数据！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double[] targetX = currentXProcessed ?? currentX;
            double[] targetY = currentY ?? currentYRaw;

            if (targetX == null || targetY == null)
            {
                MessageBox.Show("请先在左侧选择并加载一条拉曼实测光谱数据！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                int kNeighbours = 3;
                var knnResult = KnnClassifier.Predict(targetX, targetY, library, kNeighbours);

                MessageBox.Show(
                    $"【机器学习分类预测结果】\n\n" +
                    $"🔮 KNN 分类决策 (K={kNeighbours})：\n" +
                    $"预测物相大类: {knnResult.PredictedClass}\n" +
                    $"投票置信度: {knnResult.Confidence * 100:F0}%\n\n" +
                    $"说明：该决策是在 {library.Count} 维特征空间中通过多维夹角余弦距离投票计算得出。",
                    "AI 物相识别成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"分类器预测异常: {ex.Message}", "系统错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewSwitch_Click(object sender, RoutedEventArgs e) => RefreshMainList();

        private void MainPlot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (FreeCrosshair == null) return;
            (double mouseX, double mouseY) = MainPlot.GetMouseCoordinates();
            FreeCrosshair.X = mouseX; FreeCrosshair.Y = mouseY;
            FreeCrosshair.IsVisible = true;
            this.Title = $"拉曼分析 - 位移: {mouseX:F1}, 强度: {mouseY:F3}";
            MainPlot.Refresh();
        }

        private void MainPlot_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // === 修改：使用当前加工后对齐的 X 轴（currentXProcessed ?? currentX）进行最近点检索，避免越界 ===
            double[] activeX = currentXProcessed ?? currentX;
            if (activeX == null || SnapCrosshair == null) return;

            (double mouseX, _) = MainPlot.GetMouseCoordinates();
            int closestIndex = 0; double minDistance = double.MaxValue;
            for (int i = 0; i < activeX.Length; i++)
            {
                double dist = Math.Abs(activeX[i] - mouseX);
                if (dist < minDistance) { minDistance = dist; closestIndex = i; }
            }

            SnapCrosshair.X = activeX[closestIndex];
            SnapCrosshair.Y = (currentY != null) ? currentY[closestIndex] : currentYRaw[closestIndex];
            SnapCrosshair.IsVisible = true;
            MainPlot.Refresh();
        }

        // === 📤 导出整个标准库为 JSON 文件 ===
        private void BtnExportLib_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var data = dbService.GetReferenceLibrary();
                if (data.Count == 0)
                {
                    MessageBox.Show("当前标准库中没有任何数据可供导出备份。", "提示");
                    return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "标准库备份文件 (*.json)|*.json",
                    FileName = $"Raman_Standard_Library_Backup_{DateTime.Now:yyyyMMdd}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // 序列化为美化排版后的 JSON 字符串
                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    File.WriteAllText(saveFileDialog.FileName, json);

                    MessageBox.Show($"标准库导出备份成功！\n共备份 {data.Count} 条标准光谱数据。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出标准库失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // === 📥 从 JSON 备份文件导入恢复标准库 ===
        private void BtnImportLib_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "标准库备份文件 (*.json)|*.json"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var data = JsonConvert.DeserializeObject<List<ReferenceSpectrumModel>>(json);

                    if (data == null || data.Count == 0)
                    {
                        MessageBox.Show("导入的文件中未检测到合法的拉曼标准库数据。", "提示");
                        return;
                    }

                    var result = MessageBox.Show(
                        $"确认要导入这 {data.Count} 条标准库数据吗？\n警告：这将会以追加的形式写入您当前的数据库中。",
                        "确认导入",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        dbService.ImportReferenceSpectra(data);
                        MessageBox.Show("标准库数据导入还原成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                        // 如果当前停留在标准库选项卡，则立刻重刷列表进行显示
                        if (RbReference.IsChecked == true)
                        {
                            RefreshMainList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("导入标准库失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // === 新增：一键将当前屏幕展示的预处理光谱线导出为 CSV 文件 ===
        private void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            // 1. 获取当前 ScottPlot 图表上正在展示的物理 X 轴与 Y 轴数据
            // 逻辑：如果已经过预处理，则导出裁剪/平整后的 active 轴；否则降级导出导入时的原始轴
            double[] targetX = currentXProcessed ?? currentX;
            double[] targetY = currentY ?? currentYRaw;

            if (targetX == null || targetY == null || targetX.Length == 0)
            {
                MessageBox.Show("当前未加载或计算任何拉曼光谱数据，无法执行导出！", "导出提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. 唤起保存位置选择框
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Title = "导出处理后的拉曼光谱数据（标准双列格式）",
                Filter = "CSV 数据表格 (Excel/Origin兼容) (*.csv)|*.csv|文本数据文件 (*.txt)|*.txt",
                // 自动推荐文件名：[矿物名称]_提取数据_[当前时间戳].csv
                FileName = $"{currentSubstanceName}_Spectral_Data_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // 切换鼠标状态为忙碌，提供极佳的系统交互回馈
                    this.Cursor = System.Windows.Input.Cursors.Wait;

                    List<string> csvLines = new List<string>();

                    // 3. 写入标准的物理数据首行首标
                    csvLines.Add("Wavenumber(cm-1),Intensity");

                    // 4. 循环遍历并格式化写入数据点
                    // 学术修约规范：拉曼位移（波数）保留 1 位小数；归一化强度作为相对值保留 5 位小数，防止精度丢失
                    for (int i = 0; i < targetX.Length; i++)
                    {
                        string line = $"{targetX[i]:F1},{targetY[i]:F5}";
                        csvLines.Add(line);
                    }

                    // 5. 写入本地文件（使用通用 UTF-8 编码，防止中文文件名或中文字符集乱码）
                    File.WriteAllLines(saveFileDialog.FileName, csvLines, System.Text.Encoding.UTF8);

                    // 恢复鼠标状态
                    this.Cursor = System.Windows.Input.Cursors.Arrow;

                    // 弹出导出成功信息
                    MessageBox.Show(
                        $"🎉 拉曼光谱数据导出成功！\n\n" +
                        $"💾 保存位置: {saveFileDialog.FileName}\n" +
                        $"🔢 数据点总数: {targetX.Length} 个\n\n" +
                        $"学术制图提示：本文件采用逗号分隔符（CSV），可直接使用 Origin 导入（Import Single ASCII）或拖入 Excel 中进行学术出版级的高清绘图。",
                        "导出成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                    MessageBox.Show($"导出失败，发生文件读写错误: {ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private (double[] croppedX, double[] croppedY) ApplyLowFrequencyCutoff(double[] xs, double[] ys, double cutoffWavenumber)
        {
            if (xs == null || ys == null || xs.Length != ys.Length) return (xs, ys);

            // 筛选出大于或等于指定截断波数的数据索引
            var validIndices = Enumerable.Range(0, xs.Length)
                                         .Where(i => xs[i] >= cutoffWavenumber)
                                         .ToArray();

            if (validIndices.Length == 0) return (xs, ys); // 容错：防止全部被裁剪

            double[] newX = validIndices.Select(i => xs[i]).ToArray();
            double[] newY = validIndices.Select(i => ys[i]).ToArray();
            return (newX, newY);
        }
    }
}