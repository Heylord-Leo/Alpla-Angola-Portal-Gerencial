$connString = "Server=AOVIA1VMS012\SQLINNUX;Database=Innux;User Id=sa;Password=$($env:INNUX_DB_PASSWORD);TrustServerCertificate=True"
$conn = New-Object System.Data.SqlClient.SqlConnection($connString)
$conn.Open()

Write-Output "--- TCP Listener States ---"
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT port, ip_address, is_ipv4 FROM sys.dm_tcp_listener_states"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("Port: " + $reader["port"].ToString() + " | IP: " + $reader["ip_address"].ToString() + " | IPv4: " + $reader["is_ipv4"].ToString())
}
$reader.Close()
$conn.Close()
