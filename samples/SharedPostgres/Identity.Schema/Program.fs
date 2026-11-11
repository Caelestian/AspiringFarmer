open SharedPostgres

[<EntryPoint>]
let main _ =
    Schema.run
        """CREATE TABLE IF NOT EXISTS accounts (
               id integer PRIMARY KEY,
               name text NOT NULL
           )"""
        """INSERT INTO accounts (id, name) VALUES (1, 'Sample customer')
           ON CONFLICT (id) DO NOTHING"""
