# Code Review Checklist

Shared review rules. Every pull request is measured against these.

## Correctness

- Business rules from the story are enforced in code, not merely assumed or
  documented. Every constraint the acceptance criteria state must have a
  corresponding check.
- Input is validated before use, and validation failures return a clear error
  rather than throwing.
- Error and not-found paths return the correct status and leave state unchanged.
- Numeric arithmetic on quantities and money cannot silently overflow or go
  negative.

## Security

- No secret, connection string, token, or key appears in source, configuration
  committed to the repo, or logs.
- User-supplied identifiers are never interpolated directly into a query.
- Authorization is checked on any operation that reads or writes another user's
  data.

## Maintainability

- Public methods do one thing; a method that validates, mutates, and persists is
  a candidate for splitting.
- No commented-out code or leftover debugging output.
- Names describe intent rather than type.

## Testing

- A behavioural change is accompanied by a test that would fail without it.
- Edge cases named in the acceptance criteria have explicit test coverage.
