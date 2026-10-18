using System.Collections.Generic;
using System.Linq;

namespace eShop.Api.Models
{
    public class Cart
    {
        public string CustomerId { get; set; }
        public List<CartItem> Items { get; set; } = new List<CartItem>();
        public decimal Total { get; private set; }

        public static Cart CreateFor(string customerId)
        {
            return new Cart { CustomerId = customerId };
        }

        public void Recalculate()
        {
            Total = Items.Sum(i => i.UnitPrice * i.Quantity);
        }
    }

    public class CartItem
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    }

    public class AddItemRequest
    {
        public string ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class Product
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int StockOnHand { get; set; }
    }
}
