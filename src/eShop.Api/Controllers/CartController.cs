using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using eShop.Api.Models;
using eShop.Api.Services;

namespace eShop.Api.Controllers
{
    [ApiController]
    [Route("api/carts")]
    public class CartController : ControllerBase
    {
        private readonly ICartRepository _carts;
        private readonly ICatalogService _catalog;

        public CartController(ICartRepository carts, ICatalogService catalog)
        {
            _carts = carts;
            _catalog = catalog;
        }

        /// <summary>
        /// Adds a product to the customer's cart, or increases the quantity of the
        /// existing line when the product is already in the cart. User story #101.
        /// </summary>
        [HttpPost("{customerId}/items")]
        public IActionResult AddItem(string customerId, [FromBody] AddItemRequest request)
        {
            // AC1: quantity must be a positive integer.
            if (request == null || request.Quantity <= 0)
            {
                return BadRequest(new { message = "Quantity must be a positive integer." });
            }

            // AC2: unknown product returns not-found and leaves the cart unchanged.
            var product = _catalog.Find(request.ProductId);
            if (product == null)
            {
                return NotFound(new { message = $"Product '{request.ProductId}' was not found." });
            }

            var cart = _carts.GetForCustomer(customerId) ?? Cart.CreateFor(customerId);
            var existing = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);

            if (existing != null)
            {
                // AC3: increment the existing line rather than appending a duplicate.
                existing.Quantity += request.Quantity;
            }
            else
            {
                cart.Items.Add(new CartItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = product.Price,
                    Quantity = request.Quantity
                });
            }

            if (!string.IsNullOrEmpty(request.CouponCode))
            {
                cart.CouponCode = request.CouponCode;
                cart.CouponAppliedAt = DateTime.Now;
            }

            // AC5: recalculate the total before saving.
            cart.Recalculate();
            _carts.Save(cart);

            if (cart.CouponCode != null)
            {
                cart.ApplyCoupon(10m);
            }

            return Ok(cart);
        }
    }
}
