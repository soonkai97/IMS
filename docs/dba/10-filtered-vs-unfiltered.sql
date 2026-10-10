/*
  10-filtered-vs-unfiltered.sql -- does a WHERE REC_DELETED = 0 FILTERED index match EF's  [x].[REC_DELETED] = CAST(0 AS bit)  predicate,
  and what does an UNFILTERED index cost when the query carries that predicate?  Ims_Perf only; creates and drops two scratch indexes.
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() <> N'Ims_Perf' THROW 50000, N'Ims_Perf only.', 1;
GO

DROP INDEX IF EXISTS IX_tmp_unf ON dbo.StockDocument; DROP INDEX IF EXISTS IX_tmp_fil ON dbo.StockDocument;
CREATE INDEX IX_tmp_unf ON dbo.StockDocument (Status, Type, Id DESC) INCLUDE (WarehouseId);
CREATE INDEX IX_tmp_fil ON dbo.StockDocument (Status, Type, Id DESC) INCLUDE (WarehouseId) WHERE REC_DELETED = 0;
GO
SET STATISTICS IO ON; SET STATISTICS TIME ON;
PRINT '### F1 COUNT status=3 type=2 + REC_DELETED=0, UNFILTERED index #1';
SELECT COUNT(*) FROM dbo.StockDocument s WITH (INDEX(IX_tmp_unf)) WHERE s.REC_DELETED = CAST(0 AS bit) AND s.Status = 3 AND s.Type = 2;
PRINT '### F1 COUNT status=3 type=2 + REC_DELETED=0, UNFILTERED index #2';
SELECT COUNT(*) FROM dbo.StockDocument s WITH (INDEX(IX_tmp_unf)) WHERE s.REC_DELETED = CAST(0 AS bit) AND s.Status = 3 AND s.Type = 2;
PRINT '### F2 COUNT status=3 type=2 + REC_DELETED=0, FILTERED index #1';
SELECT COUNT(*) FROM dbo.StockDocument s WITH (INDEX(IX_tmp_fil)) WHERE s.REC_DELETED = CAST(0 AS bit) AND s.Status = 3 AND s.Type = 2;
PRINT '### F2 COUNT status=3 type=2 + REC_DELETED=0, FILTERED index #2';
SELECT COUNT(*) FROM dbo.StockDocument s WITH (INDEX(IX_tmp_fil)) WHERE s.REC_DELETED = CAST(0 AS bit) AND s.Status = 3 AND s.Type = 2;
PRINT '### F3 COUNT status=3 type=2 + REC_DELETED=0, optimizer free choice #1';
SELECT COUNT(*) FROM dbo.StockDocument s WHERE s.REC_DELETED = CAST(0 AS bit) AND s.Status = 3 AND s.Type = 2;
PRINT '### F3 COUNT status=3 type=2 + REC_DELETED=0, optimizer free choice #2';
SELECT COUNT(*) FROM dbo.StockDocument s WHERE s.REC_DELETED = CAST(0 AS bit) AND s.Status = 3 AND s.Type = 2;
-- parameterised form: does the filtered index still match when REC_DELETED arrives as a variable? (EF emits a literal, but raw SQL might not)
PRINT '### F4 same, REC_DELETED as a parameter, FILTERED index (expect: no match -> lookups) #1';
EXEC sp_executesql N'SELECT COUNT(*) FROM dbo.StockDocument s WITH (INDEX(IX_tmp_fil)) WHERE s.REC_DELETED = @d AND s.Status = 3 AND s.Type = 2', N'@d bit', @d = 0;
PRINT '### F4 same, REC_DELETED as a parameter, FILTERED index (expect: no match -> lookups) #2';
EXEC sp_executesql N'SELECT COUNT(*) FROM dbo.StockDocument s WITH (INDEX(IX_tmp_fil)) WHERE s.REC_DELETED = @d AND s.Status = 3 AND s.Type = 2', N'@d bit', @d = 0;
SET STATISTICS IO OFF; SET STATISTICS TIME OFF;
GO
DROP INDEX IX_tmp_unf ON dbo.StockDocument; DROP INDEX IX_tmp_fil ON dbo.StockDocument;
