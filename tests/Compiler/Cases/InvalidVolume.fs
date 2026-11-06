module CompilerFixtures.InvalidVolume

open AspiringFarmer

let invalid =
    project "api" "Api.fsproj" {
        configure ignore
        data_volume
    }
