using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SpectrumAnalyzer.Core;
using SpectrumAnalyzer.Data.Repositories;

namespace SpectrumAnalyzer
{
    public partial class CategoryManagerWindow : Window
    {
        private readonly ICategoryRepository _repository;

        public CategoryManagerWindow(ICategoryRepository repository)
        {
            InitializeComponent();
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            ReloadTree();
        }

        private void ReloadTree()
        {
            var all = _repository.GetAll();
            var byId = all.ToDictionary(c => c.Id);
            var roots = all.Where(c => !c.ParentId.HasValue).OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
            foreach (var r in roots) AttachChildren(r, byId);
            TvCategories.ItemsSource = roots;
        }

        private static void AttachChildren(CategoryModel parent, Dictionary<int, CategoryModel> byId)
        {
            foreach (var child in byId.Values
                         .Where(x => x.ParentId == parent.Id)
                         .OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            {
                AttachChildren(child, byId);
                parent.Children.Add(child);
            }
        }

        private CategoryModel Selected => TvCategories.SelectedItem as CategoryModel;

        private int NextSortOrder(int? parentId)
        {
            var all = _repository.GetAll();
            var siblings = parentId.HasValue
                ? all.Where(x => x.ParentId == parentId)
                : all.Where(x => !x.ParentId.HasValue);
            return siblings.Any() ? siblings.Max(x => x.SortOrder) + 1 : 0;
        }

        private void BtnAddTop_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text)) { MessageBox.Show("请输入分类名称"); return; }
            _repository.Add(new CategoryModel
            {
                Name = TxtName.Text.Trim(),
                ParentId = null,
                SortOrder = NextSortOrder(null),
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            TxtName.Clear();
            ReloadTree();
        }

        private void BtnAddChild_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel == null) { MessageBox.Show("请先在左侧选择父分类"); return; }
            if (string.IsNullOrWhiteSpace(TxtName.Text)) { MessageBox.Show("请输入子分类名称"); return; }
            _repository.Add(new CategoryModel
            {
                Name = TxtName.Text.Trim(),
                ParentId = sel.Id,
                SortOrder = NextSortOrder(sel.Id),
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            TxtName.Clear();
            ReloadTree();
        }

        private void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel == null) { MessageBox.Show("请先选择要重命名的分类"); return; }
            if (string.IsNullOrWhiteSpace(TxtName.Text)) { MessageBox.Show("请输入新名称"); return; }
            sel.Name = TxtName.Text.Trim();
            _repository.Update(sel);
            TxtName.Clear();
            ReloadTree();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel == null) { MessageBox.Show("请先选择要删除的分类"); return; }
            if (MessageBox.Show($"确定删除分类“{sel.Name}”吗？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (!_repository.Delete(sel.Id))
                MessageBox.Show("删除失败：该分类下可能还有子分类或光谱，请先处理。", "提示");
            else
                ReloadTree();
        }

        private void MoveSelected(int delta)
        {
            var sel = Selected;
            if (sel == null) return;

            var all = _repository.GetAll();
            var siblings = all.Where(x => x.ParentId == sel.ParentId)
                              .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
            int idx = siblings.FindIndex(s => s.Id == sel.Id);
            int target = idx + delta;
            if (idx < 0 || target < 0 || target >= siblings.Count) return;

            var tmp = siblings[idx];
            siblings[idx] = siblings[target];
            siblings[target] = tmp;

            for (int i = 0; i < siblings.Count; i++)
            {
                siblings[i].SortOrder = i;
                _repository.Update(siblings[i]);
            }

            int movedId = siblings[target].Id;
            ReloadTree();
            SelectById(movedId);
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

        private void BtnDown_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

        private void BtnDone_Click(object sender, RoutedEventArgs e) => Close();

        private void SelectById(int id)
        {
            if (!(TvCategories.ItemsSource is IEnumerable<CategoryModel> tree)) return;
            var node = FindNode(tree, id);
            if (node != null) SelectNode(node);
        }

        private static CategoryModel FindNode(IEnumerable<CategoryModel> items, int id)
        {
            foreach (var item in items)
            {
                if (item.Id == id) return item;
                var child = FindNode(item.Children, id);
                if (child != null) return child;
            }
            return null;
        }

        private void SelectNode(CategoryModel node)
        {
            if (TvCategories.ItemContainerGenerator.ContainerFromItem(node) is TreeViewItem tvi)
            {
                tvi.IsSelected = true;
                tvi.Focus();
            }
        }
    }
}