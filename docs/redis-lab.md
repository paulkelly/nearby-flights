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

Coming in stage 5c piece 2: once the API is connected, `MONITOR` shows exactly what `HybridCache` writes and
reads as you move the map.
