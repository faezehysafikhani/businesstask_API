# Operations: backup, restore and upgrades

## What holds data
| What | Where | Backed up by |
|---|---|---|
| All data (users, tasks, chat, meetings, workflow ...) | **one SQL Server database** (several schemas) | SQL Server backup |
| Uploaded files (task attachments, chat files) | folder `FileStorage:RootPath` | file copy of that folder |
| Secrets and settings | `appsettings.Production.json` / environment variables (`ConnectionStrings`, `Jwt:SigningKey`, `Identity:AdminPassword`, `Email:Smtp`) | your secret store - **not** in the database backup |
| Encryption keys for stored credentials (SMS panel, LDAP bind password) | ASP.NET Core Data Protection key ring (`DataProtection` folder or its configured store) | file copy of the key folder |

> Without the Data Protection key ring a restored database cannot decrypt the SMS and LDAP passwords: they must be typed in again.
> Keep the key ring in the backup set.

## Backup (SQL Server)
Full backup every night, differential every 4 hours, log backup every 15 minutes if the recovery model is FULL:

```sql
BACKUP DATABASE [TM] TO DISK = N'D:\Backups\TM_full.bak' WITH COMPRESSION, CHECKSUM, INIT;
BACKUP DATABASE [TM] TO DISK = N'D:\Backups\TM_diff.bak' WITH DIFFERENTIAL, COMPRESSION, CHECKSUM, INIT;
BACKUP LOG [TM] TO DISK = N'D:\Backups\TM_log.trn' WITH COMPRESSION, CHECKSUM;
```
Copy the `.bak` files and the **files folder and key ring** to a second machine or site. A backup that lives only next to the database is not a backup.

## Restore
1. Stop the host (so nothing writes while restoring).
2. Restore the database:
```sql
RESTORE DATABASE [TM] FROM DISK = N'D:\Backups\TM_full.bak' WITH REPLACE, NORECOVERY;
RESTORE DATABASE [TM] FROM DISK = N'D:\Backups\TM_diff.bak' WITH NORECOVERY;
RESTORE LOG [TM] FROM DISK = N'D:\Backups\TM_log.trn' WITH RECOVERY;     -- or: RESTORE DATABASE [TM] WITH RECOVERY;
```
3. Restore the files folder and the Data Protection key ring to the same paths.
4. Start the host. It adds any newer columns/tables itself (the `docs/upgrade` scripts' statements run at start-up), so a backup taken on an older version restores onto a newer host.
5. Check: sign in, open a task with an attachment, send a test SMS from the SMS panel.

## Restore drill (do it twice a year)
Restore last night's backup onto a spare machine, start the host there with a copy of the configuration, and confirm sign-in and an attachment download.
A backup never restored is a hope, not a backup.

## Upgrades
* Production: set `Database:SeedOnStartup=false` and apply `docs/upgrade/*.sql` in file-name order with `sqlcmd` (each is safe to run again).
* Take a full backup **before** every upgrade.
* New Core package versions come as a folder of `.nupkg` files: replace the `*-ux.nupkg` files in `packages/` and rebuild.

## Retention and size
Logs of the automation rules and webhook deliveries can be limited per organization (Plan and usage page → retention). Task data is never removed automatically.
