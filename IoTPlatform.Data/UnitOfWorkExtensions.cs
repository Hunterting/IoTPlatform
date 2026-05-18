using IoTPlatform.Data.Repositories.Interfaces;
using System.Linq;

namespace IoTPlatform.Data
{
    public static class UnitOfWorkExtensions
    {
        public static IQueryable<T> Query<T>(this IUnitOfWork unitOfWork) where T : class
        {
            return unitOfWork.GetRepository<T>().Query();
        }
    }
}
