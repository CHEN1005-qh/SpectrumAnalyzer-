using System.Collections.Generic;
using SpectrumAnalyzer.Core;

namespace SpectrumAnalyzer.Data.Repositories
{
    public interface ICategoryRepository
    {
        List<CategoryModel> GetAll();
        CategoryModel GetById(int id);
        bool Add(CategoryModel category);
        bool Update(CategoryModel category);
        bool Delete(int id);
    }
}