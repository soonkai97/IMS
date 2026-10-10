/*
  13-movements-order-diff.sql -- which rows of the movements report change position when the order goes from
  (PostedAt DESC, line Id) to (PostedAt DESC, DocumentId DESC, line Id). Read-only; run it on any database before and after you trust the change.

    sqlcmd -S "(localdb)\ImsDev" -E -d Ims_Perf -W -i docs/dba/13-movements-order-diff.sql

  A row can only move when its document shares the exact PostedAt of another document: such documents now appear newest id first (before:
  by line id, which is mostly oldest document first). Lines of one document keep their order; documents with different PostedAt never swap.
*/
SET NOCOUNT ON;
WITH ordered AS (
    SELECT l.Id,
           ROW_NUMBER() OVER (ORDER BY d.PostedAt DESC, l.Id)                 AS old_position,
           ROW_NUMBER() OVER (ORDER BY d.PostedAt DESC, l.DocumentId DESC, l.Id) AS new_position
    FROM dbo.StockDocumentLine l
    JOIN dbo.StockDocument d ON d.Id = l.DocumentId AND d.REC_DELETED = 0
    WHERE l.REC_DELETED = 0 AND d.Status IN (3, 4)
)
SELECT COUNT(*) AS report_rows,
       SUM(CASE WHEN old_position <> new_position THEN 1 ELSE 0 END) AS rows_in_another_position
FROM ordered;

SELECT COUNT(*) AS posted_at_values_shared_by_documents, SUM(documents) AS documents_involved
FROM (SELECT COUNT(*) AS documents FROM dbo.StockDocument
      WHERE REC_DELETED = 0 AND Status IN (3, 4) GROUP BY PostedAt HAVING COUNT(*) > 1) AS ties;
