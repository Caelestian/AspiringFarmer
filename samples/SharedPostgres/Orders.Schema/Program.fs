open SharedPostgres

[<EntryPoint>]
let main _ =
    Schema.run
        """CREATE TABLE IF NOT EXISTS orders (
               id integer PRIMARY KEY,
               name text NOT NULL
           )"""
        """INSERT INTO orders (id, name) VALUES (1, 'Sample order')
           ON CONFLICT (id) DO NOTHING"""
