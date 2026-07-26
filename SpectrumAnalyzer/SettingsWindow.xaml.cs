using Newtonsoft.Json;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SpectrumAnalyzer
{
    public partial class SettingsWindow : Window
    {
        public AlgorithmConfig Config { get; set; }

        public SettingsWindow(AlgorithmConfig currentConfig)
        {
            InitializeComponent();
            Config = currentConfig; // 直接引用外部配置
            LoadConfigToUI();
        }

        private void LoadConfigToUI()
        {
            if (Config == null) return;

            ChkEnableDespike.IsChecked = Config.EnableDespike;
            TxtDespikeZ.Text = Config.DespikeZ.ToString();
            TxtDespikeWindow.Text = Config.DespikeWindow.ToString();
            TxtSgWindow.Text = Config.SgWindowSize.ToString();
            TxtSgOrder.Text = Config.SgOrder.ToString();

            ChkEnableBaseline.IsChecked = Config.EnableBaseline;
            TxtBaseWindow.Text = Config.BaselineWindow.ToString();
            TxtBaseQuantile.Text = Config.BaselineQuantile.ToString();
            TxtBaseSmooth.Text = Config.BaselineSmoothWindow.ToString();

            // === 新增：加载基线算法选择与 SNIP 参数 ===
            CmbBaselineMethod.SelectedIndex = Config.BaselineMethod == "Snip" ? 1 : 0;
            TxtSnipIterations.Text = Config.SnipIterations.ToString();
            ChkCropBelow200.IsChecked = Config.CropBelow200;

            // 强制执行一次能见度更新
            UpdateBaselineUIVisibility();

            TxtMinSnr.Text = Config.MinSnr.ToString();
            TxtMinDist.Text = Config.MinPeakDistanceX.ToString();
            TxtMinHeightFrac.Text = Config.MinHeightFrac.ToString();
            ChkNormalize.IsChecked = Config.EnableNormalization;
            ChkSubPixel.IsChecked = Config.EnableSubPixel;
        }

        // 2. 新增：下拉切换事件，控制输入框的隐藏和显示
        private void CmbBaselineMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 关键防护：WPF窗口初始化时事件会提前触发，此时部分控件可能为 null，必须做防空保护
            if (CmbBaselineMethod == null || TxtSnipIterations == null) return;
            UpdateBaselineUIVisibility();
        }

        // 3. 新增：根据下拉选择，动态控制 UI 控件能见度
        private void UpdateBaselineUIVisibility()
        {
            bool isSnip = CmbBaselineMethod.SelectedIndex == 1;

            // 滚动分位数参数可见性
            var quantileVis = isSnip ? Visibility.Collapsed : Visibility.Visible;
            LblBaseWindow.Visibility = quantileVis;
            TxtBaseWindow.Visibility = quantileVis;
            LblBaseQuantile.Visibility = quantileVis;
            TxtBaseSmooth.Visibility = quantileVis;
            LblBaseSmooth.Visibility = quantileVis;

            // SNIP 参数可见性
            var snipVis = isSnip ? Visibility.Visible : Visibility.Collapsed;
            LblSnipIter.Visibility = snipVis;
            TxtSnipIterations.Visibility = snipVis;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            // === 核心机制：逐项严谨校验数据有效性，给出明确具体的报错反馈 ===
            if (!ValidateInputs(out string errorMsg, out Control errControl))
            {
                MessageBox.Show(errorMsg, "参数格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                errControl?.Focus();
                return;
            }

            try
            {
                // 数据应用
                Config.EnableDespike = ChkEnableDespike.IsChecked ?? false;
                Config.DespikeZ = double.Parse(TxtDespikeZ.Text);
                Config.DespikeWindow = int.Parse(TxtDespikeWindow.Text);
                Config.SgWindowSize = int.Parse(TxtSgWindow.Text);
                Config.SgOrder = int.Parse(TxtSgOrder.Text);

                Config.EnableBaseline = ChkEnableBaseline.IsChecked ?? false;
                Config.BaselineWindow = int.Parse(TxtBaseWindow.Text);
                Config.BaselineQuantile = double.Parse(TxtBaseQuantile.Text);
                Config.BaselineSmoothWindow = int.Parse(TxtBaseSmooth.Text);

                // === 新增：保存用户选择的基线校正算法及其具体参数 ===
                Config.BaselineMethod = CmbBaselineMethod.SelectedIndex == 1 ? "Snip" : "Quantile";
                Config.SnipIterations = int.Parse(TxtSnipIterations.Text);
                Config.CropBelow200 = ChkCropBelow200.IsChecked ?? false;

                Config.EnableBaseline = ChkEnableBaseline.IsChecked ?? false;
                Config.BaselineWindow = int.Parse(TxtBaseWindow.Text);
                Config.BaselineQuantile = double.Parse(TxtBaseQuantile.Text);
                Config.BaselineSmoothWindow = int.Parse(TxtBaseSmooth.Text);

                Config.MinSnr = double.Parse(TxtMinSnr.Text);
                Config.MinPeakDistanceX = double.Parse(TxtMinDist.Text);
                Config.MinHeightFrac = double.Parse(TxtMinHeightFrac.Text);
                Config.EnableNormalization = ChkNormalize.IsChecked ?? true;
                Config.EnableSubPixel = ChkSubPixel.IsChecked ?? true;

                // === 新增：参数持久化存储至本地 config.json ===
                SaveConfigToFile();

                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"参数应用失败: {ex.Message}", "系统错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // === 新增：一键恢复初始出厂设置 ===
        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要将算法参数还原为默认配置吗？", "还原确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                // 创建一个空配置实例（将自动使用内部定义好的默认物理属性值）
                Config = new AlgorithmConfig();
                LoadConfigToUI();
            }
        }

        // === 新增：物理有效性前置校验逻辑 ===
        private bool ValidateInputs(out string errorMsg, out Control errControl)
        {
            errorMsg = string.Empty;
            errControl = null;

            // 1. 去毛刺参数
            if (!double.TryParse(TxtDespikeZ.Text, out double z) || z <= 0)
            {
                errorMsg = "去毛刺的 Z-Score 阈值必须为正实数（推荐 6.0 ~ 12.0）。";
                errControl = TxtDespikeZ;
                return false;
            }
            if (!int.TryParse(TxtDespikeWindow.Text, out int dWin) || dWin < 1)
            {
                errorMsg = "去毛刺检测窗口大小必须为不小于 1 的整数（推荐 2 ~ 5）。";
                errControl = TxtDespikeWindow;
                return false;
            }

            // 2. SG平滑参数校验
            if (!int.TryParse(TxtSgWindow.Text, out int sgWin) || sgWin < 3 || sgWin % 2 == 0)
            {
                errorMsg = "SG 平滑窗口大小必须为大于等于 3 的【正奇数】。";
                errControl = TxtSgWindow;
                return false;
            }
            if (!int.TryParse(TxtSgOrder.Text, out int sgOrd) || sgOrd < 1)
            {
                errorMsg = "SG 平滑多项式阶数必须为正整数（推荐 2 或 3）。";
                errControl = TxtSgOrder;
                return false;
            }
            if (sgOrd >= sgWin)
            {
                errorMsg = $"SG 阶数 ({sgOrd}) 必须小于平滑窗口尺寸 ({sgWin})，否则无法进行拟合。";
                errControl = TxtSgOrder;
                return false;
            }

            // === 修改：根据选定的算法执行对应的参数校验 ===
            if (CmbBaselineMethod.SelectedIndex == 1) // 激活了 SNIP
            {
                if (!int.TryParse(TxtSnipIterations.Text, out int snipIt) || snipIt < 1 || snipIt > 200)
                {
                    errorMsg = "SNIP 迭代次数必须是 1 到 200 之间的正整数（推荐 15 ~ 40）。";
                    errControl = TxtSnipIterations;
                    return false;
                }
            }
            else // 激活了 滚动分位数
            {
                if (!int.TryParse(TxtBaseWindow.Text, out int bWin) || bWin < 3)
                {
                    errorMsg = "滚动基线窗口必须为不小于 3 的正整数（建议设在 151 ~ 301 之间）。";
                    errControl = TxtBaseWindow;
                    return false;
                }
                if (!double.TryParse(TxtBaseQuantile.Text, out double bQuant) || bQuant < 0 || bQuant > 1)
                {
                    errorMsg = "估计分位数必须为 0 到 1.0 之间的浮点数（推荐 0.01 ~ 0.05）。";
                    errControl = TxtBaseQuantile;
                    return false;
                }
                if (!int.TryParse(TxtBaseSmooth.Text, out int bSmWin) || bSmWin < 1 || bSmWin % 2 == 0)
                {
                    errorMsg = "基线二次平滑窗口必须为正奇数（推荐 51 ~ 101）。";
                    errControl = TxtBaseSmooth;
                    return false;
                }
            }

            // 4. 寻峰参数校验
            if (!double.TryParse(TxtMinSnr.Text, out double snr) || snr <= 0)
            {
                errorMsg = "寻峰最小信噪比门槛（SNR）必须为正数（推荐 1.8 ~ 5.0）。";
                errControl = TxtMinSnr;
                return false;
            }
            if (!double.TryParse(TxtMinDist.Text, out double dist) || dist <= 0)
            {
                errorMsg = "特征峰之间的最小物理距离（波数）必须为正数。";
                errControl = TxtMinDist;
                return false;
            }
            if (!double.TryParse(TxtMinHeightFrac.Text, out double hFrac) || hFrac < 0 || hFrac > 1.0)
            {
                errorMsg = "P95 比例阈值必须是 0.0 到 1.0 之间的数值。";
                errControl = TxtMinHeightFrac;
                return false;
            }

            return true;
        }

        // === 新增：自动将全局配置存盘为 config.json ===
        private void SaveConfigToFile()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch
            {
                // 静默失败，保证即便本地磁盘由于权限无法写入，也不会导致用户无法在内存中应用参数
            }
        }
    }
}