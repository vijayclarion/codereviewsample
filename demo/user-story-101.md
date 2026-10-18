# User Story #101 — Add items to shopping cart

**Type**: User Story
**State**: Active
**Tags**: cart; checkout

## Description

As a shopper, I want to add items to my shopping cart so that I can buy several
products in one order.

The cart is per-customer and persists between sessions. Adding a product that is
already in the cart should increase the quantity of the existing line rather than
creating a duplicate line.

## Acceptance Criteria

- Quantity must be a positive integer; a request with zero or a negative quantity
  is rejected with a validation error.
- Adding an unknown product returns a not-found response and does not change the
  cart.
- Adding a product already in the cart increments the quantity of the existing
  line instead of appending a second line for the same product.
- The system must not allow adding more items than are available in stock; a
  request exceeding available stock is rejected with a clear message.
- The cart total is recalculated after a successful add, before the cart is saved.

## Out of scope (tracked separately)

- #102 — Remove items from the cart
- #103 — Apply promotional codes at checkout
