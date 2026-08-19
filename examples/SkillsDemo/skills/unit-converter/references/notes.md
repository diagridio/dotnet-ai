# Notes and edge cases

- Temperature conversions are affine, not linear — never apply the multiplicative factor alone
  without also applying the offset (a common mistake).
- "Ton" is ambiguous (short ton vs. metric tonne vs. long ton) — ask which one is meant if the
  user doesn't specify and the answer would meaningfully differ.
- When converting a range (e.g. "60-75°F"), convert both endpoints rather than the midpoint.
