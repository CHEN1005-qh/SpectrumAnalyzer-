using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SpectrumAnalyzer.Services;

namespace SpectrumAnalyzer.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel
    /// 负责：业务逻辑协调、数据绑定、命令路由
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly SpectrumProcessingService _processingService;
        private readonly SpectrumMatchingService _matchingService;
        private readonly DatabaseService _databaseService;

        // 匹配命令
        private RelayCommand _matchCommand;

        // 数据属性
        private string _currentSubstanceName = "未知物质";
        private string _preprocessStatus = "就绪，等待输入";
        private AlgorithmConfig _currentConfig;

        // 分类结果属性
        private string _classificationResult = "";
        private double _classificationConfidence = 0.0;

        // 光谱数据
        private ObservableCollection<RamanSpectrumModel> _ramanSpectraList;
        private ObservableCollection<ReferenceSpectrumModel> _referenceSpectraList;

        // 当前选中的光谱
        private RamanSpectrumModel _selectedRamanSpectrum;
        private ReferenceSpectrumModel _selectedReferenceSpectrum;

        // 视图模式标志
        private bool _isRamanMode = true;

        // 命令
        private RelayCommand _loadRamanDataCommand;
        private RelayCommand _preprocessCommand;
        private RelayCommand _classifyCommand;
        private RelayCommand _resetCommand;
        private RelayCommand _deleteCommand;
        private RelayCommand _saveCommand;

        #region 公开属性

        /// <summary>
        /// 当前物质名称
        /// </summary>
        public string CurrentSubstanceName
        {
            get => _currentSubstanceName;
            set => SetProperty(ref _currentSubstanceName, value);
        }



        /// <summary>
        /// 预处理状态文本
        /// </summary>
        public string PreprocessStatus
        {
            get => _preprocessStatus;
            set => SetProperty(ref _preprocessStatus, value);
        }

        /// <summary>
        /// 当前算法配置
        /// </summary>
        public AlgorithmConfig CurrentConfig
        {
            get => _currentConfig;
            set => SetProperty(ref _currentConfig, value);
        }

        /// <summary>
        /// 当前原始 X 轴数据（用于UI绘图）
        /// </summary>
        public double[] CurrentX => _processingService?.CurrentX;

        /// <summary>
        /// 当前原始 Y 轴数据（用于UI绘图）
        /// </summary>
        public double[] CurrentYRaw => _processingService?.CurrentYRaw;

        /// <summary>
        /// 当前处理后的 Y 轴数据（用于UI绘图）
        /// </summary>
        public double[] CurrentYProcessed => _processingService?.CurrentYProcessed;

        /// <summary>
        /// 当前处理后的 X 轴数据（用于UI绘图）
        /// </summary>
        public double[] CurrentXProcessed => _processingService?.CurrentXProcessed;

        /// <summary>
        /// 当前检测到的特征峰（用于UI标记）
        /// </summary>
        public List<PeakInfo> CurrentPeaks => _processingService?.CurrentPeaks;

        /// <summary>
        /// 实测光谱列表
        /// </summary>
        public ObservableCollection<RamanSpectrumModel> RamanSpectraList
        {
            get => _ramanSpectraList;
            set => SetProperty(ref _ramanSpectraList, value);
        }

        /// <summary>
        /// 标准参考光谱列表
        /// </summary>
        public ObservableCollection<ReferenceSpectrumModel> ReferenceSpectraList
        {
            get => _referenceSpectraList;
            set => SetProperty(ref _referenceSpectraList, value);
        }

        /// <summary>
        /// 当前选中的实测光谱
        /// </summary>
        public RamanSpectrumModel SelectedRamanSpectrum
        {
            get => _selectedRamanSpectrum;
            set => SetProperty(ref _selectedRamanSpectrum, value);
        }

        /// <summary>
        /// 当前选中的参考光谱
        /// </summary>
        public ReferenceSpectrumModel SelectedReferenceSpectrum
        {
            get => _selectedReferenceSpectrum;
            set => SetProperty(ref _selectedReferenceSpectrum, value);
        }

        /// <summary>
        /// 是否为实测光谱模式
        /// </summary>
        public bool IsRamanMode
        {
            get => _isRamanMode;
            set => SetProperty(ref _isRamanMode, value);
        }

        #endregion

        /// <summary>
        /// 从文件导入光谱并加载到处理服务（支持两列：X,Y 或 单列 Y）
        /// 返回是否导入成功
        /// </summary>
        public bool ImportSpectrumFromFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return false;
                var lines = System.IO.File.ReadAllLines(filePath);
                var xList = new List<double>();
                var yList = new List<double>();

                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#")) continue;
                    // 忽略常见表头
                    if (line.ToLower().Contains("wavenumber") || line.ToLower().Contains("intensity") || line.ToLower().Contains("x") || line.ToLower().Contains("y"))
                    {
                        // 判断是否是标题行（含字母）
                        if (line.Any(c => char.IsLetter(c))) continue;
                    }

                    string[] parts = line.Split(new char[] { ',', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1)
                    {
                        if (double.TryParse(parts[0], out double yv))
                        {
                            yList.Add(yv);
                        }
                    }
                    else if (parts.Length >= 2)
                    {
                        if (double.TryParse(parts[0], out double xv) && double.TryParse(parts[1], out double yv))
                        {
                            xList.Add(xv);
                            yList.Add(yv);
                        }
                    }
                }

                if (yList.Count == 0) return false;
                double[] xArr;
                double[] yArr = yList.ToArray();
                if (xList.Count == yList.Count && xList.Count > 0)
                {
                    xArr = xList.ToArray();
                }
                else
                {
                    // 只有 Y 的情况：使用索引作为 X
                    xArr = new double[yArr.Length];
                    for (int i = 0; i < yArr.Length; i++) xArr[i] = i;
                }

                // 加载到处理服务
                _processingService.LoadRawData(xArr, yArr);
                CurrentSubstanceName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                PreprocessStatus = $"已导入文件: {System.IO.Path.GetFileName(filePath)}，点数 = {yArr.Length}";
                return true;
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"导入失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
        }

        /// <summary>
        /// 从已解析数组加载光谱（用于 CsvImportWindow 返回的数组）
        /// </summary>
        public void LoadSpectrumFromArrays(double[] xArr, double[] yArr, string sourceName = null)
        {
            try
            {
                if (yArr == null || yArr.Length == 0)
                {
                    PreprocessStatus = "导入失败：空数据";
                    return;
                }

                if (xArr == null || xArr.Length != yArr.Length)
                {
                    // 若 X 轴缺失或长度不匹配，使用索引作为 X
                    xArr = new double[yArr.Length];
                    for (int i = 0; i < yArr.Length; i++) xArr[i] = i;
                }

                _processingService.LoadRawData(xArr, yArr);
                CurrentSubstanceName = string.IsNullOrEmpty(sourceName) ? "导入光谱" : sourceName;
                PreprocessStatus = $"已导入: {CurrentSubstanceName}，点数 = {yArr.Length}";
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"导入失败: {ex.Message}";
            }
        }

        #region 命令属性

        /// <summary>
        /// 加载实测光谱数据命令
        /// </summary>
        public ICommand LoadRamanDataCommand
        {
            get
            {
                if (_loadRamanDataCommand == null)
                    _loadRamanDataCommand = new RelayCommand(LoadRamanData);
                return _loadRamanDataCommand;
            }
        }

        /// <summary>
        /// 执行预处理命令
        /// </summary>
        public ICommand PreprocessCommand
        {
            get
            {
                if (_preprocessCommand == null)
                    _preprocessCommand = new RelayCommand(ExecutePreprocess);
                return _preprocessCommand;
            }
        }

        /// <summary>
        /// 执行分类命令
        /// </summary>
        public ICommand ClassifyCommand
        {
            get
            {
                if (_classifyCommand == null)
                    _classifyCommand = new RelayCommand(ExecuteClassify);
                return _classifyCommand;
            }
        }

        /// <summary>
        /// 重置处理状态命令
        /// </summary>
        public ICommand ResetCommand
        {
            get
            {
                if (_resetCommand == null)
                    _resetCommand = new RelayCommand(ResetState);
                return _resetCommand;
            }
        }

        /// <summary>
        /// 删除光谱命令
        /// </summary>
        public ICommand DeleteCommand
        {
            get
            {
                if (_deleteCommand == null)
                    _deleteCommand = new RelayCommand(DeleteSelectedSpectrum, CanDeleteSpectrum);
                return _deleteCommand;
            }
        }

        /// <summary>
        /// 保存光谱命令
        /// </summary>
        public ICommand SaveCommand
        {
            get
            {
                if (_saveCommand == null)
                    _saveCommand = new RelayCommand(SaveSelectedSpectrum, CanSaveSpectrum);
                return _saveCommand;
            }
        }

        #endregion

        #region 构造函数

        /// <summary>
        /// 主 ViewModel 构造函数
        /// </summary>
        public MainViewModel()
        {
            // 初始化服务
            _databaseService = new DatabaseService();
            _processingService = new SpectrumProcessingService(_databaseService);
            // 初始化匹配服务（基于已有数据库）
            _matchingService = new SpectrumMatchingService(_databaseService);

            // 初始化配置
            _currentConfig = new AlgorithmConfig();
            LoadConfigFromLocal();

            // 初始化集合
            _ramanSpectraList = new ObservableCollection<RamanSpectrumModel>();
            _referenceSpectraList = new ObservableCollection<ReferenceSpectrumModel>();

            // 加载数据
            RefreshDataList();
        }

        /// <summary>
        /// 匹配当前已处理光谱并返回可用于 UI 展示的 MatchResult 列表
        /// </summary>
        public System.Collections.Generic.List<MatchResult> MatchCurrentSpectrum()
        {
            try
            {
                var spectrum = CurrentYProcessed;
                if (spectrum == null || spectrum.Length == 0)
                {
                    PreprocessStatus = "错误：当前无已处理光谱可用于比对";
                    return new System.Collections.Generic.List<MatchResult>();
                }

                // 逐条参考谱进行双轨比对：特征峰相似度 + 全谱 HQI
                var refLib = _database_service_safe();
                var results = new System.Collections.Generic.List<MatchResult>();
                var samplePeaks = CurrentPeaks ?? new System.Collections.Generic.List<PeakInfo>();
                var sampleX = CurrentXProcessed;
                var sampleY = CurrentYProcessed;

                foreach (var refEntry in refLib)
                {
                    try
                    {
                        var refPeaks = ParseReferencePeaks(refEntry?.PeaksJson);
                        var sim = CalculateSimilarity(samplePeaks, refPeaks, 8.0);

                        var refX = TryReadSpectrumArray(refEntry?.X_cm);
                        var refY = TryReadSpectrumArray(refEntry?.Y_cm);
                        var hqi = CalculateHQI(sampleX, sampleY, refX, refY);

                        results.Add(new MatchResult
                        {
                            Name = refEntry?.SubstanceName ?? string.Empty,
                            Formula = refEntry?.ChemicalFormula ?? string.Empty,
                            PeakScore = sim.Score,
                            HqiScore = hqi,
                            HitCount = sim.HitCount,
                            TotalRefPeaks = refPeaks.Count,
                            PeaksJson = refEntry?.PeaksJson
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"比对参考谱失败: {ex.Message}");
                    }
                }

                var ordered = results
                    .OrderByDescending(r => (r.PeakScore * 0.6) + (r.HqiScore * 0.4))
                    .ToList();

                PreprocessStatus = $"比对完成: 比较了 {ordered.Count} 条标准库记录";
                return ordered;
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"比对异常: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
                return new System.Collections.Generic.List<MatchResult>();
            }
        }

        // Helper: safe get reference library (防止多次打开 DB 时异常)
        private System.Collections.Generic.List<ReferenceSpectrumModel> _database_service_safe()
        {
            try
            {
                return _databaseService.GetReferenceLibrary();
            }
            catch
            {
                return new System.Collections.Generic.List<ReferenceSpectrumModel>();
            }
        }

        private class RefPeak
        {
            public double W { get; set; }
            public double I { get; set; }
        }

        private System.Collections.Generic.List<RefPeak> ParseReferencePeaks(string peaksJson)
        {
            var peaks = new System.Collections.Generic.List<RefPeak>();
            if (string.IsNullOrWhiteSpace(peaksJson))
                return peaks;

            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(peaksJson);
                if (token.Type != Newtonsoft.Json.Linq.JTokenType.Array)
                    return peaks;

                foreach (var item in token)
                {
                    if (item.Type == Newtonsoft.Json.Linq.JTokenType.Integer || item.Type == Newtonsoft.Json.Linq.JTokenType.Float)
                    {
                        peaks.Add(new RefPeak
                        {
                            W = item.ToObject<double>(),
                            I = 1.0
                        });
                    }
                    else if (item.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                    {
                        var wToken = item["W"] ?? item["w"];
                        var iToken = item["I"] ?? item["i"];
                        if (wToken == null)
                            continue;

                        peaks.Add(new RefPeak
                        {
                            W = wToken.ToObject<double>(),
                            I = iToken != null ? iToken.ToObject<double>() : 1.0
                        });
                    }
                }
            }
            catch
            {
            }

            return peaks;
        }

        private (double Score, int HitCount) CalculateSimilarity(System.Collections.Generic.List<PeakInfo> samplePeaks, System.Collections.Generic.List<RefPeak> refPeaks, double toleranceCm1)
        {
            if (samplePeaks == null || refPeaks == null || samplePeaks.Count == 0 || refPeaks.Count == 0)
                return (0.0, 0);

            var sample = new System.Collections.Generic.List<(double W, double I)>();
            foreach (var p in samplePeaks)
            {
                sample.Add((GetPeakWavenumber(p), GetPeakIntensity(p)));
            }

            double totalWeight = refPeaks.Sum(p => Math.Max(Math.Abs(p.I), 1.0));
            if (totalWeight <= 0)
                return (0.0, 0);

            int hitCount = 0;
            double weightedHit = 0.0;
            var used = new bool[sample.Count];

            foreach (var rp in refPeaks)
            {
                var bestIdx = -1;
                var bestDelta = double.MaxValue;

                for (int i = 0; i < sample.Count; i++)
                {
                    if (used[i])
                        continue;

                    var delta = Math.Abs(sample[i].W - rp.W);
                    if (delta <= toleranceCm1 && delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestIdx = i;
                    }
                }

                if (bestIdx >= 0)
                {
                    used[bestIdx] = true;
                    hitCount++;
                    var closeness = Math.Max(0.0, 1.0 - (bestDelta / toleranceCm1));
                    weightedHit += Math.Max(Math.Abs(rp.I), 1.0) * closeness;
                }
            }

            var score = (weightedHit / totalWeight) * 100.0;
            return (Math.Round(score, 1), hitCount);
        }

        private double CalculateHQI(double[] sampleX, double[] sampleY, double[] refX, double[] refY)
        {
            if (sampleX == null || sampleY == null || refX == null || refY == null)
                return 0.0;
            if (sampleX.Length < 3 || sampleY.Length < 3 || refX.Length < 3 || refY.Length < 3)
                return 0.0;

            int n = Math.Min(sampleX.Length, sampleY.Length);
            if (n < 3)
                return 0.0;

            double dot = 0.0;
            double normA = 0.0;
            double normB = 0.0;

            for (int i = 0; i < n; i++)
            {
                var a = sampleY[i];
                var b = Interpolate(refX, refY, sampleX[i]);
                dot += a * b;
                normA += a * a;
                normB += b * b;
            }

            if (normA <= 0 || normB <= 0)
                return 0.0;

            var cos = dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
            cos = Math.Max(0.0, Math.Min(1.0, cos));
            return Math.Round(cos * 100.0, 1);
        }

        private double Interpolate(double[] x, double[] y, double qx)
        {
            int n = Math.Min(x.Length, y.Length);
            if (n == 0)
                return 0.0;
            if (qx <= x[0])
                return y[0];
            if (qx >= x[n - 1])
                return y[n - 1];

            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (x[mid] <= qx)
                    lo = mid;
                else
                    hi = mid;
            }

            double x0 = x[lo], x1 = x[hi];
            double y0 = y[lo], y1 = y[hi];
            if (Math.Abs(x1 - x0) < 1e-12)
                return y0;
            double t = (qx - x0) / (x1 - x0);
            return y0 + (y1 - y0) * t;
        }

        private double[] TryReadSpectrumArray(object raw)
        {
            if (raw == null)
                return null;

            if (raw is double[] d)
                return d;

            if (raw is byte[] bytes)
            {
                try { return SpectrumProcessingService.DeserializeSpectrumData(bytes); } catch { }
            }

            if (raw is string s)
            {
                try { return Newtonsoft.Json.JsonConvert.DeserializeObject<double[]>(s); } catch { }
                try
                {
                    var buf = Convert.FromBase64String(s);
                    return SpectrumProcessingService.DeserializeSpectrumData(buf);
                }
                catch { }
            }

            return null;
        }

        private double GetPeakWavenumber(PeakInfo peak)
        {
            return ReadPropertyAsDouble(peak, "Wavenumber", "Position", "X", "W", "Center");
        }

        private double GetPeakIntensity(PeakInfo peak)
        {
            var val = ReadPropertyAsDouble(peak, "Intensity", "Height", "Y", "I", "Amplitude");
            return val <= 0 ? 1.0 : val;
        }

        private double ReadPropertyAsDouble(object obj, params string[] names)
        {
            if (obj == null || names == null)
                return 0.0;

            var type = obj.GetType();
            foreach (var name in names)
            {
                var p = type.GetProperty(name);
                if (p == null)
                    continue;
                var value = p.GetValue(obj);
                if (value == null)
                    continue;

                try
                {
                    return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                }
            }

            return 0.0;
        }

        #endregion

        #region 命令实现

        /// <summary>
        /// 加载实测光谱数据
        /// </summary>
        private void LoadRamanData()
        {
            try
            {
                if (SelectedRamanSpectrum == null)
                {
                    PreprocessStatus = "错误：未选中光谱";
                    return;
                }

                // 反序列化二进制数据为数组
                double[] xData = SpectrumProcessingService.DeserializeSpectrumData(SelectedRamanSpectrum.X_cm);
                double[] yData = SpectrumProcessingService.DeserializeSpectrumData(SelectedRamanSpectrum.Y_cm);

                // 加载到处理服务
                _processingService.LoadRawData(xData, yData);

                // 更新UI状态
                CurrentSubstanceName = SelectedRamanSpectrum.Name;
                PreprocessStatus = $"已加载光谱: {SelectedRamanSpectrum.Name}";
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"加载失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 执行预处理
        /// </summary>
        private void ExecutePreprocess()
        {
            try
            {
                PreprocessStatus = "正在处理...";

                var result = _processingService.PreprocessData(CurrentConfig);

                if (result.Success)
                {
                    PreprocessStatus = $"预处理完成: {result.Message}";
                }
                else
                {
                    PreprocessStatus = $"预处理失败: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"处理异常: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 执行分类（KNN匹配）
        /// </summary>
        private void ExecuteClassify()
        {
            try
            {
                PreprocessStatus = "正在执行分类...";
                // TODO: 集成 KnnClassifier 的分类逻辑
                PreprocessStatus = "分类完成";
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"分类失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 重置处理状态
        /// </summary>
        private void ResetState()
        {
            _processingService.ResetProcessingState();
            CurrentSubstanceName = "未知物质";
            PreprocessStatus = "已重置，待新输入";
        }

        /// <summary>
        /// 删除选中的光谱
        /// </summary>
        private void DeleteSelectedSpectrum()
        {
            try
            {
                if (IsRamanMode && SelectedRamanSpectrum != null)
                {
                    _databaseService.DeleteSubstance(SelectedRamanSpectrum.Id, "RamanSpectrum");
                    RamanSpectraList.Remove(SelectedRamanSpectrum);
                    PreprocessStatus = "已删除实测光谱";
                }
                else if (!IsRamanMode && SelectedReferenceSpectrum != null)
                {
                    _databaseService.DeleteSubstance(SelectedReferenceSpectrum.Id, "ReferenceSpectrum");
                    ReferenceSpectraList.Remove(SelectedReferenceSpectrum);
                    PreprocessStatus = "已删除标准光谱";
                }
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"删除失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 保存选中的光谱
        /// </summary>
        private void SaveSelectedSpectrum()
        {
            try
            {
                if (IsRamanMode && SelectedRamanSpectrum != null)
                {
                    _databaseService.UpdateRamanSpectrum(SelectedRamanSpectrum);
                    PreprocessStatus = "实测光谱已保存";
                }
                else if (!IsRamanMode && SelectedReferenceSpectrum != null)
                {
                    _databaseService.UpdateReference(SelectedReferenceSpectrum);
                    PreprocessStatus = "标准光谱已保存";
                }
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"保存失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 判断是否可以删除光谱
        /// </summary>
        private bool CanDeleteSpectrum()
        {
            return (IsRamanMode && SelectedRamanSpectrum != null) ||
                   (!IsRamanMode && SelectedReferenceSpectrum != null);
        }

        /// <summary>
        /// 判断是否可以保存光谱
        /// </summary>
        private bool CanSaveSpectrum()
        {
            return (IsRamanMode && SelectedRamanSpectrum != null) ||
                   (!IsRamanMode && SelectedReferenceSpectrum != null);
        }

        #endregion

        /// <summary>
        /// 批量删除已勾选的光谱（根据当前视图）
        /// </summary>
        public void DeleteCheckedSpectra()
        {
            try
            {
                if (IsRamanMode)
                {
                    var toDelete = _ramanSpectraList?.Where(r => r.IsChecked).ToList();
                    if (toDelete == null || toDelete.Count == 0) { PreprocessStatus = "未选中要删除的实测光谱"; return; }
                    foreach (var r in toDelete)
                    {
                        _databaseService.DeleteSubstance(r.Id, "RamanSpectrum");
                        _ramanSpectraList.Remove(r);
                    }
                    PreprocessStatus = $"已删除 {toDelete.Count} 条实测光谱";
                }
                else
                {
                    var toDelete = _referenceSpectraList?.Where(r => r.IsChecked).ToList();
                    if (toDelete == null || toDelete.Count == 0) { PreprocessStatus = "未选中要删除的参考光谱"; return; }
                    foreach (var r in toDelete)
                    {
                        _databaseService.DeleteSubstance(r.Id, "ReferenceSpectrum");
                        _referenceSpectraList.Remove(r);
                    }
                    PreprocessStatus = $"已删除 {toDelete.Count} 条参考光谱";
                }
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"批量删除失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 获取已勾选的实测光谱副本（用于导出）
        /// </summary>
        public System.Collections.Generic.List<RamanSpectrumModel> GetCheckedRaman()
        {
            return _ramanSpectraList?.Where(r => r.IsChecked).ToList() ?? new System.Collections.Generic.List<RamanSpectrumModel>();
        }

        /// <summary>
        /// 获取已勾选的参考光谱副本（用于导出）
        /// </summary>
        public System.Collections.Generic.List<ReferenceSpectrumModel> GetCheckedReference()
        {
            return _referenceSpectraList?.Where(r => r.IsChecked).ToList() ?? new System.Collections.Generic.List<ReferenceSpectrumModel>();
        }

        #region 辅助方法

        /// <summary>
        /// 刷新数据列表
        /// </summary>
        private void RefreshDataList()
        {
            try
            {
                var ramanData = _processingService.LoadAllRamanSpectra();
                var referenceData = _processingService.LoadAllReferenceSpectra();

                RamanSpectraList = new ObservableCollection<RamanSpectrumModel>(ramanData);
                ReferenceSpectraList = new ObservableCollection<ReferenceSpectrumModel>(referenceData);

                PreprocessStatus = $"已加载 {ramanData.Count} 条实测光谱, {referenceData.Count} 条标准光谱";
            }
            catch (Exception ex)
            {
                PreprocessStatus = $"加载数据失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 从本地配置文件加载算法参数
        /// </summary>
        private void LoadConfigFromLocal()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    var savedConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<AlgorithmConfig>(json);
                    if (savedConfig != null)
                    {
                        CurrentConfig = savedConfig;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
                CurrentConfig = new AlgorithmConfig();
            }
        }

        #endregion
    }

    /// <summary>
    /// 简单的 RelayCommand 实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();
    }
}
