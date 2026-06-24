# DTO Shapes and Read Models

Coalesce now supports a broader set of DTO and read-shape generation scenarios than the original "entity plus CRUD DTOs" flow.

This page tracks the behavior that matters when your models, projections, and generated contracts become more complex.

## Metadata discovery handles self-referential relationships

Model discovery now avoids recursing forever through self-referential foreign keys.

That matters for DTO generation because it lets Coalesce analyze real-world graphs safely before later read-shape and generated-contract features run on top of the discovered model.
