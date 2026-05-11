# DTO Shapes and Read Models

Coalesce now supports a broader set of DTO and read-shape generation scenarios than the original "entity plus CRUD DTOs" flow.

This page tracks the behavior that matters when your models, projections, and generated contracts become more complex.

## Metadata discovery handles self-referential relationships

Model discovery now avoids recursing forever through self-referential foreign keys.

That matters for DTO generation because it lets Coalesce analyze real-world graphs safely before later read-shape and generated-contract features run on top of the discovered model.

## Key metadata can be inferred from the EF model

When CLR metadata alone is not enough to determine the right key shape, Coalesce can now use EF metadata to infer the effective key for DTO and code-generation scenarios.

This is especially important for generated read models and contract shapes, where key information has to stay stable even when the entity model is configured more heavily in EF than in attributes.
