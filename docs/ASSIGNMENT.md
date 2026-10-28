# Radni zadatak: MQTT Gateway API

Verzija 1 · 2026-10-28 · vlasnik: chili · mentor: AI agent (review, ne implementacija)

---

## 1. Kontekst i pravila igre

Ovo je isti zadatak koji na poslu dobijaju backend praktikanti (`~/init-technologies/Backend Praksa/`), ali postavljen na senior nivou: nema dnevnog plana koji te vodi za ruku, nema koraka „dodaj ovu liniju u `Program.cs`". Dobijaš **zahteve i acceptance kriterijume**, a dizajn je tvoj posao.

Week 1 (konzolna aplikacija) se preskače. Krećeš direktno od Web API-ja.

**Pravila:**

1. **Produkcioni kod pišeš ti, rukom.** Bez AI generacije koda, bez IDE AI completion-a. Isto pravilo kao na `code-again`.
2. Agent (ja) radim: review, pitanja, objašnjenja koncepata, pseudokod, raspravu o trade-offovima. Ne pišem implementaciju.
3. **Review je preliminaran i po pozivu.** Ne čekaš kraj faze da bi pitao. Commit + push + „pogledaj ovo" je legitiman potez u bilo kom trenutku.
4. Commit poruke, PR opisi, kod, komentari — **engleski**. Razgovor i ovaj dokument — srpski.
5. Svaka faza završava PR-om. PR se ne merge-uje dok review ne prođe.
6. Kad nešto ne znaš — prvo pokušaj sam, pa pitaj. Pitanje „kako da napravim X" je slabije od „razmišljam između A i B, A mi smeta zbog Y".

**Zabranjeno (automatski request changes na PR-u):**

- `catch (Exception)` koji spaja dva različita uzroka u jedan odgovor
- bilo kakav secret u gitu (`appsettings.json`, compose fajl, commit istorija)
- `DateTime.Now`, `.Result`, `.Wait()`, `async void` (osim event handlera gde framework to traži — i tad objasni)
- in-memory kolekcija kao source of truth za podatke koji postoje u bazi
- zakomentarisan kod u commitu
- PR opis koji je samo naslov

---

## 2. Šta gradiš

**MQTT Gateway** — servis koji premošćuje MQTT i HTTP. Klijenti koji ne govore MQTT (web UI, drugi servisi, cron) kroz REST API mogu da:

- objave poruku na topic,
- upravljaju pretplatama gateway-a (koje topic filtere gateway sluša),
- pretraže poruke koje je gateway primio,
- vide statistiku saobraćaja po topicu u realnom vremenu.

Gateway je **dugoživeći stateful servis**: drži jednu konekciju ka brokeru kroz ceo životni vek procesa, mora da preživi pad brokera, pad baze i pad Redisa — svaki od ta tri nezavisno — i da u svakom trenutku ume da kaže u kakvom je stanju.

Težina ovog zadatka **nije** u CRUD-u. Težina je u: lifetime-ima, konkurentnosti, izolaciji otkaza i operabilnosti.

---

## 3. Stack (fiksiran)

| Sloj | Izbor | Napomena |
|---|---|---|
| Runtime | .NET 10 (`net10.0`) | lokalno imaš SDK 10.0.302 |
| MQTT klijent | `MQTTnet` 5.2.x | v5 je izbacio `ManagedClient` — reconnect pišeš sam |
| Broker (compose) | EMQX | isti kao na poslu; dashboard na 18083, MQTT na 1883 |
| Broker (testovi) | `eclipse-mosquitto:2.0` | preko `Testcontainers.Mosquitto` |
| Baza | MariaDB + `Pomelo.EntityFrameworkCore.MySql` 9.0.0 | |
| ORM | **EF Core 9.0.x** | ne 10 — vidi dole |
| Cache/stats | Redis + `StackExchange.Redis` | |
| Testovi | xUnit + `Testcontainers.{MariaDb,Redis,Mosquitto}` | |
| Logging | Serilog | structured |
| Resilience | Polly | |
| CI | GitHub Actions | |

