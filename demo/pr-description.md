Implements the add-to-cart endpoint for user story #101.

`CartController.AddItem` now validates the requested quantity, handles unknown
products, merges into an existing cart line rather than duplicating it, checks
availability, and recalculates the cart total before saving.

## Covered

- Quantity validation — rejects zero and negative quantities
- Unknown product returns not-found without changing the cart
- Adding an existing product increments the existing line
- Stock limit enforced when adding to cart
- Cart total recalculated after a successful add

## Not in this PR

- #102 — Remove items from the cart
- #103 — Apply promotional codes at checkout

## Testing

Manual smoke test against the local API. Unit tests to follow in a separate
change.
