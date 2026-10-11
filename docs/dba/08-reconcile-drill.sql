/*
  08-reconcile-drill.sql -- recovery drill: can StockBalance be verified / rebuilt from the append-only ledger, and how long does it take?
    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -W -b -i docs/dba/08-reconcile-drill.sql
  1. VERIFY: compare SUM(ledger) per (product, warehouse) with StockBalance.QtyOnHand (read-only; returns the number of mismatches, expected 0).
  3. BALANCE AFTER (last row): the BalanceAfter of the newest ledger row of every (product, warehouse) equals StockBalance.QtyOnHand (expected 0 mismatches).
  4. BALANCE AFTER (every row): each row's BalanceAfter equals the running sum of its (product, warehouse) up to and including it (expected 0 mismatches).
  2. REBUILD (dry run): recompute quantity and weighted-average cost of every balance into a temp table (nothing is written to StockBalance).
  The real rebuild would be: BEGIN TRAN; lock the table; MERGE from the temp table (or UPDATE ... FROM) ; COMMIT during a maintenance window.
  AvgCost here is the receipt-weighted average used by the seed; the production formula is the posting engine's moving average, so for a
  real rebuild replay the ledger in Id order (cursor / SQLCLR / app job) instead.
*/
SET NOCOUNT ON;
DECLARE @t0 datetime2 = SYSDATETIME(), @n bigint;

SELECT @n = COUNT(*) FROM (
    SELECT ProductId, WarehouseId, SUM(QtyDelta) AS q FROM dbo.StockLedgerEntry GROUP BY ProductId, WarehouseId) l
FULL JOIN dbo.StockBalance b ON b.ProductId = l.ProductId AND b.WarehouseId = l.WarehouseId
WHERE ISNULL(b.QtyOnHand, -1) <> ISNULL(l.q, -2);
PRINT CONCAT('1. verify SUM(ledger) vs balance: ', @n, ' mismatches, ', DATEDIFF(MILLISECOND, @t0, SYSDATETIME()), ' ms');

SET @t0 = SYSDATETIME();
SELECT ProductId, WarehouseId, SUM(QtyDelta) AS QtyOnHand,
       CAST(ISNULL(SUM(CASE WHEN QtyDelta > 0 THEN QtyDelta * UnitCost END) / NULLIF(SUM(CASE WHEN QtyDelta > 0 THEN QtyDelta END), 0), 0) AS decimal(22, 10)) AS AvgCost
INTO #rebuilt FROM dbo.StockLedgerEntry GROUP BY ProductId, WarehouseId;
SELECT @n = COUNT(*) FROM #rebuilt;
PRINT CONCAT('2. rebuild into temp table: ', @n, ' balance rows, ', DATEDIFF(MILLISECOND, @t0, SYSDATETIME()), ' ms');
DROP TABLE #rebuilt;

SET @t0 = SYSDATETIME();
SELECT @n = COUNT(*) FROM (SELECT ProductId, WarehouseId, MAX(Id) AS LastId FROM dbo.StockLedgerEntry GROUP BY ProductId, WarehouseId) m
JOIN dbo.StockLedgerEntry l ON l.Id = m.LastId
FULL JOIN dbo.StockBalance b ON b.ProductId = m.ProductId AND b.WarehouseId = m.WarehouseId
WHERE ISNULL(l.BalanceAfter, -1) <> ISNULL(b.QtyOnHand, -2);
PRINT CONCAT('3. last BalanceAfter vs balance: ', @n, ' mismatches, ', DATEDIFF(MILLISECOND, @t0, SYSDATETIME()), ' ms');

SET @t0 = SYSDATETIME();
SELECT @n = COUNT(*) FROM (
    SELECT BalanceAfter, SUM(QtyDelta) OVER (PARTITION BY ProductId, WarehouseId ORDER BY Id ROWS UNBOUNDED PRECEDING) AS Running FROM dbo.StockLedgerEntry) x
WHERE x.BalanceAfter <> x.Running;
PRINT CONCAT('4. every BalanceAfter vs the running sum: ', @n, ' mismatches, ', DATEDIFF(MILLISECOND, @t0, SYSDATETIME()), ' ms');