**Zašto EF Core 9 na .NET 10:** Pomelo 9.0.0 (poslednji stable, 2025-08-17) ima dependency `Microsoft.EntityFrameworkCore.Relational >= 9.0.0 && <= 9.0.999`. EF Core 10 nije podržan. Targetuješ `net10.0` kao TFM, ali pinuješ EF Core pakete na 9.0.x i `dotnet-ef` tool na 9.x. Postoji community fork sa EF 10 podrškom (`Microting.EntityFrameworkCore.MySql`) — **ne koristiš ga**, ali treba da znaš zašto postoji.

> Ovo je prva senior odluka u projektu i tipična je: provider ekosistem kasni za frameworkom. Zapiši je u README kao svesnu odluku, ne kao slučajnost.

---

## 4. Arhitektura: Vertical Slice

Namerno **nije** Clean Architecture (to već vežbaš na `code-again`). Cilj je da uporediš dva modela na sopstvenoj koži.

Predlog layout-a — menjaj ga ako imaš bolji argument:

```
src/MqttGateway.Api/
  Program.cs
  Features/
    Publishing/          PublishEndpoint, PublishRequest, handler, validation
    Subscriptions/       endpoints, entity, EF config, handlers
    Messages/            query endpoints, entity, EF config, paging
    Statistics/          endpoints, Redis stats reader/writer
  Infrastructure/
    Persistence/         GatewayDbContext, Migrations/
    Mqtt/                konekcija, lifecycle, ingest
    Redis/
    Options/
tests/
  MqttGateway.UnitTests/
  MqttGateway.IntegrationTests/
```

**Pravila sloja:**

- Slice je vlasnik svog endpoint-a, svojih DTO-ova, svoje validacije i svoje EF konfiguracije.
- `Infrastructure/` drži samo ono što je zaista deljeno i bezlično (DbContext, MQTT konekcija, Redis multiplexer, options).
- Dupliranje između slice-ova je **dozvoljeno** dok ne boli. VSA kupuje nezavisnost slice-ova po ceni DRY-ja — to je poenta, ne propust.
- Minimal API endpoints ili kontroleri — tvoj izbor, ali konzistentno i obrazloženo u PR-u.

**Pitanje koje ću ti postaviti na review-u (razmišljaj o njemu od početka):** `Messages` entitet piše ingest (MQTT strana), a čita ga `Messages` slice (HTTP strana). Čiji je entitet? Šta si uradio i zašto to nije sakriven layer koji si upravo vratio na mala vrata?

---

## 5. API ugovor

Error format: **RFC 9457 ProblemDetails za apsolutno svaku grešku.** Jedan oblik, bez izuzetaka. Validacione greške → `ValidationProblemDetails`.

| Metod | Ruta | Uspeh | Greške |
|---|---|---|---|
| `POST` | `/api/publish` | `202` | `400` nevalidan topic/payload/QoS · `503` broker nedostupan |
| `GET` | `/api/subscriptions` | `200` | — |
| `POST` | `/api/subscriptions` | `201` + `Location` | `400` · `409` filter već postoji · `503` broker nedostupan |
| `DELETE` | `/api/subscriptions/{id}` | `204` | `404` · `503` |
| `GET` | `/api/messages?topic=&from=&to=&page=&pageSize=` | `200` | `400` loši parametri |
| `GET` | `/api/messages/{id}` | `200` | `404` |
| `GET` | `/api/stats` | `200` | — |
| `GET` | `/api/stats/topic?filter=` | `200` | `400` |
| `GET` | `/api/connection` | `200` | — |
| `GET` | `/health/live` | `200` | `503` |
| `GET` | `/health/ready` | `200` | `503` |

**Zamke u samom ugovoru — reši ih svesno:**

