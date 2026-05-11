# DTO Shapes and Read Models

Coalesce now supports a broader set of DTO and read-shape generation scenarios than the original "entity plus CRUD DTOs" flow.

This page tracks the behavior that matters when your models, projections, and generated contracts become more complex.

## Metadata discovery handles self-referential relationships

Model discovery now avoids recursing forever through self-referential foreign keys.

That matters for DTO generation because it lets Coalesce analyze real-world graphs safely before later read-shape and generated-contract features run on top of the discovered model.

## Key metadata can be inferred from the EF model

When CLR metadata alone is not enough to determine the right key shape, Coalesce can now use EF metadata to infer the effective key for DTO and code-generation scenarios.

This is especially important for generated read models and contract shapes, where key information has to stay stable even when the entity model is configured more heavily in EF than in attributes.

## Owned and complex EF types do not block discovery

Coalesce now tolerates EF-owned and complex/value-object shapes during metadata discovery instead of treating them like unsupported roots.

That lets the higher-level DTO and read-shape features work against richer EF models without requiring you to flatten those value objects away first.

## Navigation summary DTOs provide lightweight related-data shapes

Navigation summary DTOs add a smaller, purpose-built way to surface related model information in generated read APIs without always expanding full related entities.

Those summary shapes are the foundation for later flattened-property and auto-projected read-shape features higher in the stack.

## Flattened navigation-path properties can be emitted directly

Coalesce can now generate flattened DTO properties from selected navigation paths so callers can bind to the data they actually need without manually creating one-off view types for every combination.

This keeps generated read models compact while still making important related values available as first-class DTO members.
