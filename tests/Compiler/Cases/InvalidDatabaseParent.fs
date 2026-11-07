module CompilerFixtures.InvalidDatabaseParent

open AspiringFarmer.PostgreSQL
open AspiringFarmer.Redis

let cache = redis "cache" { data_volume }
let invalid = database "appdb" cache { configure ignore }
