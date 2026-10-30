using Microsoft.EntityFrameworkCore;
using inventory_management.Models;

namespace inventory_management.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShelfUnit> ShelfUnits => Set<ShelfUnit>();
    public DbSet<UserEbayConnection> UserEbayConnections => Set<UserEbayConnection>();
    public DbSet<InventoryItemLocation> InventoryItemLocations => Set<InventoryItemLocation>();
    public DbSet<InventoryListing> InventoryListings => Set<InventoryListing>();
    public DbSet<InventoryItemUnit> InventoryItemUnits => Set<InventoryItemUnit>();
    public DbSet<Box> Boxes => Set<Box>();
    public DbSet<InventoryItemBoxAssignment> InventoryItemBoxAssignments => Set<InventoryItemBoxAssignment>();
    public DbSet<RecentOrder> RecentOrders => Set<RecentOrder>();
    public DbSet<RelistHistory> RelistHistories => Set<RelistHistory>();
    public DbSet<PriceReductionHistory> PriceReductionHistories => Set<PriceReductionHistory>();
    public DbSet<ShippingBox> ShippingBoxes => Set<ShippingBox>();
    public DbSet<EbayDraftListing> EbayDraftListings => Set<EbayDraftListing>();
    public DbSet<UserDraftSettings> UserDraftSettings => Set<UserDraftSettings>();
    public DbSet<StorageUnit> StorageUnits => Set<StorageUnit>();
    public DbSet<InventoryItemStorageUnitAssignment> InventoryItemStorageUnitAssignments => Set<InventoryItemStorageUnitAssignment>();
    public DbSet<ShelfSlotStatus> ShelfSlotStatuses => Set<ShelfSlotStatus>();
    public DbSet<AdminAuth> AdminAuth => Set<AdminAuth>();
    public DbSet<Consignor> Consignors => Set<Consignor>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();
    public DbSet<SaleRecord> SaleRecords => Set<SaleRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ShelfUnit>(entity =>
        {
            entity.ToTable("shelf_units");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Identifier)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.BarcodeNumber)
                .HasMaxLength(14)
                .IsRequired();

            entity.Property(x => x.ShelfCount)
                .IsRequired();

            entity.HasIndex(x => x.Identifier)
                .IsUnique();

            entity.HasIndex(x => x.BarcodeNumber)
                .IsUnique();
        });

        modelBuilder.Entity<UserEbayConnection>(entity =>
        {
            entity.ToTable("user_ebay_connections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.AccessToken)
                .HasColumnName("access_token")
                .IsRequired();

            entity.Property(x => x.RefreshToken)
                .HasColumnName("refresh_token")
                .IsRequired();

            entity.Property(x => x.ExpiresAtUtc)
                .HasColumnName("expires_at_utc")
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.HasIndex(x => x.Username)
                .IsUnique();
        });

        modelBuilder.Entity<InventoryItemLocation>(entity =>
        {
            entity.ToTable("inventory_item_locations");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.ItemId)
                .HasColumnName("item_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ItemTitle)
                .HasColumnName("item_title")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.ShelfUnitIdentifier)
                .HasColumnName("shelf_unit_identifier")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ShelfNumber)
                .HasColumnName("shelf_number")
                .IsRequired();

            entity.Property(x => x.ShelfBarcode)
                .HasColumnName("shelf_barcode")
                .HasMaxLength(14)
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.Property(x => x.IsSold)
                .HasColumnName("is_sold")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(x => x.UnitId)
                .HasColumnName("unit_id");

            entity.HasIndex(x => new { x.Username, x.ItemId });

            entity.HasIndex(x => new { x.Username, x.UnitId })
                .IsUnique()
                .HasFilter("unit_id IS NOT NULL");
        });

        modelBuilder.Entity<InventoryItemUnit>(entity =>
        {
            entity.ToTable("inventory_item_units");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ListingId).HasColumnName("listing_id").IsRequired();
            entity.Property(x => x.UnitIndex).HasColumnName("unit_index").IsRequired();
            entity.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(14).IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(x => x.IsSold).HasColumnName("is_sold").HasDefaultValue(false).IsRequired();
            entity.Property(x => x.LabelPrintedAtUtc).HasColumnName("label_printed_at_utc");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            entity.HasIndex(x => new { x.Username, x.Barcode }).IsUnique();
            entity.HasIndex(x => new { x.Username, x.ListingId });

            // FK-only relationship (no navigation property on InventoryItemUnit) so EF's change
            // tracker knows a unit depends on its listing and always inserts the listing first in
            // the same SaveChangesAsync. Without this, ListingId was just a plain scalar column
            // with no relationship EF was aware of, so a brand-new listing + its unit-1 row
            // created in the same sync call could batch in either order -- and did, in prod,
            // causing recurring 23503 "inventory_item_units_listing_id_fkey" violations that
            // silently killed every eBay sync that introduced a new listing. Name matches the
            // real DB constraint created by the raw SQL guard in Program.cs.
            entity.HasOne<InventoryListing>()
                .WithMany()
                .HasForeignKey(x => x.ListingId)
                .HasConstraintName("inventory_item_units_listing_id_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryListing>(entity =>
        {
            entity.ToTable("inventory_listings");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.ItemId)
                .HasColumnName("item_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.Title)
                .HasColumnName("title")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.CoverPhotoUrl)
                .HasColumnName("cover_photo_url")
                .HasMaxLength(2000);

            entity.Property(x => x.IsActive)
                .HasColumnName("is_active")
                .IsRequired();

            entity.Property(x => x.Barcode)
                .HasColumnName("barcode")
                .HasMaxLength(14)
                .IsRequired();

            entity.Property(x => x.ListedAtUtc)
                .HasColumnName("listed_at_utc")
                .IsRequired();

            entity.Property(x => x.LastSeenAtUtc)
                .HasColumnName("last_seen_at_utc")
                .IsRequired();

            entity.Property(x => x.InactivatedAtUtc)
                .HasColumnName("inactivated_at_utc");

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.Property(x => x.CurrentPrice)
                .HasColumnName("current_price");

            entity.Property(x => x.CurrencyId)
                .HasColumnName("currency_id")
                .HasMaxLength(10);

            entity.Property(x => x.AttentionIgnoredUntilUtc)
                .HasColumnName("attention_ignored_until_utc");

            entity.Property(x => x.AttentionIgnoredPermanently)
                .HasColumnName("attention_ignored_permanently")
                .IsRequired();

            entity.Property(x => x.AutoRelistEnabled)
                .HasColumnName("auto_relist_enabled")
                .IsRequired();

            entity.Property(x => x.ListingEndTimeUtc)
                .HasColumnName("listing_end_time_utc");

            entity.Property(x => x.ListingType)
                .HasColumnName("listing_type")
                .HasMaxLength(50);

            entity.Property(x => x.RelistInProgress)
                .HasColumnName("relist_in_progress")
                .IsRequired();

            entity.Property(x => x.LabelPrintedAtUtc)
                .HasColumnName("label_printed_at_utc");

            entity.Property(x => x.QuantityAvailable)
                .HasColumnName("quantity_available");

            entity.Property(x => x.OwnershipType)
                .HasColumnName("ownership_type")
                .HasMaxLength(20);

            entity.Property(x => x.ConsignorId)
                .HasColumnName("consignor_id");

            entity.Property(x => x.ConsignmentSplitPercent)
                .HasColumnName("consignment_split_percent");

            entity.Property(x => x.PurchaseCost)
                .HasColumnName("purchase_cost");

            entity.Property(x => x.ClassifiedAtUtc)
                .HasColumnName("classified_at_utc");

            entity.HasIndex(x => new { x.Username, x.ItemId })
                .IsUnique();

            entity.HasIndex(x => new { x.Username, x.IsActive });
        });

        modelBuilder.Entity<RelistHistory>(entity =>
        {
            entity.ToTable("inventory_relist_history");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.ListingId)
                .HasColumnName("listing_id")
                .IsRequired();

            entity.Property(x => x.OldItemId)
                .HasColumnName("old_item_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.NewItemId)
                .HasColumnName("new_item_id")
                .HasMaxLength(50);

            entity.Property(x => x.RelistedAtUtc)
                .HasColumnName("relisted_at_utc")
                .IsRequired();

            entity.Property(x => x.ErrorMessage)
                .HasColumnName("error_message");

            entity.HasOne(x => x.Listing)
                .WithMany()
                .HasForeignKey(x => x.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.Username, x.ListingId });
        });

        modelBuilder.Entity<PriceReductionHistory>(entity =>
        {
            entity.ToTable("inventory_price_reduction_history");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.ListingId)
                .HasColumnName("listing_id")
                .IsRequired();

            entity.Property(x => x.OldPrice)
                .HasColumnName("old_price")
                .HasPrecision(10, 2)
                .IsRequired();

            entity.Property(x => x.NewPrice)
                .HasColumnName("new_price")
                .HasPrecision(10, 2)
                .IsRequired();

            entity.Property(x => x.ReducedAtUtc)
                .HasColumnName("reduced_at_utc")
                .IsRequired();

            entity.Property(x => x.ChangeType)
                .HasColumnName("change_type")
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("AutoReduction");

            entity.HasOne(x => x.Listing)
                .WithMany()
                .HasForeignKey(x => x.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.Username, x.ListingId });
        });

        modelBuilder.Entity<Box>(entity =>
        {
            entity.ToTable("boxes");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.BoxBarcode)
                .HasColumnName("box_number")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.BoxIdentifier)
                .HasColumnName("box_identifier")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ShelfUnitIdentifier)
                .HasColumnName("shelf_unit_identifier")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ShelfNumber)
                .HasColumnName("shelf_number")
                .IsRequired();

            entity.Property(x => x.ShelfBarcode)
                .HasColumnName("shelf_barcode")
                .HasMaxLength(14)
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.HasIndex(x => new { x.Username, x.BoxBarcode })
                .IsUnique();

            entity.HasIndex(x => new { x.Username, x.BoxIdentifier })
                .IsUnique();
        });

        modelBuilder.Entity<ShippingBox>(entity =>
        {
            entity.ToTable("shipping_boxes");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.LengthIn)
                .HasColumnName("length_in")
                .HasPrecision(8, 2)
                .IsRequired();

            entity.Property(x => x.WidthIn)
                .HasColumnName("width_in")
                .HasPrecision(8, 2)
                .IsRequired();

            entity.Property(x => x.HeightIn)
                .HasColumnName("height_in")
                .HasPrecision(8, 2)
                .IsRequired();

            entity.Property(x => x.WeightLbs)
                .HasColumnName("weight_lbs");

            entity.Property(x => x.WeightOz)
                .HasColumnName("weight_oz")
                .HasPrecision(6, 1);

            entity.Property(x => x.Quantity)
                .HasColumnName("quantity")
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.HasIndex(x => new { x.Username, x.Name })
                .IsUnique();
        });

        modelBuilder.Entity<InventoryItemBoxAssignment>(entity =>
        {
            entity.ToTable("inventory_item_box_assignments");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.ItemId)
                .HasColumnName("item_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ItemTitle)
                .HasColumnName("item_title")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.BoxId)
                .HasColumnName("box_id")
                .IsRequired();

            entity.Property(x => x.BoxBarcode)
                .HasColumnName("box_number")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.Property(x => x.IsSold)
                .HasColumnName("is_sold")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(x => x.UnitId)
                .HasColumnName("unit_id");

            entity.HasIndex(x => new { x.Username, x.ItemId });

            entity.HasIndex(x => new { x.Username, x.UnitId })
                .IsUnique()
                .HasFilter("unit_id IS NOT NULL");
        });

        modelBuilder.Entity<RecentOrder>(entity =>
        {
            entity.ToTable("recent_orders");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ItemId).HasColumnName("item_id").HasMaxLength(50).IsRequired();
            entity.Property(x => x.ItemName).HasColumnName("item_name").HasMaxLength(500).IsRequired();
            entity.Property(x => x.ItemPhotoUrl).HasColumnName("item_photo_url").HasMaxLength(2000);
            entity.Property(x => x.BuyerName).HasColumnName("buyer_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.HasTracking).HasColumnName("has_tracking").IsRequired();
            entity.Property(x => x.IsRetrieved).HasColumnName("is_retrieved").IsRequired();
            entity.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
            entity.Property(x => x.BuyerStreet1).HasColumnName("buyer_street1").HasMaxLength(500);
            entity.Property(x => x.BuyerStreet2).HasColumnName("buyer_street2").HasMaxLength(500);
            entity.Property(x => x.BuyerCity).HasColumnName("buyer_city").HasMaxLength(200);
            entity.Property(x => x.BuyerState).HasColumnName("buyer_state").HasMaxLength(100);
            entity.Property(x => x.BuyerZip).HasColumnName("buyer_zip").HasMaxLength(20);
            entity.Property(x => x.BuyerCountry).HasColumnName("buyer_country").HasMaxLength(10);
            entity.Property(x => x.BuyerPhone).HasColumnName("buyer_phone").HasMaxLength(50);
            entity.Property(x => x.OrderCreatedAtUtc).HasColumnName("order_created_at_utc");
            entity.Property(x => x.LastSeenAtUtc).HasColumnName("last_seen_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.Property(x => x.TransactionPriceGross).HasColumnName("transaction_price_gross");
            entity.Property(x => x.ShippingCollected).HasColumnName("shipping_collected");
            entity.Property(x => x.TaxCollected).HasColumnName("tax_collected");
            entity.Property(x => x.EbayFee).HasColumnName("ebay_fee");
            entity.Property(x => x.ShippingLabelCost).HasColumnName("shipping_label_cost");
            entity.Property(x => x.CurrencyId).HasColumnName("currency_id").HasMaxLength(10);

            entity.HasIndex(x => new { x.Username, x.OrderId, x.ItemId }).IsUnique();
            entity.HasIndex(x => new { x.Username, x.OrderCreatedAtUtc });
        });

        modelBuilder.Entity<Consignor>(entity =>
        {
            entity.ToTable("consignors");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.DefaultSplitPercent).HasColumnName("default_split_percent").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasIndex(x => new { x.Username, x.Name }).IsUnique();
        });

        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.ToTable("receipts");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ReceiptDate).HasColumnName("receipt_date").IsRequired();
            entity.Property(x => x.TotalPrice).HasColumnName("total_price").HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasIndex(x => new { x.Username, x.ReceiptDate });
        });

        modelBuilder.Entity<ReceiptItem>(entity =>
        {
            entity.ToTable("receipt_items");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ReceiptId).HasColumnName("receipt_id").IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(500);
            entity.Property(x => x.PreTaxPrice).HasColumnName("pre_tax_price").HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.AllocatedTotalPrice).HasColumnName("allocated_total_price").HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.AssignedListingId).HasColumnName("assigned_listing_id");
            entity.Property(x => x.AssignedAtUtc).HasColumnName("assigned_at_utc");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasOne<Receipt>()
                .WithMany()
                .HasForeignKey(x => x.ReceiptId)
                .HasConstraintName("receipt_items_receipt_id_fkey")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.Username, x.ReceiptId });
            entity.HasIndex(x => x.AssignedListingId);
        });

        modelBuilder.Entity<SaleRecord>(entity =>
        {
            entity.ToTable("sale_records");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ListingId).HasColumnName("listing_id").IsRequired();
            entity.Property(x => x.UnitId).HasColumnName("unit_id").IsRequired();
            entity.Property(x => x.ItemId).HasColumnName("item_id").HasMaxLength(50).IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            entity.Property(x => x.OwnershipType).HasColumnName("ownership_type").HasMaxLength(20).IsRequired();
            entity.Property(x => x.ConsignorId).HasColumnName("consignor_id");
            entity.Property(x => x.ConsignorName).HasColumnName("consignor_name").HasMaxLength(200);
            entity.Property(x => x.ConsignorSplitPercent).HasColumnName("consignor_split_percent");
            entity.Property(x => x.PurchaseCost).HasColumnName("purchase_cost");
            entity.Property(x => x.SalePriceGross).HasColumnName("sale_price_gross");
            entity.Property(x => x.ShippingCollected).HasColumnName("shipping_collected");
            entity.Property(x => x.TaxCollected).HasColumnName("tax_collected");
            entity.Property(x => x.EbayFee).HasColumnName("ebay_fee");
            entity.Property(x => x.ShippingLabelCost).HasColumnName("shipping_label_cost");
            entity.Property(x => x.CurrencyId).HasColumnName("currency_id").HasMaxLength(10);
            entity.Property(x => x.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(x => x.SoldAtUtc).HasColumnName("sold_at_utc").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            entity.HasIndex(x => x.UnitId).IsUnique();
            entity.HasIndex(x => new { x.Username, x.SoldAtUtc });
            entity.HasIndex(x => new { x.Username, x.ConsignorId });
        });

        modelBuilder.Entity<EbayDraftListing>(entity =>
        {
            entity.ToTable("ebay_drafts");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.DraftId).HasColumnName("draft_id").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
            entity.Property(x => x.Description).HasColumnName("description");
            entity.Property(x => x.CoverPhotoUrl).HasColumnName("cover_photo_url").HasMaxLength(2000);
            entity.Property(x => x.ConditionId).HasColumnName("condition_id").HasMaxLength(100);
            entity.Property(x => x.ConditionDescription).HasColumnName("condition_description");
            entity.Property(x => x.Price).HasColumnName("price").HasPrecision(10, 2);
            entity.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(10);
            entity.Property(x => x.WeightPounds).HasColumnName("weight_pounds").HasPrecision(8, 2);
            entity.Property(x => x.LengthIn).HasColumnName("length_in").HasPrecision(8, 2);
            entity.Property(x => x.WidthIn).HasColumnName("width_in").HasPrecision(8, 2);
            entity.Property(x => x.HeightIn).HasColumnName("height_in").HasPrecision(8, 2);
            entity.Property(x => x.CategoryId).HasColumnName("category_id").HasMaxLength(50);
            entity.Property(x => x.SyncedAtUtc).HasColumnName("synced_at_utc").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            entity.HasIndex(x => new { x.Username, x.DraftId }).IsUnique();
        });

        modelBuilder.Entity<UserDraftSettings>(entity =>
        {
            entity.ToTable("user_draft_settings");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.DescriptionTemplate).HasColumnName("description_template").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<StorageUnit>(entity =>
        {
            entity.ToTable("storage_units");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Identifier).HasColumnName("identifier").HasMaxLength(50).IsRequired();
            entity.Property(x => x.BarcodeNumber).HasColumnName("barcode_number").HasMaxLength(14).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasIndex(x => x.Identifier).IsUnique();
            entity.HasIndex(x => x.BarcodeNumber).IsUnique();
        });

        modelBuilder.Entity<InventoryItemStorageUnitAssignment>(entity =>
        {
            entity.ToTable("inventory_item_storage_unit_assignments");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ItemId).HasColumnName("item_id").HasMaxLength(50).IsRequired();
            entity.Property(x => x.ItemTitle).HasColumnName("item_title").HasMaxLength(500).IsRequired();
            entity.Property(x => x.StorageUnitId).HasColumnName("storage_unit_id").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.Property(x => x.IsSold).HasColumnName("is_sold").HasDefaultValue(false).IsRequired();
            entity.Property(x => x.UnitId).HasColumnName("unit_id");

            entity.HasIndex(x => new { x.Username, x.ItemId });

            entity.HasIndex(x => new { x.Username, x.UnitId })
                .IsUnique()
                .HasFilter("unit_id IS NOT NULL");
        });

        modelBuilder.Entity<Box>(entity =>
        {
            entity.Property(x => x.StorageUnitId).HasColumnName("storage_unit_id");
            entity.Property(x => x.Status).HasColumnName("status").HasDefaultValue(ShelfStatus.ActiveInventory);
            entity.Property(x => x.IsTransitBox).HasColumnName("is_transit_box").HasDefaultValue(false).IsRequired();
        });

        modelBuilder.Entity<ShelfUnit>(entity =>
        {
            entity.Property(x => x.StorageUnitId).HasColumnName("storage_unit_id");
            entity.Property(x => x.Status).HasColumnName("status").HasDefaultValue(ShelfStatus.ActiveInventory);
        });

        modelBuilder.Entity<ShelfSlotStatus>(entity =>
        {
            entity.ToTable("shelf_slot_statuses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ShelfUnitId).HasColumnName("shelf_unit_id").IsRequired();
            entity.Property(x => x.ShelfNumber).HasColumnName("shelf_number").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasDefaultValue(ShelfStatus.ActiveInventory);
            entity.HasIndex(x => new { x.ShelfUnitId, x.ShelfNumber }).IsUnique();
        });

        modelBuilder.Entity<AdminAuth>(entity =>
        {
            entity.ToTable("admin_auth");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(x => x.RequirePasswordChange).HasColumnName("require_password_change").IsRequired();
        });
    }
}
