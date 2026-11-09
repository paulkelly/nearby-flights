# Redis lab

Hands-on exercises for stage 5c. Everything runs against the `redis` service in `compose.yaml`
(official `redis:8.10-alpine` image, 32 MB cap, LRU eviction, no persistence). Nothing here costs money.

## Setup

```bash
docker compose up -d redis              # just Redis; the rest of the app isn't needed yet
docker compose ps redis                 # STATUS should say "(healthy)" after a few seconds
docker compose exec redis redis-cli     # interactive prompt: 127.0.0.1:6379>
```

The Redis image has a shell, unlike the chiseled API image, so `exec` works. `redis-cli` also takes a command
directly: `docker compose exec redis redis-cli PING`.

Use **two terminals** for most of this: one running `MONITOR`, one where you type commands.

## 1. Watch everything: MONITOR

In terminal A:

```
MONITOR
```

It prints every command any client sends, with a timestamp and the client's address. Leave it running and keep
an eye on it during the other exercises. (The health check's `PING` appears every 5 s: that's Docker.)

`MONITOR` is great for learning and debugging, but it slows a busy server noticeably, so it's not something to
leave running in production.

## 2. Strings and expiry

```
SET greeting "hello"
GET greeting
SET greeting "hi" NX        # NX = only if it doesn't exist: (nil), nothing changed
SET counter 10
INCR counter                # atomic: safe even with many clients at once
SET session:42 "data" EX 20 # expires in 20 s
TTL session:42              # seconds left; run it a few times
PTTL session:42             # the same, in ms
TTL greeting                # -1: exists, no expiry
TTL nope                    # -2: doesn't exist
```

Wait 20 s and `GET session:42` again: `(nil)`. Expiry is the main tool a cache has. Our API's cache uses
5-second entries.

**Question:** Redis deletes expired keys in two ways: when you touch them (lazily), and with a background job
that samples keys with expiries (actively). Why would it need both?

## 3. Key names and finding keys

Keys are just strings; `:` is a naming convention for namespaces (`flightradar:flights:...`), nothing more.

```
SET user:1:name "Ada"
SET user:2:name "Linus"
KEYS user:*                 # works, but...
SCAN 0 MATCH user:* COUNT 100
```

`KEYS` walks the whole keyspace in one go, and Redis runs commands one at a time, so on a big production
instance every other client waits. `SCAN` returns a cursor and a batch; you call it again with the cursor until
it comes back as `0`. Rule of thumb: never `KEYS` in production.

## 4. Other types (and your leaderboard)

```
TYPE greeting                                   # string
HSET plane:4ca7b5 callsign RYR4TN alt 37000     # hash: a small record
HGETALL plane:4ca7b5
ZADD scores 1200 alice 950 bob 1430 carol       # sorted set: member + score
ZRANGE scores 0 -1 REV WITHSCORES               # the leaderboard, highest first
ZRANK scores bob                                # position (0-based, lowest first)
```

Sorted sets are what you used for the AWS leaderboard. Redis's **geo** commands are sorted sets in disguise: the
score is the position encoded as a geohash (52-bit integer), so nearby points have nearby scores:

```
GEOADD planes -6.27 53.42 EI-DCL -0.45 51.47 G-EUPT
GEOSEARCH planes FROMLONLAT -6.0 53.3 BYRADIUS 100 km WITHDIST ASC
TYPE planes                                     # zset
ZRANGE planes 0 -1 WITHSCORES                   # the geohash scores
```

Note the **longitude first** order. That's the idea for roadmap step 7: a background poller `GEOADD`s every
aircraft, and each user search becomes one `GEOSEARCH` instead of a call to adsb.lol.

## 5. Memory and eviction

```
INFO memory                 # look at used_memory_human and maxmemory_human
CONFIG GET maxmemory*       # 32 MB, allkeys-lru: from the command in compose.yaml
MEMORY USAGE plane:4ca7b5   # bytes for one key, overhead included
DBSIZE                      # number of keys
```

Now overfill it. From a normal terminal (not inside `redis-cli`), `redis-benchmark` (included in the image)
writes 400,000 random keys of 200 bytes, about 3× what fits:

```bash
docker compose exec redis redis-benchmark -q -t set -n 400000 -r 10000000 -d 200
docker compose exec redis redis-cli INFO stats | grep evicted_keys
docker compose exec redis redis-cli INFO memory | grep used_memory_human
```

Memory stays just under 32 MB, and `evicted_keys` shows how many keys Redis threw away to stay there. Check
whether your `greeting` and `scores` keys survived: with `allkeys-lru`, *any* key can go, including ones you
care about. That's right for a cache and wrong for anything else (for a leaderboard you'd use
`noeviction` or a `volatile-*` policy, which only evicts keys that have a TTL).

