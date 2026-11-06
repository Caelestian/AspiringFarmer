module CompilerFixtures.InvalidReference

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

let parameter =
    ResourceBuilder<ParameterResource>("setting", fun builder -> builder.AddParameter("setting")) { configure ignore }

let invalid = project "api" "Api.fsproj" { reference parameter }
