open SharedPostgres

[<EntryPoint>]
let main _ =
    Schema.run
        """CREATE TABLE IF NOT EXISTS reports (
               id integer PRIMARY KEY,
               name text NOT NULL
           )"""
        """INSERT INTO reports (id, name) VALUES (1, 'Sample report')
           ON CONFLICT (id) DO NOTHING"""
