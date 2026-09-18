using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SpectrumAnalyzer.Core;
using SpectrumAnalyzer.Data.Repositories;

namespace SpectrumAnalyzer
{
    public partial class SaveWindow : Window
    {
        public ReferenceSpectrumModel ResultModel { get; private set; }
        private bool _isEditMode = false;
        private System.Collections.ObjectModel.ObservableCollection<EditablePeak> _peakList;

        // 新增：全谱缓存字段，供数据库存入全波段波位与强度
        private double[] _fullX;
        private double[] _fullY;

        // 分类：由调用方注入全部分类列表，用于下拉选择
        public IReadOnlyList<CategoryModel> Categories { get; set; }
        private int? _existingCategoryId;

        // 分类来源：若注入仓储，则每次打开时直接从数据库现读，避免缓存/时序导致不同步
        public ICategoryRepository CategorySource { get; set; }

        // 构造函数 1：新建模式
        public SaveWindow(string defaultName, List<PeakInfo> peaks, double[] fullX = null, double[] fullY = null, string defaultFormula = "", string defaultNote = "")
        {
            InitializeComponent();
            TxtName.Text = defaultName;

            // === 需求 2 & 3：自动填充识别出的理想化学式与实测备注信息 ===
            TxtFormula.Text = defaultFormula;
            TxtDesc.Text = defaultNote;

            // === 需求 1：强制清空光栅文本框，覆盖掉 XAML 界面里可能写死的 "1200 l/mm" 默认值 ===
            TxtGrating.Text = "";

            // === 体验优化：将特征峰位四舍五入保留 1 位小数，将相对强度转为百分比形式展示 ===
            var list = peaks.Select(p => new EditablePeak
            {
                X = Math.Round(p.X, 1),         // 优化：峰位保留一位小数，避免显示超长浮点数
                Y = Math.Round(p.Y * 100.0, 1)  // 四舍五入保留一位小数
            }).ToList();

            _peakList = new System.Collections.ObjectModel.ObservableCollection<EditablePeak>(list);
            DgPeaks.ItemsSource = _peakList;
            _isEditMode = false;

            // 缓存保存全谱数据线
            _fullX = fullX;
            _fullY = fullY;

            // 初始化右键快捷菜单 + 分类下拉
            InitContextMenu();
            // CategorySource/Categories 由调用方在构造后才注入，故放到 Loaded 再填充分类
            Loaded += (s, e) => PopulateCategories();
        }

        // 构造函数 2：编辑模式
        public SaveWindow(ReferenceSpectrumModel existingModel)
        {
            InitializeComponent();
            ResultModel = existingModel;
            _isEditMode = true;
            TxtTitle.Text = "编辑标准库信息";

            TxtName.Text = existingModel.SubstanceName;
            TxtFormula.Text = existingModel.ChemicalFormula;
            TxtCas.Text = existingModel.CasNumber;
            TxtLaser.Text = existingModel.ExcitationWavelength;
            TxtGrating.Text = existingModel.Grating;
            TxtDesc.Text = existingModel.Note;

            // 从标准参考记录恢复全谱的缓存数据（如果旧记录含有全谱）
            if (!string.IsNullOrEmpty(existingModel.X_cm))
                _fullX = JsonConvert.DeserializeObject<double[]>(existingModel.X_cm);
            if (!string.IsNullOrEmpty(existingModel.Y_cm))
                _fullY = JsonConvert.DeserializeObject<double[]>(existingModel.Y_cm);

            // 解析 JSON 并映射到 X 和 Y (解决看不到数据的问题)
            try
            {
                var rawPeaks = JsonConvert.DeserializeObject<List<dynamic>>(existingModel.PeaksJson);
                var list = new List<EditablePeak>();
                foreach (var p in rawPeaks)
                {
                    list.Add(new EditablePeak
                    {
                        X = (double)(p.W ?? p.X), // 兼容 W 或 X

                        // 反序列化读取时，将 [0.0 - 1.0] 乘以 100 恢复为百分比形式展示
                        Y = Math.Round((double)(p.I ?? p.Y) * 100.0, 1)
                    });
                }
                _peakList = new System.Collections.ObjectModel.ObservableCollection<EditablePeak>(list);
                DgPeaks.ItemsSource = _peakList;
            }
            catch
            {
                _peakList = new System.Collections.ObjectModel.ObservableCollection<EditablePeak>();
                DgPeaks.ItemsSource = _peakList;
            }

            _existingCategoryId = existingModel.CategoryId;

            // 初始化右键快捷菜单 + 分类下拉
            InitContextMenu();
            // CategorySource/Categories 由调用方在构造后才注入，故放到 Loaded 再填充分类
            Loaded += (s, e) => PopulateCategories();
        }

        /// <summary>
        /// 为 DataGrid 绑定右键菜单，重用物理按钮的事件逻辑
        /// </summary>
        private void InitContextMenu()
        {
            if (DgPeaks == null) return;

            ContextMenu menu = new ContextMenu();

            // 菜单项 1：添加新峰位
            MenuItem addItem = new MenuItem { Header = "➕ 增加特征峰位" };
            addItem.Click += BtnAddPeak_Click; // 直接关联按钮方法

            // 菜单项 2：删除选中峰位
            MenuItem deleteItem = new MenuItem { Header = "🗑 删除选中峰位" };
            deleteItem.Click += BtnDeletePeak_Click; // 直接关联按钮方法

            menu.Items.Add(addItem);
            menu.Items.Add(deleteItem);

            // 绑定到 DataGrid 上
            DgPeaks.ContextMenu = menu;
        }

