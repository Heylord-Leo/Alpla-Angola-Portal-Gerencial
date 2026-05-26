$connString = 'Server=(localdb)\mssqllocaldb;Database=AlplaPortal;Trusted_Connection=True'
$conn = New-Object System.Data.SqlClient.SqlConnection($connString)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = 'SELECT TOP 5 Id, DepartmentCode, DepartmentName, CompanyCode FROM [DepartmentMasters]'
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output "$($reader[0]) | $($reader[1]) | $($reader[2]) | $($reader[3])"
}
$conn.Close()
