open SharedPostgres

[<EntryPoint>]
let main _ =
    Schema.run
        """CREATE TABLE IF NOT EXISTS products (
               id integer PRIMARY KEY,
               name text NOT NULL
           )"""
        """INSERT INTO products (id, name) VALUES (1, 'Sample product')
           ON CONFLICT (id) DO NOTHING"""