1. **Topic sadrži `/`.** `GET /api/stats/{topic}` sa topicom `sensors/room1/temp` ti razbija routing. Zato je gore query param. Ako hoćeš path param, moraš da objasniš kako si rešio escaping — i zašto je to bolje.
2. **`202` vs `200` na publish.** Šta tačno obećavaš pozivaocu? Za QoS 0 broker nikad ne potvrđuje; za QoS 1 potvrđuje. Da li tvoj `202` znači „primio sam zahtev" ili „broker je potvrdio"? Isti status kod za oba je laž u jednom od ta dva slučaja. Odluči i dokumentuj.
3. **Wildcard asimetrija.** `+` i `#` su legalni u *topic filteru* (subscribe), nelegalni u *topic name* (publish). Validacija ta dva nije ista. Ne radi jednu funkciju za oba.
4. **`503` za broker vs `503` za bazu.** Oba su „nedostupan resurs", ali pozivaocu moraju da izgledaju različito (`type`/`title`/`detail`). Nikad `catch (Exception)` koji ih spaja — to je tačno greška koju si uhvatio u tuđem review-u.
5. **Paging.** `pageSize` mora imati gornju granicu. Ordering mora biti deterministički (`ReceivedAt` nije unique). Offset ili keyset — odluči i obrazloži.

---

## 6. Faze

Svaka faza = jedna grana = jedan PR. Naming: `feature/f2-subscriptions`.

---

### F0 — Kostur, infrastruktura, CI

**Cilj:** `git clone` → `docker compose up -d` → infrastruktura radi → `dotnet run` → aplikacija se diže i ne puca.

**Zahtevi:**
- Repo sa `.gitignore` za .NET, solution + Web API projekat + dva test projekta.
- `docker-compose.yml` sa EMQX, MariaDB i Redis. Aplikacija **još nije** u compose-u (dolazi u F7).
- Health endpointi postoje (makar trivijalni).
- Konfiguracija kroz typed options sa validacijom na startu.
- GitHub Actions workflow: restore → build → test, na svaki PR.
- README: šta je ovo, kako se diže, koji portovi.

**Odluke za odbranu u PR opisu:**
- Gde stoje credentials i zašto baš tamo (`dotnet user-secrets` lokalno, env u compose-u).
- `ServerVersion` za Pomelo: `AutoDetect` (udara bazu na startu) ili eksplicitno `new MariaDbServerVersion(...)`. Jedna od te dve opcije pravi aplikaciju koja ne može da startuje dok baza nije gore. Koju biraš?

**Zamke:**
- Lozinka u `appsettings.json` ili u compose fajlu koji je commitovan. Jednom pushovana lozinka ostaje u istoriji.
- EMQX izlaže više portova (1883 MQTT, 8083 WS, 18083 dashboard). Mapiraj svesno, ne sve redom.
- Options bez `ValidateOnStart()` znače da ćeš za grešku u konfiguraciji saznati na prvom requestu, u produkciji.

**DoD:** CI zelen · `docker compose up -d` diže tri kontejnera · `GET /health/live` vraća 200 · nema secreta u gitu.

---

### F1 — MQTT konekcija i publish

**Cilj:** aplikacija održava konekciju ka brokeru kroz ceo svoj život i ume da objavi poruku.

**Zahtevi:**
- `POST /api/publish` radi end-to-end (proveri MQTTX-om ili `mosquitto_sub`-om da je poruka stvarno stigla).
- Konekcija se uspostavlja u pozadini, ne u request putanji.
- Reconnect sa backoff-om kad broker padne; aplikacija se oporavlja bez restarta.
- `GET /api/connection` prikazuje stvarno stanje.
- Graceful shutdown: `Ctrl+C` zatvara MQTT konekciju uredno.
- Unit testovi za validaciju topica/QoS-a.

**Odluke za odbranu:**
- Lifetime MQTT klijenta i zašto. (`IMqttClient` je deljen resurs — šta se dešava sa paralelnim `PublishAsync` pozivima?)
- Šta `202` tačno obećava (vidi §5.2).
- Da li publish ide kroz Polly retry. Oprez: publish nije idempotentan iz ugla subscribera.

