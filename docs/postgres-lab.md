# Postgres lab

Hands-on exercises for step 7 (saved locations). Everything runs against the `postgres` service in `compose.yaml`
(official `postgres:18-alpine` image, data in the `postgres-data` volume). Nothing here costs money, and nothing
here deletes your saved locations: the exercises that change data run inside a transaction and roll it back.

Redis (see [redis-lab.md](redis-lab.md)) is the app's cache: fast, disposable, empty after a restart. Postgres is
where things users *keep* live: tables with a fixed shape, rules the database itself enforces, and data that
survives restarts.

## Setup

```bash
docker compose up -d migrate            # starts Postgres, applies migrations, exits
docker compose ps -a postgres migrate   # postgres "(healthy)", migrate "Exited (0)"
docker compose exec postgres psql -U flightradar -d flightradar
```

The prompt becomes `flightradar=#`. Inside the container no password is needed (the image trusts local
connections); from outside (Rider's Database tool, your own psql) use the `POSTGRES_PASSWORD` from `.env`.

psql's own commands start with `\` (`\?` lists them, `\q` quits). Everything else is SQL and needs a `;` at the
end: without one, psql waits for more (the prompt changes to `flightradar-#`).

Save a location or two in the app (http://localhost:8080) first, so there's something to look at.

## 1. Look around

```
\dt                          -- tables
\d saved_locations           -- columns, types, nullability, indexes, foreign keys
\di                          -- indexes
\x auto                      -- wide rows one field per line when they don't fit
select * from saved_locations;
select id, encode(token_hash, 'hex'), created_at from owners;
```

Things to notice:
- Table and column names are snake_case (`radius_km`): `EFCore.NamingConventions` maps C#'s `RadiusKm`.
- `latitude` has 3 decimal places however precisely you clicked: the API rounds to ~100 m before saving.
- `owners` holds a **hash**. Compare it with the token in the browser (DevTools → Application → Local Storage →
  `flight-radar:owner-token`): nothing in the database can be turned back into that token.
- IDs made around the same time start with the same characters: version 7 GUIDs begin with a timestamp, so new
  rows land at the end of the index, in time order, instead of at random places in it.

## 2. The database enforces the rules

The API checks names and owners, but the database refuses bad data even when something bypasses the API (a bug,
a script, you in psql):

```sql
-- A location for an owner that doesn't exist: refused by the foreign key.
insert into saved_locations (id, owner_id, name, latitude, longitude, radius_km, created_at)
values (gen_random_uuid(), gen_random_uuid(), 'Nowhere', 0, 0, 1, now());

-- A second location with one of your names: refused by the unique index on (owner_id, name).
-- (Copy a real owner_id and name from `select owner_id, name from saved_locations;`.)
insert into saved_locations (id, owner_id, name, latitude, longitude, radius_km, created_at)
values (gen_random_uuid(), '<owner_id>', '<an existing name>', 0, 0, 1, now());
```

The second error has SQLSTATE `23505` (unique violation). That's exactly what the API catches when two saves race
each other for one name, and turns into a 409 (`LocationEndpoints.TrySaveAsync`).

## 3. Transactions: try it, then take it back

A transaction groups statements so they all happen or none do. Until `COMMIT`, nobody else sees the changes, and
`ROLLBACK` undoes them. A save point for the whole database, like quick-save before a risky fight:

```sql
begin;
delete from owners;                         -- every owner...
select count(*) from saved_locations;       -- ...and their locations went too: ON DELETE CASCADE
rollback;                                   -- undo all of it
select count(*) from saved_locations;       -- back
```

While the transaction is open, look at the app in another tab: your locations are still listed. Other connections
see the data as it was before `begin` until you commit (Postgres keeps both versions of the rows meanwhile). Try
*saving* a location there, though, and it hangs: the new row's foreign key needs your owner row, which your open
`delete` has locked. Type `rollback;` and the save completes at once. Readers never wait for writers; writers
touching the same rows wait for each other. Keep transactions short.

EF Core uses a transaction for every `SaveChangesAsync`, which is why one save either writes all its changes or
none of them.

## 4. Watch the app's SQL

Like Redis's `MONITOR`, but from the database's side: make Postgres log every statement it runs.

```sql
alter system set log_statement = 'all';
select pg_reload_conf();
```

In another terminal: `docker compose logs -f postgres`. Then use the app: list, save, delete a location.

- Every 10 s, `SELECT 1`: the API's health check (`CanConnectAsync`) proving it can reach the database.
- `DISCARD ALL` before statements: Npgsql resetting a pooled connection before reusing it. Opening connections
  is slow, so a pool keeps a few open, like an object pool for bullets.
- Queries use parameters (`$1`, `$2`) with the values listed separately, never pasted into the SQL text: that's
  what makes SQL injection impossible here.
- Deleting is a single `DELETE … WHERE id = $1 AND owner_id = $2`. The owner check is part of the statement
  itself, so it can't be forgotten.

Turn it off again (it logs your coordinates too):

```sql
alter system reset log_statement;
select pg_reload_conf();
```

## 5. Indexes and the query planner

`EXPLAIN` shows how Postgres plans to run a query without running it:

```sql
explain select * from saved_locations where owner_id = '00000000-0000-0000-0000-000000000000';
```

With a handful of rows, it says `Seq Scan`: reading the whole (tiny) table is cheaper than using an index. Make
the planner avoid that, for this session only, to see the index it *would* use on a big table:

```sql
set enable_seqscan = off;
explain select * from saved_locations where owner_id = '00000000-0000-0000-0000-000000000000';
reset enable_seqscan;
```

It uses `ix_saved_locations_owner_id_name`, the unique index on `(owner_id, name)`. An index on several columns
also serves queries on its *first* column alone, which is why EF didn't create a separate `owner_id` index.
(`where name = 'Home'` alone couldn't use it: like a phone book sorted by surname then first name, which can't
find every "Paul" without reading it all.)

## 6. Migrations

```sql
select * from "__EFMigrationsHistory";     -- quotes needed: the name has capitals
```

Each row is a migration that has run. The `migrate` container compares this list with the migrations compiled
into it and runs only the missing ones, in order, which is why running it again does nothing:

```bash
docker compose up migrate                  # "No migrations were applied. The database is already up to date."
```

From the repo root, see the SQL a migration would run (what a DBA would review at work):

```bash
dotnet ef migrations script --project src/FlightRadar.Api                       # everything, from empty
dotnet ef migrations script InitialCreate AddSavedLocations --project src/FlightRadar.Api   # one step
```

## 7. Data that survives

Unlike Redis, restarting loses nothing:

```bash
docker compose restart postgres            # your locations are still there afterwards
docker compose down                        # removes containers and network, keeps the postgres-data volume
docker compose up -d                       # same data
docker volume ls                           # flightradar_postgres-data
```

Only `docker compose down -v` deletes the volume, and with it every saved location. The browser's token then
points at nothing: the app notices the 401, forgets the token and starts afresh on the next save.

## 8. Backup and restore

`pg_dump` writes a database out as SQL:

```bash
docker compose exec -T postgres pg_dump -U flightradar -d flightradar > backup.sql
```

`-T` stops Docker allocating a terminal, which would mangle the redirected output. Open `backup.sql`: the schema
(`CREATE TABLE …`) and the data (`COPY … FROM stdin`). Restoring into an empty database is
`docker compose exec -T postgres psql -U flightradar -d flightradar < backup.sql`.

`backup.sql` contains your saved locations: don't commit it. Delete it when you're done.

## Ideas to try

- Write a query for "each owner and how many locations they have" (`group by owner_id`).
- Add a `check (radius_km > 0 and radius_km <= 250)` constraint in a migration (EF: `ToTable(t => t.HasCheckConstraint(…))`),
  then try to break it from psql.
- Add the `LastUsedAt` column for real (you've done the migration part): set it when a location is opened, and sort
  the chips by it.
