module CompilerFixtures.InvalidSqlServerParent

open AspiringFarmer.PostgreSQL

let server = postgres "postgres" { data_volume }

let invalid =
    AspiringFarmer.SqlServer.Builders.database "appdb" server { configure ignore }
