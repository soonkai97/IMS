/*
  16-check-subcent-costs.sql -- before the money-precision change (migration StoredLineAmount): which open documents carry a unit cost with a fraction of a cent?

  From that change a unit cost typed on a receipt has at most 2 decimals. Lines saved earlier may hold more (12.345, or an average taken for a staff receipt).
  The migration flags these open lines CostFromAverage and keeps their exact cost per base unit and their amount; the API shows no editable unitCost for them
  (null), and saving the draft keeps the price. Without the flag a cost per base unit below a cent would backfill EnteredUnitCost as 0.00 and the price would be
  lost on the next save. This read-only query lists the open (Draft, Submitted, Approved) receipt lines affected, so a person can retype a real cost if wanted.
  Posted and reversed documents are history and are not listed.

    sqlcmd -S "(localdb)\ImsDev" -E -d <database> -W -b -i docs/dba/16-check-subcent-costs.sql

  Status values: 0 Draft, 1 Submitted, 2 Approved. The cost per entered unit is the stored cost per base unit spread back over the entered quantity.
*/
SET NOCOUNT ON;
SELECT d.No, d.Status, l.Id AS LineId, p.Sku, l.Qty, l.BaseQty, l.UnitCost AS CostPerBaseUnit,
       CAST(l.UnitCost * l.BaseQty / NULLIF(l.Qty, 0) AS decimal(22, 8)) AS CostPerEnteredUnit
FROM dbo.StockDocumentLine l
JOIN dbo.StockDocument d ON d.Id = l.DocumentId AND d.REC_DELETED = 0
JOIN dbo.Product p ON p.Id = l.ProductId
WHERE l.REC_DELETED = 0 AND d.[Type] = 1 AND d.Status IN (0, 1, 2) AND l.Qty > 0
  AND (ABS(l.UnitCost * l.BaseQty / l.Qty - ROUND(l.UnitCost * l.BaseQty / l.Qty, 2)) > 0.00005   -- more than a rounding artefact of the unit conversion
       OR (l.UnitCost <> 0 AND ROUND(l.UnitCost * l.BaseQty / l.Qty, 2) = 0))               -- a cost below half a cent rounds to a typed zero
ORDER BY d.No, l.Id;
SELECT @@ROWCOUNT AS LinesWithAFractionOfACent;
