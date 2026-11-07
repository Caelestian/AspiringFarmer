module CompilerFixtures.InvalidCompletionWait

open AspiringFarmer
open AspiringFarmer.PostgreSQL

let server = postgres "postgres" { data_volume }
let db = database "appdb" server { configure ignore }

let invalid = project "api" "Api.fsproj" { wait_for_completion db }
