open SharedPostgres

[<EntryPoint>]
let main args =
    Web.run args "Storefront"
        [ "identity", "accounts"
          "catalog", "products"
          "orders", "orders" ]
    0
