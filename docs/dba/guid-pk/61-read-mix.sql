/*
  61-read-mix.sql -- one reader session of the "shift" workload (see 60-buffer-pool.ps1): $(Ops) read operations drawn from the hot-query mix with
  Zipf-skewed products and recent-biased documents. Same statements for every key strategy (no key literals: keys are looked up in the statement).
  Variables: Ops, Seed (different per session).
  mix: 25% stock by SKU, 10% stock by barcode, 15% ledger first page of a product, 10% documents by status/type, 15% document with lines (recent),
       5% product list by name, 5% audit by actor, 5% movements 7 days, 5% documents by date, 5% document by id.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
IF DB_NAME() NOT LIKE N'Ims[_]PerfGuid%' THROW 50000, N'Scratch databases only.', 1;
DECLARE @seed bigint = $(Seed), @i int = 0, @ops int = $(Ops), @r int, @u float, @n int, @k int, @sql nvarchar(max), @sku nvarchar(20), @mp datetime2 = (SELECT MAX(PostedAt) FROM dbo.StockDocument);
DECLARE @products int = (SELECT COUNT(*) FROM dbo._Pick_Product);
WHILE @i < @ops
BEGIN
    SET @seed = (@seed * 1103515245 + 12345) % 2147483648; SET @r = @seed % 100;
    SET @seed = (@seed * 1103515245 + 12345) % 2147483648; SET @u = (@seed % 1000000) / 1000000.0;
    SET @n = 1 + CAST(FLOOR(@products * POWER(@u, 2)) AS int);                         -- Zipf-like product number
    SET @sku = CONCAT(N'SKU-', RIGHT(CONCAT('00000', @n), 5));
    SET @k = CAST(FLOOR(3000 * POWER(@u, 3)) AS int);                                  -- k-th newest document
    SET @sql = CASE
      WHEN @r < 25 THEN CONCAT(N'SELECT p.Id, p.Sku, p.Name, b.WarehouseId, b.QtyOnHand FROM dbo.Product p LEFT JOIN dbo.StockBalance b ON b.ProductId = p.Id AND b.REC_DELETED = 0 WHERE p.REC_DELETED = 0 AND p.Sku = N''', @sku, N'''')
      WHEN @r < 35 THEN CONCAT(N'SELECT p.Id, p.Sku, p.Name, b.WarehouseId, b.QtyOnHand FROM dbo.ProductBarcode pb JOIN dbo.Product p ON p.Id = pb.ProductId LEFT JOIN dbo.StockBalance b ON b.ProductId = p.Id AND b.REC_DELETED = 0 WHERE pb.REC_DELETED = 0 AND pb.Barcode = N''9557', RIGHT(CONCAT('000000000', @n), 9), N'''')
      WHEN @r < 50 THEN CONCAT(N'SELECT TOP (50) e.Id, e.WarehouseId, e.QtyDelta, e.UnitCost, e.SourceDocId, e.OccurredAt FROM dbo.StockLedgerEntry e WHERE e.ProductId = (SELECT p.Id FROM dbo.Product p WHERE p.REC_DELETED = 0 AND p.Sku = N''', @sku, N''') ORDER BY e.Id DESC')
      WHEN @r < 60 THEN CONCAT(N'SELECT TOP (50) d.Id, d.No, d.Type, d.Status, d.WarehouseId, d.CreatedAt FROM dbo.StockDocument d WHERE d.REC_DELETED = 0 AND d.Status = 3 AND d.Type = ', 1 + @seed % 2, N' ORDER BY d.Id DESC')
      WHEN @r < 75 THEN CONCAT(N'SELECT d.Id, d.No, d.Status, l.Id, l.ProductId, p.Sku, p.Name, l.Qty, l.UnitCost FROM dbo.StockDocument d JOIN dbo.StockDocumentLine l ON l.DocumentId = d.Id JOIN dbo.Product p ON p.Id = l.ProductId WHERE d.Id = (SELECT d2.Id FROM dbo.StockDocument d2 WHERE d2.REC_DELETED = 0 ORDER BY d2.CreatedAt DESC, d2.Id DESC OFFSET ', @k, N' ROWS FETCH NEXT 1 ROWS ONLY) AND l.REC_DELETED = 0')
      WHEN @r < 80 THEN CONCAT(N'SELECT p.Id, p.Sku, p.Name, p.IsActive FROM dbo.Product p WHERE p.REC_DELETED = 0 ORDER BY p.Name, p.Id OFFSET ', 50 * (@k % 100), N' ROWS FETCH NEXT 50 ROWS ONLY')
      WHEN @r < 85 THEN CONCAT(N'SELECT TOP (50) a.Id, a.Actor, a.Action, a.EntityType, a.EntityId, a.[At] FROM dbo.AuditLogs a WHERE a.Actor = N''', CHOOSE(1 + @seed % 5, N'clerk', N'staff', N'supervisor', N'staff2', N'admin'), N''' ORDER BY a.Id DESC')
      WHEN @r < 90 THEN CONCAT(N'SELECT l.Id, d.PostedAt, d.No, d.Type, l.ProductId, p.Sku, l.Qty, l.BaseQty, l.UnitCost FROM dbo.StockDocumentLine l JOIN dbo.StockDocument d ON d.Id = l.DocumentId JOIN dbo.Product p ON p.Id = l.ProductId WHERE l.REC_DELETED = 0 AND d.REC_DELETED = 0 AND d.Status = 3 AND d.PostedAt IS NOT NULL AND d.PostedAt >= ''', CONVERT(nvarchar(40), DATEADD(DAY, -7, @mp), 126), N''' ORDER BY d.PostedAt DESC, d.Id DESC, l.Id OFFSET ', 50 * (@k % 20), N' ROWS FETCH NEXT 50 ROWS ONLY')
      WHEN @r < 95 THEN CONCAT(N'SELECT d.Id, d.No, d.Type, d.Status, d.WarehouseId, d.CreatedAt FROM dbo.StockDocument d WHERE d.REC_DELETED = 0 AND d.CreatedAt >= ''', CONVERT(nvarchar(40), DATEADD(DAY, -1 - (@k % 30), @mp), 126), N''' AND d.CreatedAt < ''', CONVERT(nvarchar(40), DATEADD(DAY, -(@k % 30), @mp), 126), N''' ORDER BY d.CreatedAt DESC, d.Id DESC OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY')
      ELSE CONCAT(N'SELECT d.* FROM dbo.StockDocument d WHERE d.Id = (SELECT d2.Id FROM dbo.StockDocument d2 WHERE d2.REC_DELETED = 0 ORDER BY d2.CreatedAt DESC, d2.Id DESC OFFSET ', @k, N' ROWS FETCH NEXT 1 ROWS ONLY)') END;
    EXEC (@sql);
    SET @i += 1;
END
