# ═══════════════════════════════════════════════════════════════════
# DEPRECATED: Historical one-time migration script. Do NOT re-run.
# Applied: corrected ProductVersion for AddFinancialSnapshotAndPaymentFields.
# The target database AlplaPortalV1 has been decommissioned.
# The canonical dev database is Portal-Gerencial-Dev-ProdClone.
# Schema changes must use EF Core migrations:
#   execution/update_dev_database.ps1 -Apply -Confirmation 'APPLY-MIGRATIONS-TO-DEV-CLONE'
# ═══════════════════════════════════════════════════════════════════
Write-Error "[BLOCKED] This script is deprecated and must not be executed. Target database AlplaPortalV1 has been decommissioned. Use EF Core migrations." -ErrorAction Stop

# --- Original code preserved below for historical reference ---
# $conn = New-Object System.Data.SqlClient.SqlConnection("Server=(localdb)\MSSQLLocalDB;Database=AlplaPortalV1;Trusted_Connection=True;")
# $conn.Open()
# $cmd = $conn.CreateCommand()
# $cmd.CommandText = "UPDATE [__EFMigrationsHistory] SET ProductVersion = '8.0.2' WHERE MigrationId = '20260417081700_AddFinancialSnapshotAndPaymentFields'"
# $rows = $cmd.ExecuteNonQuery()
# Write-Host "Updated $rows row(s). ProductVersion corrected to 8.0.2."
# $conn.Close()
