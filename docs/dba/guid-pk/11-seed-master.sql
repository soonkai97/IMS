/*
  11-seed-master.sql -- master data, identical for every key strategy: 4 units, 49 categories, $(Products) products with one barcode each,
  5 warehouses, 120 partners, 11 users, one StockBalance row per product and warehouse. Keys come from the column defaults (identity or GUID generator of the variant).
  Variables: Products (e.g. 50000). Also fills the writers' lookup tables (_Pick_*): product number n -> key.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'Ims[_]PerfGuid%' THROW 50000, N'Scratch databases only.', 1;

INSERT dbo.Uom (Code, Name) VALUES (N'PCS', N'Piece'), (N'BOX', N'Box'), (N'KG', N'Kilogram'), (N'M', N'Metre');
INSERT dbo.Category (Name) SELECT CONCAT(N'Category ', RIGHT(CONCAT('00', value), 2)) FROM GENERATE_SERIES(1, 49);
INSERT dbo.Warehouse (Code, Name, Contact, Notes, IsActive) SELECT CONCAT(N'WH0', value), CONCAT(N'Branch warehouse ', value), NULL, NULL, 1 FROM GENERATE_SERIES(1, 5);
INSERT dbo.Partner (Type, Name, Contact, IsActive) SELECT 1, CONCAT(N'Supplier ', value, N' Sdn Bhd'), NULL, 1 FROM GENERATE_SERIES(1, 40);
INSERT dbo.Partner (Type, Name, Contact, IsActive) SELECT 2, CONCAT(N'Customer ', value, N' Enterprise'), NULL, 1 FROM GENERATE_SERIES(1, 80);
INSERT dbo.[User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount)
SELECT u.n, CONCAT(u.n, N'@example.test'), CONCAT(N'User ', u.n), N'$2a$11$0000000000000000000000000000000000000000000000000000', u.r, 1, 0
FROM (VALUES (N'admin', N'Admin'), (N'manager', N'Manager'), (N'supervisor', N'Supervisor'), (N'staff', N'Staff'), (N'clerk', N'Clerk'),
             (N'viewer', N'Viewer'), (N'staff2', N'Staff'), (N'staff3', N'Staff'), (N'clerk2', N'Clerk'), (N'supervisor2', N'Supervisor'), (N'auditor', N'Viewer')) u (n, r);

-- products: names as in docs/dba/01-seed-perf.sql; key order = product number
SELECT value AS n, ABS(CHECKSUM(NEWID())) AS r1, ABS(CHECKSUM(NEWID())) AS r2, ABS(CHECKSUM(NEWID())) AS r3, ABS(CHECKSUM(NEWID())) AS r4 INTO #pr FROM GENERATE_SERIES(1, $(Products));
SELECT c.Id, ROW_NUMBER() OVER (ORDER BY c.Name) AS k INTO #cat FROM dbo.Category c; CREATE UNIQUE CLUSTERED INDEX ix ON #cat (k);
SELECT u.Id, ROW_NUMBER() OVER (ORDER BY u.Code) AS k INTO #uom FROM dbo.Uom u;     CREATE UNIQUE CLUSTERED INDEX ix ON #uom (k);

INSERT dbo.Product (Sku, Name, CategoryId, BaseUomId, SafetyStock, IsActive)
SELECT CONCAT(N'SKU-', RIGHT(CONCAT('00000', n), 5)),
       CONCAT(CHOOSE(1 + n % 8, N'Steel', N'Brass', N'Nylon', N'Copper', N'Zinc', N'Rubber', N'Plastic', N'Alloy'), N' ',
              CHOOSE(1 + (n / 8) % 10, N'bolt', N'nut', N'washer', N'bearing', N'gasket', N'valve', N'pipe', N'cable', N'switch', N'bracket'), N' ',
              CHOOSE(1 + (n / 80) % 6, N'M6', N'M8', N'M10', N'25mm', N'50mm', N'1 inch'), N' #', n),
       (SELECT c.Id FROM #cat c WHERE c.k = 1 + r1 % 49), (SELECT u.Id FROM #uom u WHERE u.k = 1 + r2 % 4),
       CASE WHEN r3 % 100 < 40 THEN 0 ELSE 20 + r4 % 1500 END, CASE WHEN r1 % 50 = 0 THEN 0 ELSE 1 END
FROM #pr ORDER BY n;

INSERT dbo.ProductBarcode (ProductId, Barcode)
SELECT p.Id, CONCAT(N'9557', RIGHT(CONCAT('000000000', CAST(SUBSTRING(p.Sku, 5, 10) AS int)), 9)) FROM dbo.Product p ORDER BY p.Sku;

INSERT dbo._Pick_Product (n, ProductId, UomId) SELECT CAST(SUBSTRING(Sku, 5, 10) AS int), Id, BaseUomId FROM dbo.Product;
INSERT dbo._Pick_Wh (n, WarehouseId) SELECT CAST(SUBSTRING(Code, 4, 5) AS int), Id FROM dbo.Warehouse;
INSERT dbo._Pick_User (n, UserId, Username) SELECT ROW_NUMBER() OVER (ORDER BY Username), Id, Username FROM dbo.[User];
INSERT dbo._Pick_Partner (n, PartnerId) SELECT ROW_NUMBER() OVER (ORDER BY Type, Name), Id FROM dbo.Partner;
-- one balance row per (product, warehouse) from the start, loaded in key order (the writers only update them: no lock cycles, no key races; 250k rows at 50000 products)
INSERT dbo.StockBalance (ProductId, WarehouseId, QtyOnHand, QtyInTransit, AvgCost)
SELECT p.Id, w.Id, 0, 0, 0 FROM dbo.Product p CROSS JOIN dbo.Warehouse w ORDER BY p.Id, w.Id;
DROP TABLE #pr, #cat, #uom;
UPDATE STATISTICS dbo.Product WITH FULLSCAN; UPDATE STATISTICS dbo.ProductBarcode WITH FULLSCAN; UPDATE STATISTICS dbo.StockBalance WITH FULLSCAN;
SELECT (SELECT COUNT(*) FROM dbo.Product) AS products, (SELECT COUNT(*) FROM dbo.ProductBarcode) AS barcodes, (SELECT COUNT(*) FROM dbo.[User]) AS users;

