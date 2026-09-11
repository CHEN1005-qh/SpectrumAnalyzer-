using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SpectrumAnalyzer.Core;
using SpectrumAnalyzer.Data;

namespace SpectrumAnalyzer.Services
{
    /// <summary>
    /// 光谱数据处理服务
    /// 负责：数据加载、预处理、峰值检测等核心业务逻辑
    /// </summary>
    public class SpectrumProcessingService
    {
        private readonly DatabaseService _databaseService;
        // 限制并发处理数量，避免同时运行过多耗时任务导致内存/CPU 激增
        private static readonly SemaphoreSlim _processingSemaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));

        // 当前处理状态缓存
        private double[] _currentX;
        private double[] _currentYRaw;
        private double[] _currentYProcessed;
        private double[] _currentXProcessed;
        private List<PeakInfo> _currentPeaks;

        /// <summary>
        /// 公开属性：获取当前处理后的X轴数据
        /// </summary>
        public double[] CurrentXProcessed => _currentXProcessed;

        /// <summary>
        /// 公开属性：获取当前处理后的Y轴数据
        /// </summary>
        public double[] CurrentYProcessed => _currentYProcessed;

        /// <summary>
        /// 公开属性：获取当前检测到的特征峰
        /// </summary>
        public List<PeakInfo> CurrentPeaks => _currentPeaks ?? new List<PeakInfo>();

        /// <summary>
        /// 公开属性：获取当前原始数据
        /// </summary>
        public double[] CurrentX => _currentX;
        public double[] CurrentYRaw => _currentYRaw;

        public SpectrumProcessingService(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        /// <summary>
        /// 加载原始光谱数据
        /// </summary>
        public void LoadRawData(double[] xData, double[] yRawData)
        {
            _currentX = xData != null ? (double[])xData.Clone() : null;
            _currentYRaw = yRawData != null ? (double[])yRawData.Clone() : null;
            _currentYProcessed = null;
            _currentXProcessed = null;
            _currentPeaks = null;
        }

        /// <summary>
        /// 同步入口：保留原始同步签名，内部委托到异步版本（会阻塞当前线程）
        /// </summary>
        public ProcessingResult PreprocessData(AlgorithmConfig config)
        {
            return PreprocessDataAsync(config, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 异步执行数据预处理流程，支持取消与并发限制
        /// </summary>
        public async Task<ProcessingResult> PreprocessDataAsync(AlgorithmConfig config, CancellationToken cancellationToken)
        {
            await _processingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = new ProcessingResult();

                try
                {
                    if (_currentX == null || _currentYRaw == null)
                    {
                        result.Success = false;
                        result.Message = "未加载任何光谱数据";
                        return result;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // 克隆原始数据以避免污染
                    double[] activeX = (double[])_currentX.Clone();
                    double[] activeYRaw = (double[])_currentYRaw.Clone();

                    // 1. 执行低频区间裁剪
                    if (config.CropBelow200)
                    {
                        var cropped = ApplyLowFrequencyCutoff(activeX, activeYRaw, 200.0);
                        activeX = cropped.croppedX;
                        activeYRaw = cropped.croppedY;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // 2. 预处理数据（去毛刺、平滑、基线校正、归一化）
                    double[] processedY = await Task.Run(() => ProcessDataInternal(activeYRaw, config, cancellationToken), cancellationToken).ConfigureAwait(false);

                    cancellationToken.ThrowIfCancellationRequested();

                    // 3. 峰值检测
                    var peaks = PreprocessingService.FindPeaksAdvanced(activeX, processedY, config);

                    // 保存结果到缓存
                    _currentXProcessed = activeX;
                    _currentYProcessed = processedY;
                    _currentPeaks = peaks;

                    result.Success = true;
                    result.ProcessedX = activeX;
                    result.ProcessedY = processedY;
                    result.Peaks = peaks;
                    result.Message = $"处理完成，检测到 {peaks.Count} 个特征峰";
                }
                catch (OperationCanceledException)
                {
                    result.Success = false;
                    result.Message = "预处理已取消";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"预处理失败: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine(ex);
                }

                return result;
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        /// <summary>
        /// 兼容方法：保留无 CancellationToken 的签名，内部委托到可取消版本
        /// </summary>
        private double[] ProcessDataInternal(double[] rawY, AlgorithmConfig activeConfig)
        {
            return ProcessDataInternal(rawY, activeConfig, CancellationToken.None);
        }

        /// <summary>
        /// 内部预处理方法（支持取消）：执行去毛刺、平滑、基线校正、归一化
        /// </summary>
        private double[] ProcessDataInternal(double[] rawY, AlgorithmConfig activeConfig, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // rawY is already a cloned buffer by the caller (PreprocessData),
            // perform in-place processing to avoid extra array allocations.
            double[] y = rawY;

            // 1. 去毛刺 (宇宙射线/毛刺)
            if (activeConfig.EnableDespike)
            {
                ct.ThrowIfCancellationRequested();
                y = PreprocessingService.RemoveSpikes(y, activeConfig.DespikeWindow, activeConfig.DespikeZ);
            }

            // 2. SG 平滑
            if (activeConfig.EnableSmoothing)
            {
                ct.ThrowIfCancellationRequested();
                y = PreprocessingService.SavitzkyGolaySmooth(y, activeConfig.SgWindowSize, activeConfig.SgOrder);
            }

            // 3. 基线扣除
            if (activeConfig.EnableBaseline)
            {
                ct.ThrowIfCancellationRequested();
                double[] baseline;

                // 分支选择基线算法
                if (activeConfig.BaselineMethod == "Snip")
                {
                    // 极速 SNIP 算法
                    baseline = PreprocessingService.SnipBaseline(y, activeConfig.SnipIterations);
                }
                else
                {
                    // 原滚动分位数基线算法并执行 SG 平滑
                    baseline = PreprocessingService.QuantileBaseline(y, activeConfig.BaselineWindow, activeConfig.BaselineQuantile);
                    baseline = PreprocessingService.SavitzkyGolaySmooth(baseline, activeConfig.BaselineSmoothWindow, activeConfig.SgOrder);
                }

                // 基线消除 —— 原地计算以避免产生临时数组，降低 GC 压力
                for (int i = 0; i < y.Length; i++)
                {
                    if ((i & 0x3FF) == 0) // 每1024次检查一次取消请求，降低检查开销
                        ct.ThrowIfCancellationRequested();

                    double diff = y[i] - baseline[i];
                    y[i] = diff > 0.0 ? diff : 0.0;
                }
            }

            // 4. 归一化 (0.0 - 1.0)
            if (activeConfig.EnableNormalization)
            {
                ct.ThrowIfCancellationRequested();
                y = PreprocessingService.Normalize(y);
            }

            return y;
        }

        /// <summary>
        /// 执行低频区间裁剪 (去除低于阈值的波数)
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
        /// 从数据库加载所有实测光谱
        /// </summary>
        public List<RamanSpectrumModel> LoadAllRamanSpectra()
        {
            try
            {
                return _databaseService.GetRamanLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载实测光谱失败: {ex.Message}");
                return new List<RamanSpectrumModel>();
            }
        }

        /// <summary>
        /// 从数据库加载所有标准参考光谱
        /// </summary>
        public List<ReferenceSpectrumModel> LoadAllReferenceSpectra()
        {
            try
            {
                return _databaseService.GetReferenceLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载标准光谱失败: {ex.Message}");
                return new List<ReferenceSpectrumModel>();
            }
        }

        /// <summary>
        /// 重置处理状态
        /// </summary>
        public void ResetProcessingState()
        {
            _currentYProcessed = null;
            _currentXProcessed = null;
            _currentPeaks = null;
        }

        /// <summary>
        /// 从光谱二进制数据反序列化为数组
        /// </summary>
        public static double[] DeserializeSpectrumData(byte[] binaryData)
        {
            if (binaryData == null || binaryData.Length == 0)
                return new double[0];

            int count = binaryData.Length / sizeof(double);
            double[] data = new double[count];
            Buffer.BlockCopy(binaryData, 0, data, 0, binaryData.Length);
            return data;
        }

        /// <summary>
        /// 将光谱数据序列化为二进制
        /// </summary>
        public static byte[] SerializeSpectrumData(double[] data)
        {
            if (data == null || data.Length == 0)
                return new byte[0];

            byte[] binaryData = new byte[data.Length * sizeof(double)];
            Buffer.BlockCopy(data, 0, binaryData, 0, binaryData.Length);
            return binaryData;
        }
    }

    /// <summary>
    /// 光谱预处理结果
    /// </summary>
    public class ProcessingResult
    {
        /// <summary>
        /// 处理是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 处理信息/错误消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 处理后的X轴数据
        /// </summary>
        public double[] ProcessedX { get; set; }

        /// <summary>
        /// 处理后的Y轴数据
        /// </summary>
        public double[] ProcessedY { get; set; }

        /// <summary>
        /// 检测到的特征峰
        /// </summary>
        public List<PeakInfo> Peaks { get; set; }
    }
}
