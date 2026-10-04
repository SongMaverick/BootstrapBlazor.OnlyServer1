# Order Management Setup

This tool creates and validates the order-management database objects used by
`BootstrapBlazor.OnlyServer1`. The normal `--install` flow creates the current
order-management tables, including `OrderTypes`, and no longer creates the
removed design-plan tables.

## Commands

Run from the solution directory:

```bat
dotnet run --project BootstrapBlazor.OrderManagementSetup -- --check
dotnet run --project BootstrapBlazor.OrderManagementSetup -- --install
```

Before running against a production database, verify the connection string and
take a database backup.

`OrderTypes` stores one classification row per order:

- `OrderType`: `相同`, `相似`, or `全新`
- `ReferenceOrderNumber`: manually entered reference order number, required for
  `相同` and `相似`, empty for `全新`

The reference order number is entered manually. New saves do not require the
referenced order to exist in `Orders`.

The setup tool creates the filtered unique index
`IX_OrderTypes_OrderId` so an order has at most one active classification row.
When upgrading an existing `OrderTypes` table, `--install` adds the nullable
`ReferenceOrderNumber` column automatically.
The same upgrade removes the obsolete `ReferenceOrderId` column. Back up the
database before running the upgrade; this column removal is irreversible.

## Remove legacy design-plan data

The destructive cleanup is explicit and is never run by `--install`:

```bat
dotnet run --project BootstrapBlazor.OrderManagementSetup -- --remove-design-plan
```

The command asks for the exact confirmation `YES`, then removes these legacy
tables in dependency-safe order:

- `OrderMaterialArrivals`
- `OrderDesignMembers`
- `OrderSimilarity`
- `MaterialCatalog`
- `DesignPlans`

It also removes the legacy `DesignPlan.Read`, `DesignPlan.Edit`, and
`MaterialCatalog.Manage` permission records and role bindings. Back up the
database first; this operation permanently deletes the tables and their data.
