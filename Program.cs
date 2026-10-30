using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
// Login is handled by a reverse proxy in front of this app — see Services/ForwardedAuthHandler.cs.
builder.Services
    .AddAuthentication(ForwardedAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ForwardedAuthHandler>(ForwardedAuthHandler.SchemeName, null);
// Served under a path prefix next to another app, whose TempData cookie
// has the default name at path "/" — a shared name would make each app try to read
// (and fail to decrypt) the other's.
builder.Services.Configure<CookieTempDataProviderOptions>(o => o.Cookie.Name = ".ebay.TempData");
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<EbayOptions>(builder.Configuration.GetSection(EbayOptions.SectionName));
builder.Services.AddScoped<IEbayService, EbayService>();
builder.Services.AddScoped<IShelfUnitStore, ShelfUnitStore>();
// Off for a second copy of the app pointed at the same database (a preview or local
// run), so only one instance ever ends and relists listings on eBay.
if (!builder.Configuration.GetValue<bool>("DisableBackgroundWorkers"))
{
    builder.Services.AddHostedService<AutoRelistWorker>();
}

var app = builder.Build();

// "/app" when hosted under a prefix; empty when run on its own.
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

var disableHttpsRedirect = app.Configuration.GetValue<bool>("DisableHttpsRedirect");
if (!disableHttpsRedirect)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.UseAuthentication();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (PostgresException ex) when (ex.SqlState == "42P07")
    {
        // Table already exists; continue startup.
    }
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS user_ebay_connections (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            access_token text NOT NULL,
            refresh_token text NOT NULL,
            expires_at_utc timestamp with time zone NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_user_ebay_connections_username
        ON user_ebay_connections (username);");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS inventory_item_locations (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            item_id character varying(50) NOT NULL,
            item_title character varying(500) NOT NULL,
            shelf_unit_identifier character varying(50) NOT NULL,
            shelf_number integer NOT NULL,
            shelf_barcode character varying(14) NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_item_locations_username_item_id
        ON inventory_item_locations (username, item_id);");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_item_locations
        ADD COLUMN IF NOT EXISTS is_sold boolean NOT NULL DEFAULT false;");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS inventory_listings (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            item_id character varying(50) NOT NULL,
            title character varying(500) NOT NULL,
            cover_photo_url character varying(2000) NULL,
            is_active boolean NOT NULL DEFAULT true,
            last_seen_at_utc timestamp with time zone NOT NULL,
            inactivated_at_utc timestamp with time zone NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_listings_username_item_id
        ON inventory_listings (username, item_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_inventory_listings_username_is_active
        ON inventory_listings (username, is_active);");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS listed_at_utc timestamp with time zone NULL;");
    db.Database.ExecuteSqlRaw(@"
        UPDATE inventory_listings
        SET listed_at_utc = last_seen_at_utc
        WHERE listed_at_utc IS NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ALTER COLUMN listed_at_utc SET NOT NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS barcode character varying(14) NULL;");
    db.Database.ExecuteSqlRaw(@"
        UPDATE inventory_listings
        SET barcode = '1' || LPAD((FLOOR(RANDOM() * 1000000000000))::bigint::text, 12, '0') || '1'
        WHERE barcode IS NULL OR barcode = '';");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ALTER COLUMN barcode SET NOT NULL;");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_listings_barcode
        ON inventory_listings (barcode);");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS current_price numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS currency_id character varying(10) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS price_reduction_months integer NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS price_reduction_type character varying(20) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS price_reduction_amount numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS minimum_price numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS last_price_reduction_utc timestamp with time zone NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS auto_relist_enabled boolean NOT NULL DEFAULT true;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS listing_end_time_utc timestamp with time zone NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS listing_type character varying(50) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS relist_in_progress boolean NOT NULL DEFAULT false;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS label_printed_at_utc timestamp with time zone NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS quantity_available integer NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS attention_ignored_until_utc timestamp with time zone NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS attention_ignored_permanently boolean NOT NULL DEFAULT false;");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS inventory_relist_history (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            listing_id uuid NOT NULL REFERENCES inventory_listings(id) ON DELETE CASCADE,
            old_item_id character varying(50) NOT NULL,
            new_item_id character varying(50) NULL,
            relisted_at_utc timestamp with time zone NOT NULL DEFAULT now(),
            error_message text NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_inventory_relist_history_username_listing_id
        ON inventory_relist_history (username, listing_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS inventory_price_reduction_history (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            listing_id uuid NOT NULL REFERENCES inventory_listings(id) ON DELETE CASCADE,
            old_price numeric(10,2) NOT NULL,
            new_price numeric(10,2) NOT NULL,
            reduced_at_utc timestamp with time zone NOT NULL DEFAULT now()
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_inventory_price_reduction_history_username_listing_id
        ON inventory_price_reduction_history (username, listing_id);");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_price_reduction_history
        ADD COLUMN IF NOT EXISTS change_type character varying(20) NOT NULL DEFAULT 'AutoReduction';");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS boxes (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            box_identifier character varying(50) NOT NULL DEFAULT '',
            box_number character varying(100) NOT NULL,
            shelf_unit_identifier character varying(50) NOT NULL,
            shelf_number integer NOT NULL,
            shelf_barcode character varying(14) NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE boxes
        ADD COLUMN IF NOT EXISTS box_identifier character varying(50);");
    db.Database.ExecuteSqlRaw(@"
        UPDATE boxes
        SET box_identifier = CONCAT('LEGACY', SUBSTRING(REPLACE(id::text, '-', '') FROM 1 FOR 8))
        WHERE box_identifier IS NULL OR box_identifier = '';");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_boxes_username_box_number
        ON boxes (username, box_number);");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_boxes_username_box_identifier
        ON boxes (username, box_identifier);");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS inventory_item_box_assignments (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            item_id character varying(50) NOT NULL,
            item_title character varying(500) NOT NULL,
            box_id uuid NOT NULL,
            box_number character varying(100) NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_item_box_assignments_username_item_id
        ON inventory_item_box_assignments (username, item_id);");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_item_box_assignments
        ADD COLUMN IF NOT EXISTS is_sold boolean NOT NULL DEFAULT false;");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS recent_orders (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            order_id character varying(100) NOT NULL,
            item_id character varying(50) NOT NULL,
            item_name character varying(500) NOT NULL,
            item_photo_url character varying(2000) NULL,
            buyer_name character varying(200) NOT NULL,
            has_tracking boolean NOT NULL DEFAULT false,
            order_created_at_utc timestamp with time zone NULL,
            last_seen_at_utc timestamp with time zone NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_recent_orders_username_order_id_item_id
        ON recent_orders (username, order_id, item_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_recent_orders_username_order_created_at_utc
        ON recent_orders (username, order_created_at_utc);");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS quantity integer NOT NULL DEFAULT 1;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS is_retrieved boolean NOT NULL DEFAULT false;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS buyer_street1 character varying(500) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS buyer_street2 character varying(500) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS buyer_city character varying(200) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS buyer_state character varying(100) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS buyer_zip character varying(20) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS buyer_country character varying(100) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS buyer_phone character varying(50) NULL;");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS shipping_boxes (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            name character varying(100) NOT NULL,
            length_in numeric(8,2) NOT NULL,
            width_in numeric(8,2) NOT NULL,
            height_in numeric(8,2) NOT NULL,
            weight_lbs integer NULL,
            weight_oz numeric(6,1) NULL,
            quantity integer NOT NULL DEFAULT 0,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_shipping_boxes_username_name
        ON shipping_boxes (username, name);");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ebay_drafts (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            draft_id character varying(100) NOT NULL,
            title character varying(500) NULL,
            description text NULL,
            cover_photo_url character varying(2000) NULL,
            condition_id character varying(100) NULL,
            condition_description text NULL,
            price numeric(10,2) NULL,
            currency character varying(10) NULL,
            weight_pounds numeric(8,2) NULL,
            length_in numeric(8,2) NULL,
            width_in numeric(8,2) NULL,
            height_in numeric(8,2) NULL,
            category_id character varying(50) NULL,
            synced_at_utc timestamp with time zone NOT NULL,
            created_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_ebay_drafts_username_draft_id
        ON ebay_drafts (username, draft_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS user_draft_settings (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            description_template text NOT NULL DEFAULT '',
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_user_draft_settings_username
        ON user_draft_settings (username);");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS storage_units (
            id uuid PRIMARY KEY,
            name character varying(200) NOT NULL,
            identifier character varying(50) NOT NULL,
            barcode_number character varying(14) NOT NULL DEFAULT '',
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_storage_units_identifier
        ON storage_units (identifier);");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_storage_units_barcode_number
        ON storage_units (barcode_number) WHERE barcode_number <> '';");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS inventory_item_storage_unit_assignments (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            item_id character varying(50) NOT NULL,
            item_title character varying(500) NOT NULL,
            storage_unit_id uuid NOT NULL REFERENCES storage_units(id) ON DELETE CASCADE,
            updated_at_utc timestamp with time zone NOT NULL
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_item_storage_unit_assignments_username_item_id
        ON inventory_item_storage_unit_assignments (username, item_id);");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_item_storage_unit_assignments
        ADD COLUMN IF NOT EXISTS is_sold boolean NOT NULL DEFAULT false;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE shelf_units
        DROP COLUMN IF EXISTS ""Location"";");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE boxes
        ADD COLUMN IF NOT EXISTS storage_unit_id uuid NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE boxes
        ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE boxes
        ADD COLUMN IF NOT EXISTS is_transit_box boolean NOT NULL DEFAULT false;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE shelf_units
        ADD COLUMN IF NOT EXISTS storage_unit_id uuid NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE shelf_units
        ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0;");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS shelf_slot_statuses (
            id uuid NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
            shelf_unit_id uuid NOT NULL,
            shelf_number integer NOT NULL,
            status integer NOT NULL DEFAULT 0
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_shelf_slot_statuses_unit_shelf
        ON shelf_slot_statuses (shelf_unit_id, shelf_number);");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS admin_auth (
            id SERIAL PRIMARY KEY,
            password_hash TEXT NOT NULL,
            require_password_change BOOLEAN NOT NULL DEFAULT FALSE
        );");
    // admin_auth is no longer read (login moved to the proxy); the table is
    // kept so the EF model and existing data are untouched, but nothing seeds it now.

    // Multi-quantity unit tracking: one row per physical unit of a listing. Unit 1 always
    // reuses the parent listing's existing barcode (LOCKED — never regenerated), so every
    // barcode already printed and stuck on a physical item keeps working unchanged.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS inventory_item_units (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            listing_id uuid NOT NULL REFERENCES inventory_listings(id) ON DELETE CASCADE,
            unit_index integer NOT NULL,
            barcode character varying(14) NOT NULL,
            status character varying(20) NOT NULL DEFAULT 'Listed',
            is_sold boolean NOT NULL DEFAULT false,
            label_printed_at_utc timestamp with time zone NULL,
            created_at_utc timestamp with time zone NOT NULL DEFAULT now()
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_item_units_username_barcode
        ON inventory_item_units (username, barcode);");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_inventory_item_units_username_listing_id
        ON inventory_item_units (username, listing_id);");
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO inventory_item_units
            (id, username, listing_id, unit_index, barcode, status, is_sold, label_printed_at_utc, created_at_utc)
        SELECT gen_random_uuid(), l.username, l.id, 1, l.barcode, 'Listed', false, l.label_printed_at_utc, l.listed_at_utc
        FROM inventory_listings l
        WHERE NOT EXISTS (
            SELECT 1 FROM inventory_item_units u WHERE u.listing_id = l.id AND u.unit_index = 1
        );");

    // unit_id links each location row to the specific physical unit it holds. Backfilled from
    // each listing's unit-1 row so every existing location keeps pointing at the same physical
    // item it always has; the old (username, item_id) unique constraint is relaxed to a plain
    // index (multiple units of the same listing can now each have their own location) and
    // replaced by a unique constraint on (username, unit_id) instead.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_item_locations
        ADD COLUMN IF NOT EXISTS unit_id uuid NULL;");
    db.Database.ExecuteSqlRaw(@"
        UPDATE inventory_item_locations loc
        SET unit_id = u.id
        FROM inventory_item_units u
        JOIN inventory_listings il ON il.id = u.listing_id
        WHERE u.unit_index = 1
          AND il.username = loc.username
          AND il.item_id = loc.item_id
          AND loc.unit_id IS NULL;");
    db.Database.ExecuteSqlRaw(@"
        DROP INDEX IF EXISTS ix_inventory_item_locations_username_item_id;");
    // The original EF migration created this index as "IX_inventory_item_locations_username_item_id"
    // (quoted, mixed-case) — a distinct, case-sensitive identifier from the unquoted lowercase
    // DROP INDEX above, so it silently survived and kept enforcing the old one-location-per-item_id
    // uniqueness, blocking assignment of any unit past the first for the same item. Must be dropped
    // with matching quoted casing.
    db.Database.ExecuteSqlRaw(@"
        DROP INDEX IF EXISTS ""IX_inventory_item_locations_username_item_id"";");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_inventory_item_locations_username_item_id
        ON inventory_item_locations (username, item_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_item_locations_username_unit_id
        ON inventory_item_locations (username, unit_id) WHERE unit_id IS NOT NULL;");

    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_item_box_assignments
        ADD COLUMN IF NOT EXISTS unit_id uuid NULL;");
    db.Database.ExecuteSqlRaw(@"
        UPDATE inventory_item_box_assignments boxa
        SET unit_id = u.id
        FROM inventory_item_units u
        JOIN inventory_listings il ON il.id = u.listing_id
        WHERE u.unit_index = 1
          AND il.username = boxa.username
          AND il.item_id = boxa.item_id
          AND boxa.unit_id IS NULL;");
    db.Database.ExecuteSqlRaw(@"
        DROP INDEX IF EXISTS ix_inventory_item_box_assignments_username_item_id;");
    // Same case-sensitive-identifier gap as inventory_item_locations above: the EF migration's
    // quoted "IX_inventory_item_box_assignments_username_item_id" survived the unquoted lowercase
    // DROP INDEX and kept blocking a second unit of the same item from getting its own box row.
    db.Database.ExecuteSqlRaw(@"
        DROP INDEX IF EXISTS ""IX_inventory_item_box_assignments_username_item_id"";");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_inventory_item_box_assignments_username_item_id
        ON inventory_item_box_assignments (username, item_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_item_box_assignments_username_unit_id
        ON inventory_item_box_assignments (username, unit_id) WHERE unit_id IS NOT NULL;");

    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_item_storage_unit_assignments
        ADD COLUMN IF NOT EXISTS unit_id uuid NULL;");
    db.Database.ExecuteSqlRaw(@"
        UPDATE inventory_item_storage_unit_assignments sua
        SET unit_id = u.id
        FROM inventory_item_units u
        JOIN inventory_listings il ON il.id = u.listing_id
        WHERE u.unit_index = 1
          AND il.username = sua.username
          AND il.item_id = sua.item_id
          AND sua.unit_id IS NULL;");
    db.Database.ExecuteSqlRaw(@"
        DROP INDEX IF EXISTS ix_inventory_item_storage_unit_assignments_username_item_id;");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_inventory_item_storage_unit_assignments_username_item_id
        ON inventory_item_storage_unit_assignments (username, item_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_inventory_item_storage_unit_assignments_username_unit_id
        ON inventory_item_storage_unit_assignments (username, unit_id) WHERE unit_id IS NOT NULL;");

    // Ownership classification, consignors, and sales stats (added 2026-08-14). Applies only to
    // currently-active and future listings — see Services/OwnershipRules.cs for the cutoff date
    // and classification rules.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS consignors (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            name character varying(200) NOT NULL,
            default_split_percent numeric(5,2) NOT NULL DEFAULT 50,
            created_at_utc timestamp with time zone NOT NULL DEFAULT now(),
            updated_at_utc timestamp with time zone NOT NULL DEFAULT now()
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_consignors_username_name
        ON consignors (username, name);");

    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS ownership_type character varying(20) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS consignor_id uuid NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS consignment_split_percent numeric(5,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS purchase_cost numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE inventory_listings
        ADD COLUMN IF NOT EXISTS classified_at_utc timestamp with time zone NULL;");

    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS transaction_price_gross numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS shipping_collected numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS tax_collected numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS ebay_fee numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS shipping_label_cost numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE recent_orders
        ADD COLUMN IF NOT EXISTS currency_id character varying(10) NULL;");

    // Permanent snapshot of every resolved sale from this feature's cutoff date forward —
    // independent of recent_orders' 90-day purge. One row per InventoryItemUnit, ever (see the
    // unique index on unit_id), created by Services/SaleResolutionService.cs::MarkUnitsSoldAsync.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS sale_records (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            listing_id uuid NOT NULL,
            unit_id uuid NOT NULL,
            item_id character varying(50) NOT NULL,
            title character varying(500) NOT NULL,
            ownership_type character varying(20) NOT NULL,
            consignor_id uuid NULL,
            consignor_name character varying(200) NULL,
            consignor_split_percent numeric(5,2) NULL,
            purchase_cost numeric(10,2) NULL,
            sale_price_gross numeric(10,2) NULL,
            shipping_collected numeric(10,2) NULL,
            tax_collected numeric(10,2) NULL,
            ebay_fee numeric(10,2) NULL,
            currency_id character varying(10) NULL,
            order_id character varying(100) NOT NULL,
            sold_at_utc timestamp with time zone NOT NULL,
            created_at_utc timestamp with time zone NOT NULL DEFAULT now()
        );");
    // CREATE TABLE IF NOT EXISTS above only covers a brand-new install — an existing
    // sale_records table needs this added explicitly, same as recent_orders.shipping_label_cost.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE sale_records
        ADD COLUMN IF NOT EXISTS shipping_label_cost numeric(10,2) NULL;");
    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ix_sale_records_unit_id
        ON sale_records (unit_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_sale_records_username_sold_at_utc
        ON sale_records (username, sold_at_utc);");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_sale_records_username_consignor_id
        ON sale_records (username, consignor_id);");

    // Receipts (added 2026-08-14): track purchase receipts and let each line item be assigned
    // to an inventory item, which pushes AllocatedTotalPrice onto that listing's PurchaseCost —
    // see Services/ReceiptCalculator.cs and Controllers/ReceiptsController.cs.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS receipts (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            receipt_date date NOT NULL,
            total_price numeric(10,2) NOT NULL,
            created_at_utc timestamp with time zone NOT NULL DEFAULT now(),
            updated_at_utc timestamp with time zone NOT NULL DEFAULT now()
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_receipts_username_receipt_date
        ON receipts (username, receipt_date);");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS receipt_items (
            id uuid PRIMARY KEY,
            username character varying(100) NOT NULL,
            receipt_id uuid NOT NULL REFERENCES receipts (id) ON DELETE CASCADE,
            name character varying(500) NULL,
            pre_tax_price numeric(10,2) NOT NULL,
            allocated_total_price numeric(10,2) NOT NULL DEFAULT 0,
            assigned_listing_id uuid NULL,
            assigned_at_utc timestamp with time zone NULL,
            created_at_utc timestamp with time zone NOT NULL DEFAULT now(),
            updated_at_utc timestamp with time zone NOT NULL DEFAULT now()
        );");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_receipt_items_username_receipt_id
        ON receipt_items (username, receipt_id);");
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_receipt_items_assigned_listing_id
        ON receipt_items (assigned_listing_id);");
}

app.Run();
