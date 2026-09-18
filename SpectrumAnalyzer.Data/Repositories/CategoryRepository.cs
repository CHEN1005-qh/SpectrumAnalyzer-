using System;
using System.Collections.Generic;
using SpectrumAnalyzer.Core;

namespace SpectrumAnalyzer.Data.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly DatabaseService _databaseService;

        public CategoryRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        public List<CategoryModel> GetAll()
        {
            try
            {
                return _databaseService.GetCategories();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取分类失败: {ex.Message}");
                return new List<CategoryModel>();
            }
        }

        public CategoryModel GetById(int id)
        {
            try
            {
                return _databaseService.GetCategoryById(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"根据ID获取分类失败: {ex.Message}");
                return null;
            }
        }

        public bool Add(CategoryModel category)
        {
            try
            {
                _databaseService.AddCategory(category);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"新增分类失败: {ex.Message}");
                return false;
            }
        }

        public bool Update(CategoryModel category)
        {
            try
            {
                _databaseService.UpdateCategory(category);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新分类失败: {ex.Message}");
                return false;
            }
        }

        public bool Delete(int id)
        {
            try
            {
                _databaseService.DeleteCategory(id);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除分类失败: {ex.Message}");
                return false;
            }
        }
    }
}