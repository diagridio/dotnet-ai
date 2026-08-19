---
name: unit-converter
description: Reference guidance for converting between common units of measurement (length, weight, temperature).
---

# Unit Converter

Use this skill whenever the user asks to convert a value between units of measurement.

## Conversions

- Length: 1 mile = 1.60934 kilometers; 1 inch = 2.54 centimeters; 1 foot = 0.3048 meters.
- Weight: 1 pound = 0.453592 kilograms; 1 ounce = 28.3495 grams.
- Temperature: `celsius = (fahrenheit - 32) / 1.8`; `fahrenheit = celsius * 1.8 + 32`.

## Guidance

1. Identify the source unit, target unit, and the value to convert.
2. Apply the appropriate formula from the table above (interpolating for units not listed directly,
   e.g. combine miles→kilometers with kilo/hecto/deca prefixes as needed).
3. Round to 2 decimal places unless the user asks for more precision.
4. Always state both the original and converted value, e.g. "10 miles is approximately 16.09 km."

See `references/notes.md` for a couple of edge cases worth double-checking.
