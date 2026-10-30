# Inventory Management

A proof-of-concept ASP.NET Core MVC (.NET 9) app for running the operational side of an eBay store: eBay-driven active inventory, physical storage management, barcode printing, and mobile scan-to-assign workflows.

> Showcase copy. Credentials, hostnames and deployment details have been removed. It is not configured to run against a live store out of the box.

## Features
- **eBay sync** via OAuth 2.0 (Inventory, Fulfillment, Marketing and Finances APIs): active listings, orders, fees and shipping costs.
- **Physical storage model**: shelving units, shelf slots and boxes, with item counts and status (Active / Unlisted / Needs Sorted).
- **Barcodes**: every item and storage location gets a permanent barcode, rendered as QR codes for printing. Item barcodes never change, even when eBay relists under a new ItemID.
- **Scan-to-assign**: scan a location, then scan items to put them there. Works with a USB/Bluetooth scanner or the phone camera.
- **Multi-quantity tracking**: one row per physical unit of a listing, each with its own barcode and location, plus backstock.
- **Sale resolution**: detects sold units and asks which physical unit shipped only when it is ambiguous.
- **Needs Attention queue, bulk repricing, auto-relist worker, receipts/cost tracking, consignors and profit stats.**

## Design notes
- **Per-user data isolation**: every table has a `Username` column and every query filters by the signed-in user.
- **Locked invariants**: an item's barcode, listing ID and unit ID are set once and never reassigned, because labels are already stuck on physical items.
- **Schema** is managed by EF Core migrations plus idempotent startup SQL guards in `Program.cs`.
- **Auth** is delegated: the app has no login of its own. A reverse proxy (nginx `auth_request`) forwards an `X-Forwarded-User` header, which `Services/ForwardedAuthHandler.cs` trusts only from loopback.

## Stack
ASP.NET Core MVC, EF Core with PostgreSQL (Npgsql), Bootstrap 5 and vanilla JS.

## Running it
1. Create a PostgreSQL database and set `ConnectionStrings:DefaultConnection` in `appsettings.json` (or via environment variables / user secrets).
2. Optionally add your own eBay developer keys under `Ebay:` (use the Sandbox environment).
3. Put the app behind a proxy that sets `X-Forwarded-User`, or adapt `ForwardedAuthHandler` for local development.
4. `dotnet run`

Never commit real credentials. Use environment variables or `dotnet user-secrets`.
