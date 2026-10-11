/*
  09-create-db.sql -- creates one scratch database for one key strategy. Variables: Db (must start with Ims_PerfGuid), DataDir (existing folder).
  Same settings for every strategy: READ_COMMITTED_SNAPSHOT ON (as the product), recovery SIMPLE while loading, data file growth 256 MB.
*/
SET NOCOUNT ON;
IF N'$(Db)' NOT LIKE N'Ims[_]PerfGuid%' THROW 50000, N'Scratch databases only: the name must start with Ims_PerfGuid.', 1;
IF DB_ID(N'$(Db)') IS NOT NULL THROW 50001, N'Database exists already; drop the scratch database first.', 1;
CREATE DATABASE [$(Db)] ON PRIMARY (NAME = N'$(Db)', FILENAME = N'$(DataDir)\$(Db).mdf', SIZE = 512MB, FILEGROWTH = 256MB)
                       LOG ON (NAME = N'$(Db)_log', FILENAME = N'$(DataDir)\$(Db)_log.ldf', SIZE = 256MB, FILEGROWTH = 256MB);
ALTER DATABASE [$(Db)] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [$(Db)] SET RECOVERY SIMPLE;
ALTER DATABASE [$(Db)] SET AUTO_CLOSE OFF;
