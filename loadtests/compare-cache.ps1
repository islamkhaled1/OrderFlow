# Controlled comparison: identical parameters, Redis up then Redis stopped. Dev/verification DB only
# (it resets product stock). Requires a running compose stack.
#   $env:SA_PASSWORD = '<value of MSSQL_SA_PASSWORD from .env>'
#   $env:COMPOSE_NETWORK = 'orderflow-monitoring_default'   # optional; see `docker network ls`
$ErrorActionPreference = 'Stop'
if (-not $env:SA_PASSWORD) { throw 'Set SA_PASSWORD (the MSSQL_SA_PASSWORD from your .env).' }
$network = if ($env:COMPOSE_NETWORK) { $env:COMPOSE_NETWORK } else { 'orderflow-monitoring_default' }

docker exec orderflow-sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $env:SA_PASSWORD -d OrderFlowDb -Q "UPDATE Products SET StockQuantity = 1000000" | Out-Null

function RunK6($tag) {
  docker rm -f k6run 2>$null | Out-Null
  docker run -d --name k6run --network $network -e BASE_URL=http://api:8080 -e VUS=10 -e DURATION=20s -e PRODUCT_ID=4 -v "${PWD}\loadtests:/scripts" grafana/k6 run --summary-export=/scripts/k6-$tag.json /scripts/orders.js | Out-Null
  docker wait k6run | Out-Null
  docker logs k6run > "loadtests\k6-$tag.txt" 2>&1
  docker rm k6run | Out-Null
}

docker start orderflow-redis | Out-Null; Start-Sleep 5
RunK6 'redis-up'
docker stop orderflow-redis | Out-Null; Start-Sleep 3
RunK6 'redis-down'
docker start orderflow-redis | Out-Null
'DONE'
