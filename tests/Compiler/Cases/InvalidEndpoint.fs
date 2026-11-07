module CompilerFixtures.InvalidEndpoint

open AspiringFarmer

let invalid = endpoint "http" (secretParameter "password")
