module CompilerFixtures.InvalidWait

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

let parameter =
    ResourceBuilder<ParameterResource>("setting", fun builder -> builder.AddParameter("setting")) { configure ignore }

let invalid = project "api" "Api.fsproj" { wait_for parameter }
