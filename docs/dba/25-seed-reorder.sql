/*
  25-seed-reorder.sql -- open purchase orders for the reorder timing (README section 13). Run on a RESTORED COPY of Ims_PerfPid1M (name Ims_Perf*) that has been
  brought to the current migration, with the purchasing module on:
    INSERT SystemSetting ([Key],[Value]) VALUES (N'Module.purchasing', N'true');
  Run it with sqlcmd -I (the filtered indexes need QUOTED_IDENTIFIER ON) -b -d <copy> -i docs/dba/25-seed-reorder.sql.
  Plan figures: 1,000 open order lines. Here: 200 orders of 5 lines, 40 per warehouse, status Draft / Submitted / Approved in turn, on products that are LOW in the
  warehouse (so every order line meets a suggestion row), 3 lines of the same product on some orders; and 100 Posted receipts of the 5 warehouses linked to Approved orders
  of the same warehouse (every line of the receipt names order line 1), so the posted-quantity subquery has rows to find. Nothing here is a real order.
*/
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE 'Ims[_]Perf%' THROW 50000, 'A perf database only.', 1;
IF EXISTS (SELECT 1 FROM PurchaseOrder) THROW 50001, 'Orders exist already: use a fresh restore.', 1;

DECLARE @admin int = (SELECT Id FROM [User] WHERE Username = N'admin');
DECLARE @supplier int = (SELECT TOP 1 Id FROM Partner WHERE Type = 1 AND REC_DELETED = 0 ORDER BY Id);

DROP TABLE IF EXISTS #orders;
CREATE TABLE #orders (OrderId int, WarehouseId int, N int);

-- 40 orders per warehouse
INSERT PurchaseOrder (No, Status, WarehouseId, SupplierId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, REC_DELETED)
OUTPUT inserted.Id, inserted.WarehouseId, CAST(RIGHT(inserted.No, 3) AS int) INTO #orders (OrderId, WarehouseId, N)
SELECT CONCAT(N'PO-PERF-', w.Id, N'-', RIGHT(CONCAT(N'00', n.N), 3)), n.N % 3, w.Id, @supplier, @admin, SYSUTCDATETIME(), 0, 0, 0
FROM Warehouse w CROSS JOIN (SELECT TOP 40 ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS N FROM sys.all_objects) n;

-- 5 lines per order, on the low products of its warehouse (the n-th order takes products 5n-4 .. 5n of the warehouse's low list)
;WITH low AS (
    SELECT b.WarehouseId, b.ProductId, p.BaseUomId, ROW_NUMBER() OVER (PARTITION BY b.WarehouseId ORDER BY b.ProductId) AS Rn
    FROM StockBalance b JOIN Product p ON p.Id = b.ProductId
    WHERE p.REC_DELETED = 0 AND p.IsActive = 1 AND (b.QtyOnHand < 0 OR (p.SafetyStock > 0 AND b.QtyOnHand <= p.SafetyStock))
), lines AS (
    SELECT o.OrderId, l.Rn - (o.N - 1) * 5 AS [LineNo], l.ProductId, l.BaseUomId
    FROM #orders o JOIN low l ON l.WarehouseId = o.WarehouseId AND l.Rn BETWEEN (o.N - 1) * 5 + 1 AND o.N * 5
)
INSERT PurchaseOrderLine (PurchaseOrderId, [LineNo], ProductId, Qty, UomId, BaseQty, UnitPrice, Amount, REC_DELETED)
SELECT OrderId, [LineNo], ProductId, 10 + (ProductId % 40), BaseUomId, 10 + (ProductId % 40), 1, 10 + (ProductId % 40), 0 FROM lines;

-- the order status is the n % 3 chosen above (0 Draft, 1 Submitted, 2 Approved); Approved orders carry an approver
UPDATE PurchaseOrder SET ApprovedBy = @admin, ApprovedAt = SYSUTCDATETIME(), SubmittedBy = @admin, SubmittedAt = SYSUTCDATETIME() WHERE Status = 2;

-- 100 Posted receipts (20 per warehouse) linked to Approved orders of their warehouse, every line naming order line 1
;WITH approved AS (
    SELECT Id, WarehouseId, ROW_NUMBER() OVER (PARTITION BY WarehouseId ORDER BY Id) AS Rn FROM PurchaseOrder WHERE Status = 2
), receipts AS (
    SELECT d.Id, d.WarehouseId, ROW_NUMBER() OVER (PARTITION BY d.WarehouseId ORDER BY d.Id) AS Rn
    FROM StockDocument d WHERE d.Type = 1 AND d.Status = 3 AND d.REC_DELETED = 0 AND d.PurchaseOrderId IS NULL
)
UPDATE d SET PurchaseOrderId = a.Id
FROM StockDocument d JOIN receipts r ON r.Id = d.Id JOIN approved a ON a.WarehouseId = r.WarehouseId AND a.Rn = (r.Rn - 1) % 13 + 1
WHERE r.Rn <= 20;
UPDATE l SET PurchaseOrderLineNo = 1 FROM StockDocumentLine l JOIN StockDocument d ON d.Id = l.DocumentId WHERE d.PurchaseOrderId IS NOT NULL;

SELECT 'orders' AS What, Status, COUNT(*) AS N FROM PurchaseOrder GROUP BY Status
UNION ALL SELECT 'order lines', NULL, COUNT(*) FROM PurchaseOrderLine
UNION ALL SELECT 'linked receipts', NULL, COUNT(*) FROM StockDocument WHERE PurchaseOrderId IS NOT NULL
UNION ALL SELECT 'linked receipt lines', NULL, COUNT(*) FROM StockDocumentLine WHERE PurchaseOrderLineNo IS NOT NULL;
