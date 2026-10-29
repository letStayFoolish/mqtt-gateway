# MQTT Gateway

Servis koji premošćuje MQTT i HTTP. Klijenti koji ne govore MQTT kroz REST API mogu da objave poruku na topic, upravljaju pretplatama gateway-a, pretraže primljene poruke i vide statistiku saobraćaja.

Gateway je dugoživeći stateful servis: drži jednu konekciju ka brokeru kroz ceo životni vek procesa i preživljava pad brokera, baze i Redisa — svaki nezavisno.

ASP.NET Core (.NET 10) · Vertical Slice Architecture · MQTTnet 5 · EF Core 9 + MariaDB · Redis

Radni zadatak sa fazama i acceptance kriterijumima: [`docs/ASSIGNMENT.md`](docs/ASSIGNMENT.md).

## Status

| Faza | Stanje |
|---|---|
| F0 — kostur, infrastruktura, CI | u toku |
| F1 — MQTT konekcija i publish | — |
| F2 — pretplate i perzistencija | — |
| F3 — ingest i upit nad porukama | — |
| F4 — statistika u Redisu | — |
| F5 — integracioni testovi | — |
| F6 — observability | — |
| F7 — Docker i isporuka | — |

Trenutno radi: infrastruktura kroz Docker Compose i prazan Web API projekat. Još ne postoji: test projekti, CI workflow, nijedan endpoint.

## Preduslovi

