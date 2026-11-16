-- VPS-02: explicit opt-in, run with SQLCMD :setvar WebRuntimeUser "existing_runtime_user"
-- after Provision-MyDmsVnWebPrincipals.sql and on a disposable, approved schema >= 9.
-- This script never creates a login/user, never grants schema-wide DML or security-table DML.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb')
    THROW 51110, 'Desktop API grants require an application database.', 1;
IF USER_ID(N'$(WebRuntimeUser)') IS NULL
    THROW 51111, 'An existing restricted Web runtime principal is required.', 1;
IF OBJECT_ID(N'dbo.ApplySecurityAdministration',N'P') IS NULL
    OR OBJECT_ID(N'dbo.ReadSecurityPermissionImportTarget',N'P') IS NULL
    OR OBJECT_ID(N'dbo.ApplySecurityPermissionImport',N'P') IS NULL
    THROW 51112, 'Security stored procedures are missing.', 1;
IF DATABASE_PRINCIPAL_ID(N'MyDmsVnDesktopApiRuntimeRole') IS NULL
    CREATE ROLE [MyDmsVnDesktopApiRuntimeRole] AUTHORIZATION dbo;
IF ISNULL(IS_ROLEMEMBER(N'MyDmsVnDesktopApiRuntimeRole',N'$(WebRuntimeUser)'),0)<>1
    ALTER ROLE [MyDmsVnDesktopApiRuntimeRole] ADD MEMBER [$(WebRuntimeUser)];
GRANT INSERT,UPDATE ON OBJECT::dbo.Products TO [MyDmsVnDesktopApiRuntimeRole];
GRANT INSERT,UPDATE ON OBJECT::dbo.Warehouses TO [MyDmsVnDesktopApiRuntimeRole];
GRANT INSERT,UPDATE ON OBJECT::dbo.Employees TO [MyDmsVnDesktopApiRuntimeRole];
GRANT INSERT,UPDATE ON OBJECT::dbo.Customers TO [MyDmsVnDesktopApiRuntimeRole];
GRANT EXECUTE ON OBJECT::dbo.ApplySecurityAdministration TO [MyDmsVnDesktopApiRuntimeRole];
GRANT EXECUTE ON OBJECT::dbo.ReadSecurityPermissionImportTarget TO [MyDmsVnDesktopApiRuntimeRole];
GRANT EXECUTE ON OBJECT::dbo.ApplySecurityPermissionImport TO [MyDmsVnDesktopApiRuntimeRole];
-- Startup with MYDMSVN_WEB_DESKTOP_API=enabled performs independent positive AND negative privilege checks.