        /// <summary>
        /// 填充分类下拉框（树形拍平，用全角空格表示层级），默认选中已存分类或"(未分类)"
        /// </summary>
        private void PopulateCategories()
        {
            // 优先从数据库现读，回退到调用方注入的快照
            var cats = CategorySource?.GetAll() ?? Categories;
            var options = new List<CategoryOption> { new CategoryOption { Id = null, Label = "(未分类)" } };
            if (cats != null)
            {
                var byId = cats.ToDictionary(c => c.Id);
                foreach (var top in cats.Where(c => !c.ParentId.HasValue).OrderBy(c => c.SortOrder).ThenBy(c => c.Id))
                    FlattenCategory(top, byId, 0, options);
            }
            CmbCategory.ItemsSource = options;
            CmbCategory.DisplayMemberPath = "Label";
            CmbCategory.SelectedItem = options.FirstOrDefault(o => o.Id == _existingCategoryId) ?? options[0];
        }

        private static void FlattenCategory(CategoryModel cat, Dictionary<int, CategoryModel> byId, int depth, List<CategoryOption> result)
        {
            result.Add(new CategoryOption { Id = cat.Id, Label = new string('\u3000', depth * 2) + cat.Name });
            var children = byId.Values.Where(x => x.ParentId == cat.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
            foreach (var child in children)
                FlattenCategory(child, byId, depth + 1, result);
        }

        // 界面“增加特征峰”实体按钮的点击事件
        private void BtnAddPeak_Click(object sender, RoutedEventArgs e)
        {
            if (_peakList == null)
            {
                _peakList = new System.Collections.ObjectModel.ObservableCollection<EditablePeak>();
                DgPeaks.ItemsSource = _peakList;
            }
            // 插入一条新特征峰数据行，默认值为 0.0 (%)
            _peakList.Add(new EditablePeak { X = 0.0, Y = 0.0 });
        }

        // 界面“删除选中峰”实体按钮的点击事件
        private void BtnDeletePeak_Click(object sender, RoutedEventArgs e)
        {
            if (DgPeaks.SelectedItem is EditablePeak selected)
            {
                _peakList.Remove(selected);
            }
            else
            {
                MessageBox.Show("请先在列表中点击选择要删除的特征峰！", "提示");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text)) { MessageBox.Show("名称不能为空"); return; }

            int? chosenCategoryId = (CmbCategory.SelectedItem as CategoryOption)?.Id;

            // 强制提交流量中所有处于“正在编辑”状态的单元格和行，确保当前光标所在的修改数据落盘
            if (DgPeaks != null)
            {
                DgPeaks.CommitEdit(DataGridEditingUnit.Cell, true);
                DgPeaks.CommitEdit(DataGridEditingUnit.Row, true);
            }

            // 从 DataGrid 重新抓取用户修改、增加或删除后的最新数据
            var updatedPeakData = _peakList.Select(p => new {
                W = Math.Round(p.X, 1),

                // 将界面上的百分比值（如 21.2）除以 100.0，还原为 [0.0 - 1.0] 的相对强度存入 PeaksJson，防止比对算法出错
                I = Math.Round(p.Y / 100.0, 3)
            }).ToList();

            if (_isEditMode)
            {
                ResultModel.SubstanceName = TxtName.Text;
                ResultModel.ChemicalFormula = TxtFormula.Text;
                ResultModel.CasNumber = TxtCas.Text;
                ResultModel.ExcitationWavelength = TxtLaser.Text;
                ResultModel.Grating = TxtGrating.Text;
                ResultModel.Note = TxtDesc.Text;
                ResultModel.PeaksJson = JsonConvert.SerializeObject(updatedPeakData);
                ResultModel.CategoryId = chosenCategoryId;

                // 编辑模式也同步支持更新全谱 JSON
                if (_fullX != null) ResultModel.X_cm = JsonConvert.SerializeObject(_fullX);
                if (_fullY != null) ResultModel.Y_cm = JsonConvert.SerializeObject(_fullY);
            }
            else
            {
                ResultModel = new ReferenceSpectrumModel
                {
                    SubstanceName = TxtName.Text,
                    ChemicalFormula = TxtFormula.Text,
                    CasNumber = TxtCas.Text,
                    ExcitationWavelength = TxtLaser.Text,
                    Grating = TxtGrating.Text,
                    Note = TxtDesc.Text,
                    PeaksJson = JsonConvert.SerializeObject(updatedPeakData),
                    CategoryId = chosenCategoryId,

                    // 将缓存的全谱信号一并序列化保存到数据库中
                    X_cm = _fullX != null ? JsonConvert.SerializeObject(_fullX) : null,
                    Y_cm = _fullY != null ? JsonConvert.SerializeObject(_fullY) : null
                };
            }

            this.DialogResult = true;
        }

        // 解决 BtnCancel_Click 报错
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        public class EditablePeak
        {
            public double X { get; set; } // 对应峰位
            public double Y { get; set; } // 对应强度 (%)
        }

        /// <summary>
        /// 分类下拉框选项（Id 为 null 表示"未分类"）
        /// </summary>
        public class CategoryOption
        {
            public int? Id { get; set; }
            public string Label { get; set; }
        }
    }
}