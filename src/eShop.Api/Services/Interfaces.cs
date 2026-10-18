using eShop.Api.Models;

namespace eShop.Api.Services
{
    public interface ICartRepository
    {
        Cart GetForCustomer(string customerId);
        void Save(Cart cart);
    }

    public interface ICatalogService
    {
        /// <summary>Returns the product, or null when no product has that id.</summary>
        Product Find(string productId);
    }
}
