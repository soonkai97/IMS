/*
  15-repair-reversal-average.sql -- one-off repair of the average cost that the old receipt reversal left contaminated (PR-01).

  Before the fix, reversing a receipt took the quantity out but left the receipt's cost blended into the moving average, so the stock value and every later issue
  estimate of that product were too high (or too low). A product-warehouse pair is affected when (a) a receipt of it was reversed and (b) the value of the
  balance (QtyOnHand x AvgCost) no longer equals the value of its ledger (SUM(QtyDelta x UnitCost)): in the moving-average costing every receipt, issue and
  reversal moves the two together, so the ledger value is what the balance should be worth. The corrected average is that value divided by the quantity.

  What it does NOT touch: pairs that have no reversed receipt (a difference there has another cause, for example negative stock, where a receipt replaces the
  average on purpose), pairs with no stock or a non-positive ledger value (nothing sound to divide), and the ledger itself (append-only; issues that were booked at
  the contaminated average keep their cost, only the average of the stock that remains is corrected).

  READ THE REPORT BEFORE APPLYING. A pair whose stock was ever negative (negative stock allowed, an issue under override) may differ from its ledger value by design:
  a receipt onto zero or negative stock REPLACES the average instead of blending it. Such a difference is not the old bug and must not be "repaired": leave those
  pairs out (for a pair the report lists, check whether its running quantity ever went below zero) or restrict the update to the pairs you have checked.

  Usage (maintenance window, API stopped, database backed up):
    sqlcmd -S <server> -E -d <database> -b -v Apply=0 -i docs/dba/15-repair-reversal-average.sql     -- report only (default)
    sqlcmd -S <server> -E -d <database> -b -v Apply=1 -i docs/dba/15-repair-reversal-average.sql     -- report, then update inside one transaction
  Check the report first: Tolerance is 0.01 currency units on the value of the pair.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
DECLARE @apply bit = $(Apply), @tolerance decimal(18, 4) = 0.01;

IF OBJECT_ID('tempdb..#affected') IS NOT NULL DROP TABLE #affected;

SELECT b.ProductId, b.WarehouseId, b.QtyOnHand, b.AvgCost AS AvgCostNow,
       l.LedgerValue,
       CAST(l.LedgerValue / b.QtyOnHand AS decimal(22, 10)) AS AvgCostRepaired,
       b.QtyOnHand * b.AvgCost - l.LedgerValue AS ValueTooHigh
INTO #affected
FROM dbo.StockBalance b
JOIN (SELECT ProductId, WarehouseId, SUM(QtyDelta * UnitCost) AS LedgerValue FROM dbo.StockLedgerEntry GROUP BY ProductId, WarehouseId) l
  ON l.ProductId = b.ProductId AND l.WarehouseId = b.WarehouseId
WHERE b.REC_DELETED = 0
  AND b.QtyOnHand > 0 AND l.LedgerValue > 0
  AND ABS(b.QtyOnHand * b.AvgCost - l.LedgerValue) > @tolerance
  AND EXISTS (SELECT 1 FROM dbo.StockLedgerEntry r
              JOIN dbo.StockDocument d ON d.Id = r.SourceDocId
              WHERE r.ProductId = b.ProductId AND r.WarehouseId = b.WarehouseId AND r.Reason = 6 AND d.[Type] = 1);

SELECT COUNT(*) AS PairsAffected, SUM(ValueTooHigh) AS TotalValueTooHigh FROM #affected;
SELECT TOP (200) a.ProductId, p.Sku, a.WarehouseId, a.QtyOnHand, a.AvgCostNow, a.AvgCostRepaired, a.ValueTooHigh
FROM #affected a JOIN dbo.Product p ON p.Id = a.ProductId
ORDER BY ABS(a.ValueTooHigh) DESC;

IF @apply = 1
BEGIN
    BEGIN TRANSACTION;
    UPDATE b SET b.AvgCost = a.AvgCostRepaired, b.REC_REVDATE = SYSUTCDATETIME()
    FROM dbo.StockBalance b JOIN #affected a ON a.ProductId = b.ProductId AND a.WarehouseId = b.WarehouseId;
    PRINT CONCAT('updated ', @@ROWCOUNT, ' balances');
    COMMIT TRANSACTION;
END
ELSE PRINT 'Report only. Run again with -v Apply=1 to update.';
DROP TABLE #affected;