**Zamke:**
- `ConnectAsync` u `Program.cs` posle `builder.Build()` (tako praktikantski plan kaže) → aplikacija ne startuje ako broker nije gore, a nema ni reconnect. Koristi `BackgroundService`.
- MQTTnet 5 **nema** `ManagedClient`. Ako nađeš tutorial sa `ManagedMqttClient`, on je za v3/v4.
- `IsConnected` ume da laže — vrednost je iz poslednjeg poznatog stanja, ne živa provera.
- Reconnect petlja bez jitter-a pravi thundering herd kad se broker vrati.
- `CancellationToken` mora da prođe kroz ceo lanac, uključujući reconnect petlju — inače shutdown visi.

---

### F2 — Pretplate i perzistencija

**Cilj:** pretplate preživljavaju restart aplikacije i pad brokera.

**Zahtevi:**
- CRUD nad pretplatama (`topicFilter` + `qos`), perzistovan u MariaDB.
- Pretplata je **jedinstvena po topic filteru** — garantovano, ne nadano.
- Na svaki (re)connect gateway obnavlja sve pretplate iz baze.
- Ako obnavljanje padne, to se **vidi** — ne nestaje u logu.
- EF migracije u repo-u.

**Odluke za odbranu:**
- Redosled: prvo baza pa broker, ili prvo broker pa baza? Oba imaju neuspešan scenario — opiši koji si izabrao i šta ostaje nekonzistentno u tvom izboru.
- Kako se migracije primenjuju (`dotnet ef database update` ručno / `Migrate()` na startu / zaseban migrator). Šta se dešava kad se jednog dana diže više instanci?
- Da li `/api/subscriptions` vraća stanje iz baze ili stvarno stanje na brokeru. Šta ako se razlikuju?

**Zamke (ovo su tačno nalazi iz review-a koji si čitao):**
- „Proveri pa upiši" nije atomično. 8 paralelnih POST-ova na isti filter → 8 redova. Rešenje je **unique index u bazi**, ne lock u aplikaciji. Kad baza odbije duplikat — mapiraj to na `409`, ne na `500`.
- `string` bez `MaxLength` → Pomelo generiše `longtext`, a na `longtext` ne ide efikasan (ni unique) indeks. Indeks je razlog zbog kog ti `MaxLength` ovde uopšte treba.
- **Pročitaj generisani `Up()` pre `database update`.** To je SQL koji će jednog dana ići na produkciju.
- Connect koji uspe a ne obnovi pretplate nije uspešan connect: `IsConnected == true`, nula pretplata, nula poruka, nula signala. Ili se diskonektuj, ili vodi odvojeno stanje „subscriptions restored" (vratiće ti se u F6 kao `Degraded` health).
- Pad baze i pad brokera na ovom endpointu moraju da daju različite odgovore.

---

### F3 — Ingest i upit nad porukama

**Cilj:** primljene poruke trajno žive u bazi i mogu se pretražiti.

**Zahtevi:**
- Svaka primljena poruka se upisuje u MariaDB (topic, payload, QoS, retain, timestamp).
- `GET /api/messages` sa filtriranjem po topicu i vremenskom opsegu, sa pagingom.
- `GET /api/messages/{id}`.
- **Jedan source of truth.** Nema in-memory liste koja odgovara na `GET /messages`.
- Indeks koji pokriva stvarni upit, ne „indeks za svaki slučaj".

**Odluke za odbranu:**
- Kako scoped `DbContext` dolazi do singleton MQTT handlera. (Postoji tačno jedan ispravan odgovor i dva popularna pogrešna — očekujem da znaš sva tri.)
- `DateTime` vs `DateTimeOffset`, i koja je stvarna preciznost kolone u MariaDB. Default te može iznenaditi.
- Payload: `string` ili `byte[]`? MQTT payload je binarni. Šta radiš sa porukom koja nije UTF-8? Postoji li gornja granica?
- Offset ili keyset paging.

**Zamke:**
- MQTTnet poziva handler **sekvencijalno**. Spor upis u bazu usporava prijem *svih* poruka. Izmeri to pre nego što tvrdiš da nije problem.
- Kad upis padne, poruka je izgubljena — a brokeru je QoS 1 ack već poslat. Broker misli da je isporučena. Šta radiš po tom pitanju (makar: šta loguješ)?
- Dva `DateTime.UtcNow` poziva za isti događaj daju dve različite vrednosti. Uzmi jednom.
- `pageSize=1000000`.
- Read-only upit koji nepotrebno trackuje entitete (znaš ovo iz `code-again` lekcije 0013 — ovde je realan volumen, pa se i vidi).