- .NET SDK 10.0
- Docker i Docker Compose
- Opciono: [MQTTX](https://mqttx.app/) ili `mosquitto-clients` za ručno testiranje brokera

## Brzi start

```bash
cp .env.example .env     # pa popuni lozinke
docker compose up -d
docker compose ps        # sačekaj da sva tri servisa budu (healthy)
dotnet run --project src/MqttGateway.Api
```

`.env` je u `.gitignore` i nikada se ne commituje. `docker compose up` neće krenuti ako neka lozinka nije postavljena — varijable su deklarisane sa `${VAR:?...}` upravo zato da bi greška pukla odmah, a ne pri prvoj konekciji.

## Servisi i portovi

| Servis | Adresa | Namena |
|---|---|---|
| Web API | `http://localhost:5219` · `https://localhost:7202` | REST API (vidi `launchSettings.json`) |
| EMQX — MQTT | `127.0.0.1:1883` | MQTT TCP listener |
| EMQX — Dashboard | `http://127.0.0.1:18083` | dashboard i REST API brokera (`admin` / `$EMQX_DASHBOARD__DEFAULT_PASSWORD`) |
| MariaDB | `127.0.0.1:3306` | baza `mqtt_gateway_db` |
| Redis | `127.0.0.1:6379` | keš i izvedena statistika |

Svi portovi su vezani na `127.0.0.1`, ne na `0.0.0.0`. Vidi odluku o loopback binding-u ispod.

## Provera da infrastruktura radi

```bash
docker compose ps                                    # sva tri moraju reći (healthy)
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:18083/
docker exec mqtt-gateway-redis redis-cli ping        # PONG
docker exec mqtt-gateway-mariadb mariadb -u mqtt_gateway_user -p -e "select version();" mqtt_gateway_db
```

MQTT round-trip bez instaliranja klijenta na host:

```bash
docker run --rm --network mqtt-gateway_default eclipse-mosquitto:2.0 \
  mosquitto_sub -h emqx -t test/smoke -C 1 -W 10 &
docker run --rm --network mqtt-gateway_default eclipse-mosquitto:2.0 \
  mosquitto_pub -h emqx -t test/smoke -m "hello" -q 1
```

## Odluke

### EMQX 5.8.9 (Apache 2.0 linija)

Od verzije 5.9.0 EMQX je prešao na Business Source License 1.1 i prestao da objavljuje Docker Official Image; novije verzije postoje samo kao `emqx/emqx` pod BSL. BSL dozvoljava development, testiranje i single-node produkciju, pa nas njena ograničenja (klaster, hosted servis, preprodaja) ovde ne dodiruju — ali 5.8.x je poslednja linija pod Apache 2.0 i bira se da bi licenca ostala trivijalna.

Posledica koju treba imati na umu: ako se produkcija nekad vozi na 5.9+, lokalno okruženje više ne odgovara produkciji.

### MariaDB 11.4 LTS

Pomelo testira protiv MySQL 8.0/8.4 i MariaDB 11.6, 11.5, **11.4 (LTS)**, 11.3, 10.11, 10.6, 10.5. Najviši LTS u toj matrici je 11.4; novije linije (11.8, 12.x, 13.x) nisu testirane sa providerom koji koristimo. Dodatno, 13.0 nije LTS i community support mu ističe krajem 2026.

Isti broj ulazi i u `MariaDbServerVersion` u konfiguraciji EF Core-a — izbor taga i izbor server verzije u kodu su jedna odluka, ne dve.

### EF Core 9 na .NET 10

Projekat targetuje `net10.0`, ali koristi EF Core **9.0.x** pakete i `Pomelo.EntityFrameworkCore.MySql` 9.0.0. Pomelo 9.0.0 ima dependency `Microsoft.EntityFrameworkCore.Relational >= 9.0.0 && <= 9.0.999` — EF Core 10 nije podržan. EF Core 9 paketi rade na .NET 10 runtime-u bez problema; `dotnet-ef` tool takođe mora biti 9.x.

Postoji community fork sa EF Core 10 podrškom (`Microting.EntityFrameworkCore.MySql`) — svesno se ne koristi.

### Portovi vezani na loopback

Svi objavljeni portovi su `127.0.0.1:<port>:<port>`, a ne `"<port>:<port>"`. Kratka forma vezuje port na sve mrežne interfejse (`0.0.0.0`), što znači da bi baza i broker bili dostupni svakome na istoj mreži. Loopback forma zadržava pristup sa host mašine (DataGrip, MQTTX, `dotnet run`) bez izlaganja mreži.

Komunikacija između kontejnera ne zavisi od `ports:` — oni se nalaze preko imena servisa u Compose mreži.

### Redis bez persistent volume-a

Redis drži isključivo izvedeno stanje (brojači i statistika po topicu). Source of truth su poruke u MariaDB. Redis sme da padne bez posledica po ingest i po `/api/messages` — degradira samo statistika.

Zato nema volume: Redis startuje prazan pri svakom recreate-u, po dizajnu. Perzistentan keš bi tokom razvoja samo omogućio da ključevi iz opozvane šeme prežive restart i daju brojeve koji ne odgovaraju bazi.

### Dashboard lozinka kroz environment

EMQX default je `admin`/`public` uz obaveznu promenu pri prvom loginu, ali to stanje živi u `emqx_data` volume-u — `docker compose down -v` vraća default. `EMQX_DASHBOARD__DEFAULT_PASSWORD` iz `.env` daje isti, ponovljiv rezultat na svakom čistom checkout-u, konzistentno sa načinom na koji se konfiguriše MariaDB.

## Konfiguracija i tajne

| Gde | Čemu služi |
|---|---|
| `.env` | lozinke za Docker Compose; nije u gitu |
| `.env.example` | spisak varijabli sa placeholder vrednostima; jeste u gitu |
| `dotnet user-secrets` | connection stringovi za lokalni `dotnet run` |
| environment varijable | konfiguracija aplikacije u kontejneru (F7) |

Nijedna lozinka ne sme da završi u `appsettings.json`, u `docker-compose.yml`, ni u izlazu koji se lepi u PR — `docker compose config` renderuje vrednosti u plaintext-u.

## Česti problemi

**`docker compose ps` pokazuje `Up` bez `(healthy)`** — healthcheck ne prolazi. Pogledaj `docker compose logs <servis>`; EMQX-u treba oko 15 sekundi da postane spreman.

**`compose up` odmah puca sa porukom o varijabli** — nema `.env` ili je neka vrednost prazna. To je namerno ponašanje `${VAR:?}` sintakse.

**Port je zauzet** — proveri da nemaš lokalno instaliran MySQL/MariaDB na 3306 ili Redis na 6379.

**Statistika ne odgovara bazi** — Redis je keš bez perzistencije; posle izmene šeme ključeva `docker compose restart redis` ga vraća na prazno.

**Čist start od nule** — `docker compose down -v && docker compose up -d` briše i volumene (podatke baze i stanje brokera).

## Struktura repozitorijuma

```
docker-compose.yml          EMQX + MariaDB + Redis (aplikacija dolazi u F7)
MqttGateway.slnx
src/MqttGateway.Api/        Web API — Features/ po slice-u, Infrastructure/ za deljeno
docs/ASSIGNMENT.md          radni zadatak, faze i acceptance kriterijumi
```
