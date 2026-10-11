/*
  16-check-subcent-costs.sql -- before the money-precision change (migration StoredLineAmount): which open documents carry a unit cost with a fraction of a cent?

  From that change a unit cost typed on a receipt has at most 2 decimals. Lines saved earlier may hold more (12.345). The migration keeps their value (the amount is
  computed from the exact cost) but the API then shows the cost per ENTERED unit rounded to 2 decimals, so opening and saving such a draft changes its price by up
  to half a cent per unit. This read-only query lists the open (Draft, Submitted, Approved) receipt lines affected, so they can be corrected first.
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
  AND ABS(l.UnitCost * l.BaseQty / l.Qty - ROUND(l.UnitCost * l.BaseQty / l.Qty, 2)) > 0.0005   -- more than a rounding artefact of the unit conversion
ORDER BY d.No, l.Id;
SELECT @@ROWCOUNT AS LinesWithAFractionOfACent;