---

### F4 — Statistika u Redisu

**Cilj:** live statistika saobraćaja, sa rolling prozorom od 1h, koja ne obara API kad Redis padne.

**Zahtevi:**
- Per-topic brojači i ukupan throughput, rolling 1h prozor.
- `GET /api/stats` i `GET /api/stats/topic?filter=`.
- **Redis je derived state.** Ako Redis padne: ingest nastavlja, `/api/messages` radi, stats endpoint degradira (i to se vidi u health-u) — ne vraća `500`.

**Odluke za odbranu:**
- Koja Redis struktura nosi rolling prozor i zašto. (Naivni `INCR` + `EXPIRE` na svakom inkrementu **ne pravi** rolling prozor — pomera TTL unapred i key nikad ne istekne dok ima saobraćaja. To je bug u praktikantskom planu. Bucketed keys po minuti sa TTL-om, ili sorted set sa timestamp score-om + trim — izaberi i obrazloži.)
- Lifetime `ConnectionMultiplexer`-a.
- Da li se stats računa pri upisu (write path) ili pri čitanju (read path).

**Zamke:**
- Praktikantski plan ovde savetuje `Task.Run(...)` + `Task.WhenAll(...)` „da bude paralelno i brže". U async I/O kodu `Task.Run` ne dodaje ništa osim thread-pool skoka — treba da umeš da objasniš tačno zašto, jer je to omiljeno pitanje i omiljena greška.
- `Task.WhenAll` nad dva taska gde jedan pukne: drugi exception ti je progutan. Znaš li gde ode?
- Nekonzistentnost DB vs Redis posle pada jednog od njih — nije bug koji moraš da rešiš, ali jeste bug koji moraš da *imenuješ* u README-u.

---

### F5 — Integracioni testovi (Testcontainers)

**Cilj:** test koji diže pravi broker, pravu bazu i pravi Redis i dokazuje da lanac radi.

**Zahtevi:**
- `WebApplicationFactory` + Testcontainers (`MariaDb`, `Redis`, `Mosquitto`).
- Glavni scenario: publish preko API-ja → broker → ingest → red u bazi → vidljivo kroz `GET /api/messages`.
- Scenario za duplikat pretplate → `409`.
- Scenario za pad zavisnosti: ugasi Redis kontejner → stats degradira, ingest radi.
- Testovi prolaze u CI-ju.

**Odluke za odbranu:**
- Gde je granica unit vs integration testa u ovom projektu.
- Kontejneri po test-klasi ili po collection fixture-u.
- Čišćenje stanja između testova.

**Zamke:**
- Asinhroni lanac → `Thread.Sleep(2000)` i flake. Polling sa timeout-om, ne fiksno čekanje.
- Konfiguracija testnog hosta mora da pregazi connection stringove *pre* nego što se DI kontejner izgradi.
- Čekanje na readiness brokera nije isto što i „kontejner je startovan".
- Testcontainers u GitHub Actions traži Docker na runneru — proveri da workflow to ima.

---

### F6 — Observability

**Cilj:** kad servis krene da se ponaša čudno, iz logova i health-a se vidi šta je.

**Zahtevi:**
- Serilog, structured — polja (`topic`, `messageId`, `subscriptionId`), ne interpolirani stringovi.
- Health checks: broker, baza, Redis, i **„subscriptions restored"** kao zaseban signal.
- `Degraded` se koristi tamo gde zaista znači degradirano (Redis dole), `Unhealthy` tamo gde znači neupotrebljivo.
- Polly tamo gde ima smisla — i obrazloženje gde namerno **nema**.
- `/health/live` i `/health/ready` imaju različito značenje i to se vidi iz implementacije.

**Odluke za odbranu:**
- Zašto baš ta podela live/ready i šta orchestrator radi sa svakim od njih.
- Retry policy za konekciju vs za publish — zašto nisu isti.

