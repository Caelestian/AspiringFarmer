open SharedPostgres

[<EntryPoint>]
let main _ =
    Schema.run
        """CREATE TABLE IF NOT EXISTS stock_items (
               id integer PRIMARY KEY,
               name text NOT NULL
           )"""
        """INSERT INTO stock_items (id, name) VALUES (1, 'Sample stock item')
           ON CONFLICT (id) DO NOTHING"""
