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

Flights are cached for seconds (5 s, or 25 s for areas the poller keeps fresh: section 11), but two other kinds
of answer barely change, so they're cached much longer in the same Redis:

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
- **They outlive the flights:** a minute after you close the app, `--scan --pattern 'flightradar:flights:*'`
  finds nothing (seconds-long TTLs, and the poller stops once nobody watches) while the route and logo keys are
  still there. With `maxmemory 32mb` and `allkeys-lru`
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

## 10. Watched areas: a sorted set as an expiring set (step 7a)

The API keeps the areas people are looking at fresh in the background. Which areas those are lives in one sorted
set, `flightradar:watched-areas`:

- **member:** the area (snapped centre and radius), e.g. `53.47377272275962:-6.20671317663633:20`
- **score:** when it stops being watched, in Unix milliseconds: the time of the latest request plus 30 s

Redis can give a whole key a TTL, but not single members of a set. Putting the deadline in the score gets the same
effect, and every operation becomes a range of scores. Your leaderboard used scores for points; here they're
deadlines.

Open the app on Dublin (http://localhost:8080), then:

```
ZRANGE flightradar:watched-areas 0 -1 WITHSCORES     # every member and its deadline
TIME                                                  # Redis's clock: seconds, then microseconds
TYPE flightradar:watched-areas                        # zset
OBJECT ENCODING flightradar:watched-areas             # listpack: small sets are stored as one compact list
```

Compare the score with `TIME` (add three zeros to its first line for milliseconds): about 30,000 ahead. Run it
again after the next browser poll and the score has moved on: each request renews it. Change the radius slider or
click somewhere else and a second member appears.

What each part of the app sends (watch with `MONITOR` in a second terminal):

| Who | Command | Why |
|---|---|---|
| every `/api/flights` request | `ZADD flightradar:watched-areas GT <now+30000> <area>` | add, or push the deadline later. **GT** = only if greater: an instance whose clock is a little behind can never shorten it |
| the poller, every 10 s | `ZRANGEBYSCORE flightradar:watched-areas (<now> +inf` | everything still in the future. `(` = exclusive: a deadline of exactly now has passed |
| the poller, after each tick | `ZREMRANGEBYSCORE flightradar:watched-areas -inf <now>` | your exercise: forget what has passed. Inclusive, so prune and read agree on the boundary |

Try the read yourself, putting Redis's own time (in milliseconds) in place of the number:

```
ZRANGE flightradar:watched-areas (1794323307000 +inf BYSCORE WITHSCORES
```

**Experiment: zones go to sleep.** Close (or hide) the app's tab. Its polling stops, so nothing renews the area;
30 s later it drops out of `ZRANGE … BYSCORE`, and by the next tick the poller has pruned it from the set
altogether (`ZCARD flightradar:watched-areas` goes to 0). After that the poller fetches nothing, and adsb.lol gets
no calls on your behalf.

## 11. The poller and its claims

To see the poller tick, set `POLLER_LOG_LEVEL=Debug` in `.env` (or in the shell) and restart the API:

```bash
docker compose up -d                                       # picks up the new value
docker compose logs -f api | grep "Poller tick"            # one line every 10 s
```

```
Poller tick: 1 watched, 1 fetched, 0 by another instance, in 56 ms
```

Each fetched area is written to the cache with a 25 s TTL (two ticks plus a margin), so a slow tick never leaves
a gap. `TTL flightradar:flights:<area>` keeps bouncing back up towards 25 instead of running out, and the browser's
requests are answered from the cache without waiting for adsb.lol.

**Claims: the simplest distributed lock.** With two API instances both ticking, who fetches an area? Before
fetching, each instance runs:

```
SET flightradar:poll-claim:<area> <host>:<pid> NX PX 8000
```

- **NX:** only if the key doesn't exist. Redis runs one command at a time, so exactly one instance's `SET` succeeds;
  the other gets `(nil)` and skips the area.
- **PX 8000:** the key deletes itself after 8 s, just before the next tick, so the area is up for grabs again. If
  the instance holding it crashes, nothing is stuck: the claim simply runs out.
- **The value** is only for you: which container (Docker sets the hostname to the container ID) holds it.

Run two instances and watch:

```bash
POLLER_LOG_LEVEL=Debug docker compose up -d --scale api=2
docker compose logs -f api | grep "Poller tick"
```

Each tick, one instance says `1 fetched, 0 by another instance` and the other `0 fetched, 1 by another instance`.
In `redis-cli`:

```
KEYS flightradar:poll-claim:*                         # fine on a tiny dev Redis; use SCAN on a real one
GET flightradar:poll-claim:<the key above>            # which instance holds it
PTTL flightradar:poll-claim:<the key above>           # milliseconds left, under 8000
```

Then stop the instance holding the claims (`docker stop flightradar-api-1`, say). Within a tick the other one starts
fetching. Scale back afterwards with `docker compose up -d --scale api=1`.

**Questions:**
- Why must the claim (8 s) be shorter than the tick (10 s)? What would happen with 15 s? (The app refuses to start
  with that setting: `Poller:ClaimSeconds must be less than Poller:IntervalSeconds`.)
- Nobody deletes a claim after fetching. Why is that better here than a `DEL` afterwards? (Think about a fast
  instance finishing its fetch before the other instance's tick has even started.)
- A lock that does get released early must check it still owns the key first, or it might delete someone else's.
  Why can't that be done safely with a separate `GET` and then `DEL`? (Look up the "distributed locks" page in the
  Redis docs, and `EVAL`.)

## 12. Exercise: a geo set of every aircraft

Today each watched area costs its own adsb.lol call, even when areas overlap (Dublin at 10 km and at 50 km). Redis
can index points by position: a **geo set** is a sorted set whose scores are geohashes (positions turned into one
number, so nearby points get nearby scores).

```
GEOADD flightradar:aircraft -6.2701 53.4213 4cad7b        # longitude first!
GEOSEARCH flightradar:aircraft FROMLONLAT -6.27 53.42 BYRADIUS 20 km ASC WITHDIST
ZSCORE flightradar:aircraft 4cad7b                         # it really is a sorted set underneath
GEOPOS flightradar:aircraft 4cad7b                         # back out (slightly rounded)
```

The exercise, in steps:

1. In `PollerTick`, after fetching an area, `GEOADD` every aircraft (hex as the member) and store its details in a
   hash (`HSET flightradar:aircraft-data <hex> <json>`).
2. Aircraft vanish (landed, out of range). Keep a second sorted set of hex to last seen (ms), and prune both with
   `ZRANGEBYSCORE` + `ZREM`, the way watched areas are pruned. A geo set member can't expire on its own either.
3. Answer `/api/flights` with `GEOSEARCH … BYRADIUS … ASC WITHDIST` and `HMGET` for the details, falling back to
   the provider when the area isn't covered.
4. Then the interesting part: fetch one big circle per region instead of one per watched area, so every
   overlapping search is answered from Redis.

Things to decide: what "covered" means (the search circle must lie inside a fetched circle); how stale a position
may be (an airliner covers about 250 m a second); and whether `NearbyFlightsService`'s exact haversine trim is
still needed (`GEOSEARCH` measures distance too, on a slightly different Earth model).

## 13. Pub/sub: the SignalR backplane (step 7b)

Everything so far has been keys: data that stays until it expires or is deleted. **Pub/sub** is the opposite:
messages that exist only while they're being delivered. A client `SUBSCRIBE`s to a channel; anyone can `PUBLISH`
to it; Redis hands the message to whoever is subscribed *at that moment* and forgets it. Nothing is stored: a
subscriber that wasn't listening never sees it. (Redis Streams, `XADD`/`XREAD`, are the stored, replayable kind.)

Try it with two terminals, both in `redis-cli`:

```
SUBSCRIBE lab:news                    # terminal A: blocks, waiting for messages
PUBLISH lab:news "hello"              # terminal B: (integer) 1 = delivered to one subscriber
PUBLISH lab:nobody "hello"            # (integer) 0: nobody listening, the message is simply gone
PUBSUB CHANNELS                       # channels with at least one subscriber
```

`Ctrl+C` leaves terminal A's subscription. A connection that has subscribed can only run subscribe-type commands,
which is why StackExchange.Redis keeps a second connection to Redis just for subscriptions. Each API instance has
three connection pairs in `CLIENT LIST` (`CLIENT LIST` shows each one's last command in `cmd=`): ours for the
registry and the claims, the cache's, and SignalR's backplane, whose subscription connection has `sub=` above 0.
The cache and the backplane each close the connection they use when the app shuts down, so each must own its own
rather than borrow ours.

### How SignalR uses it

With two API instances, a browser's WebSocket is open to one of them, but the poller that fetches its area may run
on the other. The backplane joins them up: each instance subscribes to a channel per SignalR group its connections
are in, and sending to a group is a `PUBLISH` to that group's channel. Like the message bus between the servers of
a sharded game world: a server announces "this happened in zone X", and whichever servers have players in zone X
pass it on.

Open the app on Dublin (it subscribes over a WebSocket), then:

```
PUBSUB CHANNELS *FlightsHub*
```

```
FlightRadar.Api.Streaming.FlightsHub:all
FlightRadar.Api.Streaming.FlightsHub:group:area:53.47377272275962:-6.20671317663633:20
FlightRadar.Api.Streaming.FlightsHub:connection:hg3rwKIbOF9Bd7FybnnNzg
FlightRadar.Api.Streaming.FlightsHub:internal:groups
…
```

- `…:group:area:<area>`: one per group in use: the area your browser is watching (the same snapped area as the
  watched-areas set and the cache key).
- `…:connection:<id>`: one per connection, for messages to a single client.
- `…:all` and `…:internal:…`: broadcasts, and the instances' own bookkeeping (group joins, acknowledgements).

The names start with the hub's class name, not `flightradar:`: SignalR's channel prefix is a setting of the Redis
connection, and we leave the backplane connection's unset. Channels and keys live in separate worlds anyway,
so they can't collide.

Listen in on your area's channel (copy its name from the list above):

```
SUBSCRIBE "FlightRadar.Api.Streaming.FlightsHub:group:area:53.47377272275962:-6.20671317663633:20"
```

Every 10 s (each poller tick) a message arrives: a few binary bytes (SignalR's MessagePack envelope), then plain
JSON: `{"type":1,"target":"flightsUpdated","arguments":[{"lat":53.47…,"count":8,"flights":[…]}]}`. That's exactly
what the browser receives over its WebSocket: the instance that publishes serialises the message once, and every
instance passes those bytes straight on to its connections. You can see the same in the browser: DevTools →
Network → WS → `flights` → Messages.

### Experiments

**Two instances.** `docker compose up -d --scale api=2`, reload the app, and run `PUBSUB NUMSUB` on your group's
channel: `1` (only the instance holding your WebSocket subscribes). Turn on `POLLER_LOG_LEVEL=Debug` (section 11):
the instance that fetches the area isn't always the one your browser is connected to, and pushes arrive either way.

**Redis down, connections up.** The backplane carries every push, even to connections on the same instance. Stop
Redis (`docker compose stop redis`) with the app open: the WebSocket stays connected, but nothing arrives on it.
After 25 s without data the app's watchdog notices and the status line changes from "● Live" to "updates every
10 s": it's polling `/api/flights` again (Network tab, Fetch/XHR). `docker compose start redis` and within a tick or
two it's back to Live. Pub/sub's "deliver now or never" is why a watchdog is needed at all: a message published
while Redis was down is not waiting anywhere to be picked up later.

**Questions:**
- Why does the poller push every area it refreshes, even when no browser on its own instance watches it?
- Messages aren't stored, so a browser that reconnects after a minute misses the pushes in between. Why doesn't
  that matter for this app? (What does the browser do as soon as it's connected again?)
- Redis Cluster can't send every `PUBLISH` to every node cheaply, so it has *sharded* pub/sub (`SSUBSCRIBE` /
  `SPUBLISH`): a channel lives on one shard, like a key. Which of the channels above would benefit, and which (like
  `…:all`) must reach everyone?