Clean up with `FLUSHALL` (deletes every key; fine here, careful anywhere else).

**Question:** LRU means "least recently used". Redis doesn't keep a perfect LRU list; it samples a few keys
(`maxmemory-samples`, 5) and evicts the oldest of those. Why might that be a good trade-off?

## 6. Restart = empty

```bash
docker compose restart redis
docker compose exec redis redis-cli DBSIZE   # 0
```

Persistence is off (`--save ""`, `--appendonly no`), so a restart starts empty. For a cache that's the point: no
disk I/O, and any stale data is gone. The API will refill it on the next requests.

## 7. Watching the API use Redis

The API uses Redis as the second level (L2) of its `HybridCache` when `ConnectionStrings:Redis` is set: Compose
sets it for the `api` container, and `appsettings.Development.json` sets it (`127.0.0.1:6379`) for Rider runs.
Without it, the API uses its in-memory cache (L1) only.

Start everything, open `MONITOR` in one terminal, and use the app at http://localhost:8080 (or `curl` the API):

```bash
docker compose up -d --build
docker compose exec redis redis-cli MONITOR
```

What a cache **miss** looks like (trimmed):

```
"HMGET" "flightradar:flights:53.4737…:-6.2067…:60" "absexp" "sldexp"   # is there an entry, and has it expired?
"HGET"  "flightradar:flights:53.4737…:-6.2067…:60" "data"              # nothing, so the API calls adsb.lol, then:
"HMSET" "flightradar:flights:53.4737…:-6.2067…:60" "absexp" "639…" "sldexp" "-1" "data" "\x03\x01…"
"EXPIRE" "flightradar:flights:53.4737…:-6.2067…:60" "5"                # Redis deletes it after the 5 s TTL
```

Things to notice:

- **Each entry is a hash**, not a string: `data` holds the payload, `absexp`/`sldexp` the absolute and sliding
  expiry. `HGETALL` one (quickly, it only lives 5 s) and `TTL` it. The payload is `HybridCache`'s own binary
  header followed by JSON; the key is stored inside it too, as a sanity check.
- **The key** is `flightradar:` (the `InstanceName` prefix) + `flights:{lat}:{lon}:{radiusKm}` from
  `CachingFlightDataProvider.CacheKey`, using the *snapped* area (see the experiment below), so the numbers
  won't match what you clicked.
- **A repeat request within 5 s sends nothing to Redis**: it's answered from the API's memory (L1). Redis is only
  asked when L1 misses.