**Zamke:**
- Health check koji vraća `Healthy` dok pretplate nisu obnovljene (nastavak nalaza iz F2).
- Health check koji sam obara servis: skup query na svaki poziv, bez timeout-a.
- Logovanje payload-a u celosti na `Information` nivou.

---

### F7 — Docker i isporuka

**Cilj:** `docker compose up` diže ceo sistem, uključujući aplikaciju, na čistoj mašini.

**Zahtevi:**
- Dockerfile (multi-stage ili `dotnet publish /t:PublishContainer` — tvoj izbor, obrazloži).
- Aplikacija u compose-u zajedno sa EMQX, MariaDB, Redis.
- Sva konfiguracija kroz env varijable; nijedan secret u repo-u.
- `depends_on` sa healthcheck-ovima — aplikacija ne puca ako baza kasni.
- README: setup, env varijable, kako se pokreće, kako se migracije primenjuju, poznata ograničenja.
- Postman collection ili `.http` fajl sa svim endpointima.

**Odluke za odbranu:**
- Kako migracije stižu u bazu u kontejnerskom svetu.
- Non-root user u imidžu — da ili ne, i zašto.

**Zamke:**
- `localhost` u connection stringu unutar kontejnera. Hostname je ime servisa iz compose-a.
- .NET 8+ imidži slušaju na portu 8080, ne 80/5000.
- Timezone/UTC u kontejneru — zato je `DateTime.UtcNow` iz F3 bio bitan.
- `.dockerignore` koji fali → build context od 500MB i `bin/obj` sa host mašine u imidžu.
- Kredencijali u compose fajlu umesto u `.env` koji je u `.gitignore`.

---

### F8 — Opciono (stretch, nije deo isporuke)

Namerno van scope-a, ali ostavljeno zapisano jer je to sledeći pravi korak:

- **`Channel<T>` ingest pipeline**: MQTT handler samo upisuje u kanal, consumer radi batch insert. Rešava i „spora baza usporava prijem" i „pad upisa gubi poruku" iz F3. Sa tim dolaze backpressure politika, graceful drain na shutdown i batch sizing.
- Outbox/idempotentnost za publish.
- Rate limiting na publish endpointu.

---

## 7. Definition of Done (ceo projekat)

- [ ] `git clone` + `docker compose up -d` na čistoj mašini daje radan sistem
- [ ] Svi endpointi iz §5 rade i vraćaju ProblemDetails za svaku grešku
- [ ] Pretplate i poruke preživljavaju restart; pretplate se obnavljaju na reconnect
- [ ] Pad brokera, baze ili Redisa — nijedan ne obara ceo servis; svaki se različito vidi
- [ ] Unit + integration testovi prolaze u CI-ju
- [ ] Nema secreta u git istoriji
- [ ] README dovoljan da neko ko nije ti digne projekat
- [ ] Umeš da odbraniš svaku „odluku za odbranu" iz svake faze, napamet

---

## 8. Review protokol

**Kad tražiš review:**

```
git push -u origin feature/f2-subscriptions
gh pr create --draft --title "..." --body-file ...
```

Onda mi kažeš: `review PR #3` (ili: `review feature/f2-subscriptions`), plus jedna rečenica o tome gde si nesiguran.

**PR opis — obavezna struktura:**

```markdown
## What
## Why  (koje odluke iz §6 si doneo i zašto)
## How to test  (konkretne komande, ne "pokreni i probaj")
## Migration / setup steps
## Open questions  (gde želiš da gledam pažljivo)
```

**Šta dobijaš od mene:** summary komentar sa P1 nalazima (reprodukovanim lokalno, sa logom), inline komentari za ostalo, i pitanja umesto odgovora tamo gde je poenta razumevanje. Nalazi su odvojeni na P1 (blokira merge) i nitpick.

**Šta ne dobijaš:** gotov kod.

**Pre nego što otvoriš PR**, prođi self-review checklist: `~/teach/mqtt-gateway/reference/pr-self-review-checklist.html`.
