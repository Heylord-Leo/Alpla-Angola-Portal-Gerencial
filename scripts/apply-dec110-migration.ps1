$conn = New-Object System.Data.SqlClient.SqlConnection("Server=(localdb)\MSSQLLocalDB;Database=AlplaPortalV1;Trusted_Connection=True;")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE [__EFMigrationsHistory] SET ProductVersion = '8.0.2' WHERE MigrationId = '20260417081700_AddFinancialSnapshotAndPaymentFields'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "Updated $rows row(s). ProductVersion corrected to 8.0.2."
$conn.Close()
