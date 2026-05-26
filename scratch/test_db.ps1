$connString = "Server=AOVIA1VMS012\SQLALPLA;Database=PRI297514001;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True"
$conn = New-Object System.Data.SqlClient.SqlConnection($connString)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT top 1 * FROM dbo.Departamentos"
$reader = $cmd.ExecuteReader()
$cols = @()
for ($i=0; $i -lt $reader.FieldCount; $i++) { $cols += $reader.GetName($i) }
$cols
$conn.Close()
