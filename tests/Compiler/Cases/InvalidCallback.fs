module CompilerFixtures.InvalidCallback

open Aspire.Hosting.ApplicationModel
open AspiringFarmer.Redis

let projectOnly (_: IResourceBuilder<ProjectResource>) = ()

let invalid = redis "cache" { configure projectOnly }
