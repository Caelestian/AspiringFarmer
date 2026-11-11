open SharedPostgres

[<EntryPoint>]
let main args =
    Web.run args "BackOffice"
        [ "identity", "accounts"
          "inventory", "stock_items"
          "reporting", "reports" ]
    0