- **`__MSFT_HCT__*`** reads are `HybridCache` checking whether "invalidate everything" was requested (tag-based
  invalidation, which we don't use).
- **At connect** you'll see `CLIENT SETNAME`, `INFO`, `CLUSTER NODES`, a `SUBSCRIBE`: StackExchange.Redis
  working out what kind of server it's talking to.

**Experiment: L2 survives an API restart.** Request an area, `docker compose restart api`, and request the same
area within 5 s. `MONITOR` shows `HMGET`/`HGET` but no `HMSET`: the new process had an empty L1, found the answer
in Redis and never called adsb.lol. With several API instances (stage 5c piece 4), that's how they share work.

**Experiment: nearby requests share a key.** Keys aren't the exact request: `AreaSnapping` moves the centre to
the nearest point of a 10 km grid and enlarges the radius to cover the difference, so the key looks like
`flightradar:flights:53.4737…:-6.2067…:60` for a 50 km search from Dublin. Click a few points a couple of
hundred metres apart (same radius) and watch `MONITOR`: one `HMSET`, then nothing, because they all snap to the
same circle and are answered from the API's memory. The API logs show one call to adsb.lol, for the snapped
circle (`/v2/point/53.4737…/-6.2067…/33`, in nautical miles). Click 20 km away and a second key appears.
Each user still sees aircraft measured from their own centre: the API trims the shared result per request.

**Experiment: Redis is optional.** `docker compose stop redis` and keep using the app. It keeps working; the API
logs `Cache backend read failure` / `write failure` for each request and calls adsb.lol directly.
`docker compose start redis` and it reconnects by itself, with no API restart. The settings that make that
true are in `RedisCacheServiceCollectionExtensions.cs` (`AbortOnConnectFail`, `BacklogPolicy.FailFast`,
short timeouts).

## 8. Two API instances, one cache

This is where a shared cache earns its keep. Each API instance has its own in-memory cache (L1), so without
Redis two instances would each call adsb.lol for the same area. With Redis (L2), whichever instance misses
first fetches and stores the answer, and the other one finds it there.

```bash
docker compose up -d --build --scale api=2
docker compose ps                                   # flightradar-api-1 and flightradar-api-2
docker compose exec web nslookup api 127.0.0.11     # Docker's DNS: one name, two addresses
docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' flightradar-api-1
docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' flightradar-api-2
```

nginx resolves `api` to both addresses and spreads requests across them (that's why `nginx.conf.template` uses
a variable `proxy_pass` with `resolver`). Start `MONITOR`, then request the same area a few times within 5 s:

```bash
docker compose exec redis redis-cli MONITOR
curl "http://localhost:8080/api/flights?lat=53.42&lon=-6.27&radiusKm=50"   # repeat 3-4 times
docker compose logs api | grep "Sending HTTP request GET https://api.adsb.lol"
```

What you should see (`MONITOR` shows each client's IP, so you can tell the instances apart):

```
172.18.0.3  HMGET, HGET, HMSET   # api-2: L1 and L2 miss, called adsb.lol, stored the answer
172.18.0.4  HMGET, HGET          # api-1: L1 miss, L2 hit. No HMSET: it never called adsb.lol
```

and only **one** `Sending HTTP request` line across both instances. Requests that land on an instance which
already has the answer in memory don't reach Redis at all.

Two things to notice:

- **The split isn't strictly alternate.** nginx runs several worker processes, each with its own round-robin
  counter, so a run of requests can land on the same instance. Over many requests it evens out.
- **Each instance connects to Redis on its first request** (a few hundred ms once), then reuses the connection.

Go back to one instance with `docker compose up -d --scale api=1` (or just `docker compose up -d`).

**Question:** the two instances can still both miss at the same moment and both call adsb.lol: `HybridCache`'s
stampede protection only works *within* one process. How could you stop that across instances, and is it worth it
for a 5-second cache?

## 9. Long-lived keys: routes and logos

Flights are cached for 5 seconds, but two other kinds of answer barely change, so they're cached much longer in
the same Redis:

| Key | Holds | TTL |
|---|---|---|
| `flightradar:route:EIN123` | a flight number's route, or "no route known" | 1 hour (`Routes:CacheMinutes`) |
| `flightradar:logo:EIN` | an airline's logo (PNG bytes), or "no logo" | 24 hours (`Logos:CacheHours`) |

Use the app for a minute around Dublin (or `docker compose up -d redis` and run the API from Rider), then:

```bash
docker compose exec redis redis-cli --scan --pattern 'flightradar:route:*'   # one key per airline callsign seen
docker compose exec redis redis-cli TTL flightradar:route:EIN123             # counting down from 3600
docker compose exec redis redis-cli HGET flightradar:route:EIN123 data       # HybridCache's header, then the JSON
docker compose exec redis redis-cli TTL flightradar:logo:EIN                 # counting down from 86400
docker compose exec redis redis-cli MEMORY USAGE flightradar:logo:EIN        # a few KB per logo
```

Things to notice:

- **Same shape as the flight keys:** a hash with `data`, `absexp` and `sldexp`. A route's `data` ends in JSON
  like `{"Route":{"Airports":[{"Icao":"EIDW",…}]}}`; a logo's is the PNG itself (it starts `\x89PNG`), because
  `HybridCache` stores `byte[]` as-is rather than as JSON.
- **"Nothing known" is cached too:** a callsign the route database doesn't have is stored as `{"Route":null}`,
  and an airline without a logo as an empty `data`. Without that, every 10-second poll would ask again for every
  such callsign.
- **Failures aren't:** if the route source is down, nothing is written, and the next poll tries again.
- **They outlive the flights:** after a few minutes `--scan --pattern 'flightradar:flights:*'` finds little or
  nothing (5 s TTL) while the route and logo keys are still there. With `maxmemory 32mb` and `allkeys-lru`
  (section 5), Redis evicts whatever was used longest ago when it fills up, whatever its TTL.

**Experiment: a route lookup is shared.** Open `MONITOR`, then load an area you haven't viewed for an hour. You'll
see one `HMSET flightradar:route:…` per new airline callsign, and on the next polls none: the API's memory (L1)
answers. Restart the API (`docker compose restart api`) and poll again: `HGET`s, but still no `HMSET`, and the
API logs show no requests to `vrs-standing-data.adsb.lol`, because Redis (L2) already had them.

**Question:** routes are looked up one callsign at a time. The same database is also published as one file,
`routes.csv` (https://vrs-standing-data.adsb.lol/routes.csv, about 19 MB and 620,000 routes), updated hourly.
How would you load it into Redis so every lookup is a single `HGET` (one hash, callsign → route)? How would you
keep it fresh without a moment where it's half-loaded (hint: load into a new key, then `RENAME`)? And would it
even fit in the 32 MB limit? (`MEMORY USAGE` on a sample tells you; storing only the airport codes, like
`EIDW-KORD`, and looking airports up separately is one way to shrink it.)
