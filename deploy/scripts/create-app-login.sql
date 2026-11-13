/*
  create-app-login.sql -- the least-privilege login the API connects with (go-live gate, docs/architecture/deployment.md section 6).
  Run ONCE, after the first migration has created the Ims database, as sa, by hand. The password is NEVER in this file or on a command line:
  it is a sqlcmd scripting variable that sqlcmd reads from the environment variable APP_PASSWORD (letters and digits only), set for this one command:

    read -rs APP_PASSWORD && export APP_PASSWORD
    docker compose --env-file deploy/.env.prod -f deploy/compose.prod.yml cp deploy/scripts/create-app-login.sql db:/var/opt/mssql/backup/create-app-login.sql
    docker compose --env-file deploy/.env.prod -f deploy/compose.prod.yml exec -T -e APP_PASSWORD db \
      /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -i /var/opt/mssql/backup/create-app-login.sql
    unset APP_PASSWORD

  Then put IMS_DB_USER=ims_app and IMS_APP_DB_PASSWORD=<the same value> in deploy/.env.prod and redeploy. sa stays for the DBA script and the backups only.
  The login may read, write and execute; it cannot change the schema, so migrations (and IMS_MIGRATE_ON_STARTUP) need sa.
  NOT VERIFIED without a host: that the API works with exactly these rights (application locks via sp_getapplock, the append-only trigger on the ledger, the seeder).
  Run the smoke check and a posting by hand with this login before real data; if something is refused, add the narrowest grant that fixes it and record it here.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF SUSER_ID(N'ims_app') IS NOT NULL THROW 50001, N'The login ims_app exists already. Change its password by hand (ALTER LOGIN) or drop it first.', 1;
CREATE LOGIN [ims_app] WITH PASSWORD = N'$(APP_PASSWORD)', CHECK_POLICY = ON, DEFAULT_DATABASE = [Ims];
GO
USE [Ims];
GO
CREATE USER [ims_app] FOR LOGIN [ims_app];
ALTER ROLE db_datareader ADD MEMBER [ims_app];
ALTER ROLE db_datawriter ADD MEMBER [ims_app];
GRANT EXECUTE TO [ims_app];
GO
SELECT N'ims_app created with db_datareader, db_datawriter and EXECUTE on Ims' AS result;
